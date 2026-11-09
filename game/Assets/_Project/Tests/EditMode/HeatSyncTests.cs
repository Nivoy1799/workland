using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 027c AC3: the server sends the two heat bytes only when they change, at most 4 times per second. Drives the
    /// server's path (KitchenState's reading through the HeatSync gate, one call per tick as KitchenService does) for a
    /// potato burning in a pan, and with the burner off.
    /// </summary>
    public class HeatSyncTests
    {
        private const int FoodId = 42;
        private const int Pan = HeatTestKit.PanIndex;
        private const uint Start = HeatTestKit.StartTick;
        private const double Rate = HeatTestKit.TickRate;

        private struct Sent
        {
            public uint Tick;
            public byte Progress;
            public byte Zone;
        }

        /// <summary>Every send of the gate, calling it once per tick from <paramref name="from"/> for <paramref name="ticks"/>.</summary>
        private static List<Sent> Run(KitchenState kitchen, HeatSync sync, uint from, uint ticks)
        {
            var sent = new List<Sent>();
            for (uint tick = from; tick < from + ticks; tick++)
            {
                HeatReading reading = kitchen.HeatReadingOf(Pan, tick, Rate);
                byte zone = HeatZoneRules.Encode(reading);
                if (sync.ShouldSend(tick, Rate, reading.Progress, zone))
                    sent.Add(new Sent { Tick = tick, Progress = reading.Progress, Zone = zone });
            }
            return sent;
        }

        private static KitchenState PotatoInPan(bool on)
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient("potato", out Item potato));
            kitchen.Register(FoodId, potato);
            if (on) HeatTestKit.SwitchOn(kitchen, Pan, HeatTestKit.PanBurner);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(FoodId, Pan, VesselKind.Pan, Start));
            return kitchen;
        }

        [Test]
        public void BurningPotato_SendsOnlyChanges_AtMost4PerSecond()
        {
            KitchenState kitchen = PotatoInPan(true);
            var sync = new HeatSync();
            uint ticks = (uint)(90 * Rate); // 90 real seconds: raw to burnt
            List<Sent> sent = Run(kitchen, sync, Start, ticks);

            Assert.Greater(sent.Count, 20, "the ring moves while it cooks");
            uint minTicks = HeatSync.MinTicks(Rate);
            Assert.AreEqual(8u, minTicks, "30 Hz / 4 = 7.5, rounded up");
            for (int i = 1; i < sent.Count; i++)
            {
                Assert.GreaterOrEqual(sent[i].Tick - sent[i - 1].Tick, minTicks, "two sends too close at tick " + sent[i].Tick);
                Assert.IsTrue(sent[i].Progress != sent[i - 1].Progress || sent[i].Zone != sent[i - 1].Zone, "a send without a change");
            }
            for (uint second = Start; second < Start + ticks; second += (uint)Rate)
            {
                int inSecond = sent.FindAll(s => s.Tick >= second && s.Tick < second + (uint)Rate).Count;
                Assert.LessOrEqual(inSecond, HeatSync.MaxPerSecond, "sends in the second from tick " + second);
            }
            Assert.AreEqual(HeatZone.Burnt, HeatZoneRules.ZoneOf(sent[sent.Count - 1].Zone), "it ends burnt");
            Assert.AreEqual(255, sent[sent.Count - 1].Progress);
            // The zones arrive in cooking order, the warning among them.
            var zones = new List<HeatZone>();
            bool warned = false;
            foreach (Sent s in sent)
            {
                HeatZone z = HeatZoneRules.ZoneOf(s.Zone);
                if (zones.Count == 0 || zones[zones.Count - 1] != z) zones.Add(z);
                warned |= HeatZoneRules.WarnOf(s.Zone);
            }
            CollectionAssert.AreEqual(new[] { HeatZone.Raw, HeatZone.Cooking, HeatZone.Good, HeatZone.Perfect, HeatZone.Overdone, HeatZone.Burnt }, zones);
            Assert.IsTrue(warned);
        }

        [Test]
        public void BurnerOff_NothingChanges_NothingIsSent()
        {
            KitchenState kitchen = PotatoInPan(false);
            var sync = new HeatSync();
            List<Sent> sent = Run(kitchen, sync, Start, (uint)(20 * Rate));
            Assert.AreEqual(1, sent.Count, "only the first value (raw, 0)");
            Assert.AreEqual(HeatZone.Raw, HeatZoneRules.ZoneOf(sent[0].Zone));
        }

        [Test]
        public void Gate_SameValueNever_ChangeWaitsForTheInterval()
        {
            var sync = new HeatSync();
            Assert.IsTrue(sync.ShouldSend(100, 30, 10, 2), "the first value");
            Assert.IsFalse(sync.ShouldSend(104, 30, 11, 2), "a change 4 ticks later waits");
            Assert.IsFalse(sync.ShouldSend(107, 30, 11, 2));
            Assert.IsTrue(sync.ShouldSend(108, 30, 11, 2), "0.25 s after the last send");
            Assert.AreEqual(11, sync.Progress);
            Assert.IsFalse(sync.ShouldSend(500, 30, 11, 2), "unchanged, however late");
            Assert.IsTrue(sync.ShouldSend(5, 30, 12, 2), "the tick counter went back (a restart)");
            sync.Reset();
            Assert.IsTrue(sync.ShouldSend(6, 30, 12, 2), "after a reset the value goes out again");
            Assert.AreEqual(1u, HeatSync.MinTicks(0), "bad tick rate");
        }
    }
}
