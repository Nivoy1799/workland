using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 027c AC2: zone boundaries, monotonic progress, the warning exactly 3 s before Overdone, bad input; and the
    /// timeline the server builds from the real sim (potato in the pan and in the pot).
    /// </summary>
    public class HeatZoneRulesTests
    {
        private const int FoodId = 42;
        private const int Pan = HeatTestKit.PanIndex;
        private const int Pot = HeatTestKit.PotIndex;
        private const uint Start = HeatTestKit.StartTick;
        private const double Rate = HeatTestKit.TickRate;

        // cook from 10, good from 100, window ends (overdone) at 200, burnt at 250 sim s; Perfect from 150.
        private static readonly HeatTimeline Line = new HeatTimeline(10, 100, 200, 200, 250);
        private static readonly HeatTimeline NeverOvercooks =
            new HeatTimeline(10, 10, 400, double.PositiveInfinity, double.PositiveInfinity);

        [Test]
        public void Zone_Boundaries()
        {
            Assert.AreEqual(150.0, Line.PerfectFrom, 1e-9);
            Assert.AreEqual(HeatZone.Raw, HeatZoneRules.Zone(0, Line));
            Assert.AreEqual(HeatZone.Raw, HeatZoneRules.Zone(9.999, Line));
            Assert.AreEqual(HeatZone.Cooking, HeatZoneRules.Zone(10, Line));
            Assert.AreEqual(HeatZone.Cooking, HeatZoneRules.Zone(99.999, Line));
            Assert.AreEqual(HeatZone.Good, HeatZoneRules.Zone(100, Line));
            Assert.AreEqual(HeatZone.Good, HeatZoneRules.Zone(149.999, Line));
            Assert.AreEqual(HeatZone.Perfect, HeatZoneRules.Zone(150, Line));
            Assert.AreEqual(HeatZone.Perfect, HeatZoneRules.Zone(199.999, Line));
            Assert.AreEqual(HeatZone.Overdone, HeatZoneRules.Zone(200, Line));
            Assert.AreEqual(HeatZone.Overdone, HeatZoneRules.Zone(249.999, Line));
            Assert.AreEqual(HeatZone.Burnt, HeatZoneRules.Zone(250, Line));
            Assert.AreEqual(HeatZone.Burnt, HeatZoneRules.Zone(1e6, Line));
            Assert.AreEqual(HeatZone.Perfect, HeatZoneRules.Zone(1e6, NeverOvercooks), "a window that ends at 1 never overcooks");
        }

        [Test]
        public void Progress_IsMonotonic_AndAnchoredAtTheGoodWindow()
        {
            foreach (HeatTimeline line in new[] { Line, NeverOvercooks })
            {
                byte previous = 0;
                for (double t = 0; t <= 500; t += 0.25)
                {
                    byte p = HeatZoneRules.Progress(t, line);
                    Assert.GreaterOrEqual(p, previous, "progress fell at " + t + " s");
                    previous = p;
                }
            }
            Assert.AreEqual(0, HeatZoneRules.Progress(0, Line));
            Assert.AreEqual((byte)System.Math.Round(HeatZoneRules.GoodShare * 255), HeatZoneRules.Progress(100, Line), "good starts at the fixed share");
            Assert.AreEqual(255, HeatZoneRules.Progress(200, Line), "full at the end of the good window");
            Assert.AreEqual(255, HeatZoneRules.Progress(300, Line));
            Assert.Less(HeatZoneRules.Progress(199, Line), 255);
        }

        [Test]
        public void Warning_FiresExactly3sBeforeOverdone()
        {
            Assert.AreEqual(3.0, HeatZoneRules.WarnRealSeconds);
            Assert.AreEqual(3.0 * CookingTime.TimeScale, HeatZoneRules.WarnSimSeconds);
            double at = Line.OverdoneFrom - HeatZoneRules.WarnSimSeconds; // 182 sim s
            Assert.IsFalse(HeatZoneRules.Warn(at - 0.001, Line));
            Assert.IsTrue(HeatZoneRules.Warn(at, Line));
            Assert.IsTrue(HeatZoneRules.Warn(Line.OverdoneFrom - 0.001, Line));
            Assert.IsFalse(HeatZoneRules.Warn(Line.OverdoneFrom, Line), "overdone itself is no longer a warning");
            for (double t = 0; t < 1000; t += 1) Assert.IsFalse(HeatZoneRules.Warn(t, NeverOvercooks));
        }

        [Test]
        public void BadInput_IsSafe()
        {
            Assert.AreEqual(HeatZone.Raw, HeatZoneRules.Zone(double.NaN, Line));
            Assert.AreEqual(HeatZone.Raw, HeatZoneRules.Zone(-5, Line));
            Assert.AreEqual(0, HeatZoneRules.Progress(double.NaN, Line));
            Assert.AreEqual(0, HeatZoneRules.Progress(-5, Line));
            Assert.IsFalse(HeatZoneRules.Warn(double.NaN, Line));

            HeatTimeline none = HeatZoneRules.Build(null, t => null);
            Assert.AreEqual(HeatZone.Raw, HeatZoneRules.Zone(1e6, none), "no time parameter: never leaves Raw");
            Assert.AreEqual(0, HeatZoneRules.Progress(1e6, none));
            var time = new ParamDef("time", ParamKind.Number, 10f, 600f, new BucketDef[0]);
            HeatTimeline refused = HeatZoneRules.Build(time, t => null);
            Assert.AreEqual(HeatZone.Raw, HeatZoneRules.Zone(1e6, refused), "a refusing sim: never leaves Raw");
            Assert.AreEqual(HeatZone.Raw, HeatZoneRules.Zone(1e6, HeatZoneRules.Build(time, null)));

            foreach (HeatZone zone in System.Enum.GetValues(typeof(HeatZone)))
            {
                Assert.AreEqual(zone, HeatZoneRules.ZoneOf(HeatZoneRules.Encode(zone, false)));
                Assert.AreEqual(zone, HeatZoneRules.ZoneOf(HeatZoneRules.Encode(zone, true)));
                Assert.IsTrue(HeatZoneRules.WarnOf(HeatZoneRules.Encode(zone, true)));
                Assert.IsFalse(HeatZoneRules.WarnOf(HeatZoneRules.Encode(zone, false)));
            }
            Assert.AreEqual(HeatZone.None, HeatZoneRules.ZoneOf(0x0F), "unknown zone value");
            Assert.AreEqual(0, HeatZoneRules.Encode((HeatZone)99, false));
        }

        [Test]
        public void Build_FindsTheWindow_FromALinearDoneness()
        {
            // Doneness 0.005 per sim second, window 0.5..0.8: good at 100, overdone at 160, burnt (0.9) at 180.
            var time = new ParamDef("time", ParamKind.Number, 10f, 600f, new BucketDef[0]);
            var ideal = new DonenessWindow(0.5f, 0.8f);
            HeatTimeline line = HeatZoneRules.Build(time, t => new[] { new ItemComponent("x", System.Math.Min(1f, 0.005f * t), ideal) });
            Assert.AreEqual(10, line.CookFrom, 1e-6);
            Assert.AreEqual(100, line.GoodFrom, 0.05, "within the bisection and doneness tolerance");
            Assert.AreEqual(160, line.OverdoneFrom, 0.05);
            Assert.AreEqual(160, line.End, 0.05);
            Assert.AreEqual(180, line.BurntFrom, 0.05);
            // A window that ends at 1: never overdone or burnt; the end is where it is fully done.
            HeatTimeline open = HeatZoneRules.Build(time, t => new[] { new ItemComponent("x", System.Math.Min(1f, 0.005f * t), new DonenessWindow(0f, 1f)) });
            Assert.AreEqual(10, open.GoodFrom, 1e-6);
            Assert.IsTrue(double.IsPositiveInfinity(open.OverdoneFrom));
            Assert.IsTrue(double.IsPositiveInfinity(open.BurntFrom));
            Assert.AreEqual(200, open.End, 0.05);
        }

        [Test]
        public void Combine_SlowestDecides_UntilOneOvercooks()
        {
            var raw = new HeatReading(HeatZone.Cooking, 40, false);
            var good = new HeatReading(HeatZone.Good, 180, true);
            var over = new HeatReading(HeatZone.Overdone, 255, false);
            HeatReading both = HeatZoneRules.Combine(HeatZoneRules.Combine(HeatReading.Empty, raw), good);
            Assert.AreEqual(HeatZone.Cooking, both.Zone);
            Assert.AreEqual(40, both.Progress);
            Assert.IsTrue(both.Warn);
            HeatReading burnt = HeatZoneRules.Combine(both, over);
            Assert.AreEqual(HeatZone.Overdone, burnt.Zone);
            Assert.AreEqual(255, burnt.Progress);
            Assert.AreEqual(HeatZone.None, HeatZoneRules.Combine(HeatReading.Empty, HeatReading.Empty).Zone);
        }

        // ---- the real sim ----

        private static KitchenState WithPotatoIn(VesselKind kind, int vessel, int burner)
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient("potato", out Item potato));
            kitchen.Register(FoodId, potato);
            HeatTestKit.SwitchOn(kitchen, vessel, burner);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(FoodId, vessel, kind, Start));
            return kitchen;
        }

        private static float DonenessAt(KitchenState kitchen, int vessel, VesselKind kind, double simSeconds)
        {
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTakeOut(vessel, kind, Start + HeatTestKit.TicksFor(simSeconds), Rate, out Item item));
            return item.Components[0].Doneness;
        }

        [Test]
        public void WholePotato_InThePan_ZonesMatchTheSimWindow()
        {
            KitchenState kitchen = WithPotatoIn(VesselKind.Pan, Pan, HeatTestKit.PanBurner);
            Assert.IsTrue(kitchen.TryGetHeat(Pan, 0, out HeatSession session));
            HeatTimeline line = session.Timeline;
            TestContext.WriteLine("potato in the pan: " + line + "; real s: good " + (line.GoodFrom / CookingTime.TimeScale).ToString("0.0")
                + ", perfect " + (line.PerfectFrom / CookingTime.TimeScale).ToString("0.0") + ", overdone "
                + (line.OverdoneFrom / CookingTime.TimeScale).ToString("0.0") + ", burnt " + (line.BurntFrom / CookingTime.TimeScale).ToString("0.0"));
            // The sim's own doneness either side of each threshold (potato window 0.55..0.85).
            Assert.Less(DonenessAt(kitchen, Pan, VesselKind.Pan, line.GoodFrom - 1), 0.55f);
            Assert.GreaterOrEqual(DonenessAt(kitchen, Pan, VesselKind.Pan, line.GoodFrom + 1), 0.55f);
            Assert.LessOrEqual(DonenessAt(kitchen, Pan, VesselKind.Pan, line.OverdoneFrom - 1), 0.85f);
            Assert.Greater(DonenessAt(kitchen, Pan, VesselKind.Pan, line.OverdoneFrom + 1), 0.85f);
            Assert.Greater(line.BurntFrom, line.OverdoneFrom);
            Assert.Less(line.BurntFrom, 600, "a whole potato burns in the pan within the op's time range");

            uint Tick(double s) => Start + HeatTestKit.TicksFor(s);
            Assert.AreEqual(HeatZone.Raw, kitchen.HeatReadingOf(Pan, Start, Rate).Zone);
            Assert.AreEqual(HeatZone.Cooking, kitchen.HeatReadingOf(Pan, Tick(60), Rate).Zone);
            Assert.AreEqual(HeatZone.Perfect, kitchen.HeatReadingOf(Pan, Tick(line.OverdoneFrom - 5), Rate).Zone);
            Assert.IsTrue(kitchen.HeatReadingOf(Pan, Tick(line.OverdoneFrom - 5), Rate).Warn);
            Assert.AreEqual(HeatZone.Burnt, kitchen.HeatReadingOf(Pan, Tick(line.BurntFrom + 1), Rate).Zone);
            Assert.AreEqual(HeatZone.None, kitchen.HeatReadingOf(Pot, Start, Rate).Zone, "an empty vessel");
        }

        [Test]
        public void WholePotato_InThePot_TakesLonger_ThanInThePan()
        {
            KitchenState pan = WithPotatoIn(VesselKind.Pan, Pan, HeatTestKit.PanBurner);
            KitchenState pot = WithPotatoIn(VesselKind.Pot, Pot, HeatTestKit.PotBurner);
            Assert.IsTrue(pan.TryGetHeat(Pan, 0, out HeatSession fried));
            Assert.IsTrue(pot.TryGetHeat(Pot, 0, out HeatSession boiled));
            HeatTimeline line = boiled.Timeline;
            TestContext.WriteLine("potato in the pot: " + line + "; real s: good " + (line.GoodFrom / CookingTime.TimeScale).ToString("0.0")
                + ", perfect " + (line.PerfectFrom / CookingTime.TimeScale).ToString("0.0") + ", overdone "
                + (line.OverdoneFrom / CookingTime.TimeScale).ToString("0.0") + ", burnt " + (line.BurntFrom / CookingTime.TimeScale).ToString("0.0"));
            Assert.Greater(line.GoodFrom, fried.Timeline.GoodFrom);
            Assert.Greater(line.OverdoneFrom, fried.Timeline.OverdoneFrom);
            Assert.AreEqual(30, line.CookFrom, 1e-6, "boil's minimum time");
        }
    }
}
