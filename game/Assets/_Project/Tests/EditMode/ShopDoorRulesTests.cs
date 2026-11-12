using NUnit.Framework;
using Worktown.Game.Climb;
using Worktown.Game.Layout;
using Worktown.Game.Npc;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 062b AC3: the shop door: every state and press, closing at the summary's end, no hour while closed.</summary>
    public class ShopDoorRulesTests
    {
        [Test]
        public void EveryStateAndPress()
        {
            Assert.AreEqual(ShopDoorState.ClosingAfterService, ShopDoorRules.Next(ShopDoorState.Open, true, true), "during a service: after it");
            Assert.AreEqual(ShopDoorState.Closed, ShopDoorRules.Next(ShopDoorState.Open, false, true), "between services: at once");
            Assert.AreEqual(ShopDoorState.Open, ShopDoorRules.Next(ShopDoorState.ClosingAfterService, true, true), "closing: back to open");
            Assert.AreEqual(ShopDoorState.Open, ShopDoorRules.Next(ShopDoorState.ClosingAfterService, false, true));
            Assert.AreEqual(ShopDoorState.Open, ShopDoorRules.Next(ShopDoorState.Closed, false, true), "closed: open (the next service starts)");
            Assert.AreEqual(ShopDoorState.Open, ShopDoorRules.Next(ShopDoorState.Closed, true, true));
            foreach (ShopDoorState s in new[] { ShopDoorState.Open, ShopDoorState.ClosingAfterService, ShopDoorState.Closed })
            {
                Assert.AreEqual(s, ShopDoorRules.Next(s, true, false), "no press, no change");
                Assert.AreEqual(s, ShopDoorRules.Next(s, false, false));
            }
        }

        [Test]
        public void ClosingBecomesClosed_AtTheSummaryEnd()
        {
            Assert.AreEqual(ShopDoorState.Closed, ShopDoorRules.AfterSummary(ShopDoorState.ClosingAfterService));
            Assert.AreEqual(ShopDoorState.Open, ShopDoorRules.AfterSummary(ShopDoorState.Open));
            Assert.AreEqual(ShopDoorState.Closed, ShopDoorRules.AfterSummary(ShopDoorState.Closed));
        }

        [Test]
        public void OnlyAClosedShop_HoldsTheClock_AndOpensNoService()
        {
            Assert.IsTrue(ShopDoorRules.HoldsClock(ShopDoorState.Closed, false));
            Assert.IsFalse(ShopDoorRules.HoldsClock(ShopDoorState.Closed, true), "a service that runs ends first");
            Assert.IsFalse(ShopDoorRules.HoldsClock(ShopDoorState.ClosingAfterService, false));
            Assert.IsFalse(ShopDoorRules.HoldsClock(ShopDoorState.Open, false));
            Assert.IsFalse(ShopDoorRules.MayOpenService(ShopDoorState.Closed));
            Assert.IsTrue(ShopDoorRules.MayOpenService(ShopDoorState.ClosingAfterService));
            Assert.IsTrue(ShopDoorRules.MayOpenService(ShopDoorState.Open));
            Assert.IsTrue(ShopDoorRules.AllowsBuild(ShopDoorState.Closed));
            Assert.IsFalse(ShopDoorRules.AllowsBuild(ShopDoorState.ClosingAfterService));
        }

        /// <summary>
        /// The server's loop for 600 simulated seconds while closed: EconomyService's clock (60 s an hour) does not advance while the door holds
        /// it, and the shift (CustomerService.OnHour with the door's MayOpenService) opens no service at 08:00. Then the door opens: the shift
        /// opens at once and the hours run again.
        /// </summary>
        [Test]
        public void WhileClosed_NoHourPasses_In600SimulatedSeconds()
        {
            var shift = new Shift();
            long tick = 8; // 08:00 of day 1
            float intoHour = 0f, now = 0f;
            const float dt = 1f / 30f;
            ShopDoorState door = ShopDoorState.Closed;
            int hours = 0;
            for (int frame = 0; frame < 600 * 30; frame++)
            {
                now += dt;
                shift.OnClock(tick, now, 0, ShopDoorRules.MayOpenService(door));
                bool held = !shift.IsOpen && !shift.SummaryShowing && Shift.IsShiftHour(tick) && ShopDoorRules.HoldsClock(door, false);
                if (!held) intoHour += dt;
                if (intoHour < Worktown.Game.Economy.EconomyRules.SecondsPerHour) continue;
                intoHour -= Worktown.Game.Economy.EconomyRules.SecondsPerHour;
                tick++;
                hours++;
            }
            Assert.AreEqual(0, hours, "no hour passed");
            Assert.IsFalse(shift.IsOpen, "no service opened");
            door = ShopDoorRules.Next(door, shift.IsOpen, true);
            Assert.IsTrue(shift.OnClock(tick, now, 0, ShopDoorRules.MayOpenService(door)));
            Assert.IsTrue(shift.IsOpen, "opening starts the service at once (the clock waited at 08:00)");
            Assert.AreEqual(8, Shift.HourOf(tick));
        }

        [Test]
        public void TheSign_AndThePromptWords()
        {
            Assert.AreEqual("OPEN", ShopDoorRules.SignText(ShopDoorState.Open));
            Assert.AreEqual("CLOSED", ShopDoorRules.SignText(ShopDoorState.Closed));
            Assert.AreEqual("close after this service", ShopDoorRules.PressWords(ShopDoorState.Open, true));
            Assert.AreEqual("close the shop", ShopDoorRules.PressWords(ShopDoorState.Open, false));
            Assert.AreEqual("keep the shop open", ShopDoorRules.PressWords(ShopDoorState.ClosingAfterService, true));
            Assert.AreEqual("open the shop", ShopDoorRules.PressWords(ShopDoorState.Closed, false));
            foreach (string text in new[] { ShopDoorRules.ClosedLine, ShopDoorRules.ClosingLine, ShopDoorRules.OpenedLine, ShopDoorRules.ClosingSign })
                Assert.IsNull(ContentRules.FindForbidden(text), text);
            Assert.IsTrue(ShopStageRules.IsOpen(ShopStageRules.FoodCorner, Cooking.StationKind.DoorSign, 0), "the sign works at every stage");
        }
    }
}
