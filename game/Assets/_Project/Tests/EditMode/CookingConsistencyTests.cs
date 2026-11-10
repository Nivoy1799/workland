using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Game.Npc;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 043 in the kitchen: an undercooked menu dish is Close, not Exact (the time buckets follow the zones); the ring's Perfect
    /// starts where the sim rates best, also for a mix whose parts reach their Perfect halves at different times; the look on the
    /// heat follows the zone, not the seconds.
    /// </summary>
    public class CookingConsistencyTests
    {
        private static readonly ItemFactory Factory = new ItemFactory(new ItemCatalog(DemoIngredients.Catalog), OperationCatalog.CreateDefault());
        private const double Scale = CookingTime.TimeScale;

        private static Item Base(string id) => Factory.CreateBase(id).Item;

        private static Item Apply(string op, IReadOnlyDictionary<string, ParamValue> parameters, params Item[] inputs)
        {
            OperationResult r = Factory.Apply(op, inputs, parameters);
            Assert.IsTrue(r.Ok, op + ": " + r.Error);
            return r.Item;
        }

        private static Item Cut(Item food, string shape) =>
            Apply("cut", new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice(shape) }, food);

        private static Item DeepFried(Item food, float kitchenSeconds) => Apply(DeepFry.OperationId, new Dictionary<string, ParamValue>
        {
            ["temp"] = ParamValue.Number(DeepFry.ReferenceTempC),
            ["time"] = ParamValue.Number(kitchenSeconds * (float)Scale),
            [DeepFry.ShakeParam] = ParamValue.Choice(DeepFry.NotShaken),
            [DeepFry.DrainParam] = ParamValue.Choice(DeepFry.Drained),
        }, food);

        private static Item Griddled(Item food, float sideKitchenSeconds) =>
            Apply(Griddle.OperationFor(food), Griddle.Params(sideKitchenSeconds * (float)Scale, sideKitchenSeconds * (float)Scale), food);

        private static Item Boiled(Item food, float simSeconds) =>
            Apply("boil", new Dictionary<string, ParamValue> { ["time"] = ParamValue.Number(simSeconds) }, food);

        private static Item Plate(params Item[] foods) => Apply("plate", new Dictionary<string, ParamValue>(), foods);

        private static void AssertMatch(OrderMatch expected, Item served, byte dish, string why) =>
            Assert.AreEqual(expected, OrderRules.Match(served, Menu.ById(dish)), Menu.NameOf(dish) + ": " + why + " (" + KitchenState.NameOf(served) + ")");

        [Test]
        public void AC2_UndercookedMenuDishes_AreClose_DoneOnesExact()
        {
            Item sticks = Cut(Base("potato"), "sticks");
            AssertMatch(OrderMatch.Exact, Plate(DeepFried(sticks, 30f)), Menu.Fries, "golden fries");
            AssertMatch(OrderMatch.Close, Plate(DeepFried(sticks, 20f)), Menu.Fries, "pale fries (Good from 25 s)");

            AssertMatch(OrderMatch.Exact, Plate(DeepFried(Base("falafel_mix"), 25f)), Menu.Falafel, "golden falafel");
            AssertMatch(OrderMatch.Close, Plate(DeepFried(Base("falafel_mix"), 15f)), Menu.Falafel, "pale falafel (Good from 20 s)");

            Item paprika = Cut(Base("paprika"), "sticks"), eggplant = Cut(Base("eggplant"), "slices");
            AssertMatch(OrderMatch.Exact, Plate(Griddled(paprika, 12f), Griddled(eggplant, 12f)), Menu.GrilledVeggies, "both grilled");
            // 9 s a side was "medium" before 043 (an exact order); the window starts at 10 s.
            AssertMatch(OrderMatch.Close, Plate(Griddled(paprika, 9f), Griddled(eggplant, 12f)), Menu.GrilledVeggies, "half-grilled paprika");

            AssertMatch(OrderMatch.Exact, Plate(Griddled(Base("flatbread"), 5f)), Menu.ToastedFlatbread, "toasted");
            AssertMatch(OrderMatch.Close, Plate(Griddled(Base("flatbread"), 3f)), Menu.ToastedFlatbread, "warm (Good from 4 s)");

            Item halves = Cut(Base("potato"), "halves");
            AssertMatch(OrderMatch.Exact, Plate(Boiled(halves, 420f)), Menu.PotatoSoup, "boiled");
            // 300 sim s was "medium" before 043; at the 2x pot the halves are not in their window yet.
            AssertMatch(OrderMatch.Close, Plate(Boiled(halves, 200f)), Menu.PotatoSoup, "lightly boiled");
        }

        [Test]
        public void ThePerfectCue_StartsWhereEveryPartRatesBest_AlsoForAMix()
        {
            // Flatbread (window 0.4..0.8, Perfect from 0.6) and mushroom (0.3..0.7, Perfect from 0.5) fried together: Good when the
            // flatbread reaches 0.4, overdone when the mushroom passes 0.7. The middle of that (0.55) is not Perfect for the flatbread;
            // spec 043 measures Perfect where both are in their Perfect halves (the flatbread at 0.6).
            var kitchen = KitchenState.CreateDemo();
            Item mix = Apply("mix", new Dictionary<string, ParamValue>(), Cut(Base("flatbread"), "halves"), Cut(Base("mushroom"), "halves"));
            Item FriedFor(double s) => Apply("fry", new Dictionary<string, ParamValue>
            {
                ["temp"] = ParamValue.Number(StoveSettings.PanTempC),
                ["time"] = ParamValue.Number((float)s),
            }, mix);
            HeatTimeline line = kitchen.FreshTimeline(mix, "fry", StoveSettings.PanTempC);
            double middle = line.GoodFrom + HeatZoneRules.PerfectShareOfWindow * (line.End - line.GoodFrom);
            TestContext.WriteLine(line.ToString() + ", 027c middle " + middle.ToString("0.0"));

            Assert.Greater(line.PerfectFrom, middle + 1.0, "the measured Perfect comes after the middle of the window");
            Assert.Less(line.PerfectFrom, line.OverdoneFrom);
            Assert.AreEqual(DonenessZone.Perfect, DonenessZones.Of(FriedFor(line.PerfectFrom + 0.05).Components));
            Assert.AreEqual(DonenessZone.Good, DonenessZones.Of(FriedFor(line.PerfectFrom - 0.5).Components));

            // The best rating over the whole range lies in the ring's Perfect zone.
            double bestT = 0; float best = -1f;
            for (double t = 10; t <= 600; t += 0.5)
            {
                float q = ItemRater.Rate(FriedFor(t)).Quality;
                if (q > best) { best = q; bestT = t; }
            }
            Assert.AreEqual(HeatZone.Perfect, HeatZoneRules.Zone(bestT, line), "best at " + bestT + " sim s");
        }

        [Test]
        public void TheLookOnTheHeat_FollowsTheZone_NotTheSeconds()
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient("potato", out Item potato));
            kitchen.Register(42, potato);
            HeatTestKit.SwitchOn(kitchen, HeatTestKit.PanIndex, HeatTestKit.PanBurner);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutOnHeat(42, HeatTestKit.PanIndex, VesselKind.Pan, HeatTestKit.StartTick));

            // A whole potato after 100 sim s: the seconds alone say "medium" (from 60 s), but it is still undercooked (Good from about
            // 130 s at the 2x pan), so it looks and is named lightly fried, and the ring says Cooking.
            uint at100 = HeatTestKit.StartTick + HeatTestKit.TicksFor(100);
            Assert.IsTrue(OperationCatalog.CreateDefault().TryGet("fry", out OperationDef fry));
            Assert.IsTrue(CookingTime.TryGetTimeParam(fry, out ParamDef time));
            Assert.AreEqual("medium", CookingTime.BucketId(time, 100));
            Assert.AreEqual(FoodLook.DonenessShort, kitchen.HeatLook(HeatTestKit.PanIndex, at100, HeatTestKit.TickRate).Value.Doneness);
            Assert.AreEqual(HeatZone.Cooking, kitchen.HeatReadingOf(HeatTestKit.PanIndex, at100, HeatTestKit.TickRate).Zone);
            StringAssert.Contains("lightly fried potato", kitchen.ResultName(HeatTestKit.PanIndex, VesselKind.Pan, at100, HeatTestKit.TickRate));

            // At 170 sim s it is Perfect: medium look, the plain name.
            uint at170 = HeatTestKit.StartTick + HeatTestKit.TicksFor(170);
            Assert.AreEqual(FoodLook.DonenessMedium, kitchen.HeatLook(HeatTestKit.PanIndex, at170, HeatTestKit.TickRate).Value.Doneness);
            Assert.AreEqual(HeatZone.Perfect, kitchen.HeatReadingOf(HeatTestKit.PanIndex, at170, HeatTestKit.TickRate).Zone);
            Assert.AreEqual("crispy fried potato", kitchen.ResultName(HeatTestKit.PanIndex, VesselKind.Pan, at170, HeatTestKit.TickRate));
        }

        [Test]
        public void ThePaceTable_IsTheKitchensClock()
        {
            Assert.AreEqual(CookingTime.TimeScale, DeepFry.KitchenTimeScale, "the pace table's kitchen seconds are the kitchen's");
            Assert.AreEqual(2f, CookingPace.PanPotSpeed);
        }
    }
}
