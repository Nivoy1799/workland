using NUnit.Framework;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Game.Office;
using Worktown.Sim.Climb;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 059 AC5: one row per 052 channel; the counter stays on; the stage 3 channels refuse with their text. (The door row and the build
    /// row join 062b's build: 059 ships after 062a without them.)
    /// </summary>
    public class ServicesAppRulesTests
    {
        private static ClimbCatalog Climb => FoodCornerRules.DefaultClimb;

        [Test]
        public void TheRows_AreTheCounterAndTheStage3Channels()
        {
            CollectionAssert.AreEqual(new[] { "Counter", "Delivery", "Custom orders", "Tables", "Buffet" }, ServicesAppRules.Names(Climb));
            Assert.AreEqual("Counter: open", ServicesAppRules.RowText(0, Climb));
            Assert.AreEqual("Delivery: comes with stage 3", ServicesAppRules.RowText(1, Climb));
            Assert.AreEqual("Custom orders: comes with stage 3", ServicesAppRules.RowText(2, Climb));
            Assert.AreEqual("Buffet: comes with stage 3", ServicesAppRules.RowText(4, Climb));
            Assert.AreEqual(string.Empty, ServicesAppRules.RowText(5, Climb));
            Assert.AreEqual("Comes with stage 3", ServicesAppRules.ComesLaterText);
            foreach (ChannelDef c in Climb.Channels)
                if (c.Id != "corner_counter" && c.Id != "street_counter") Assert.AreEqual(ClimbStage.ServiceMix, c.Stage, c.Id + " comes with stage 3");
        }

        [Test]
        public void LockedChannels_RefuseWithTheirText_AndTheCounterStaysOn()
        {
            Assert.AreEqual(CookOutcome.AlwaysOn, ServicesAppRules.Decide(0, TerminalOptions.Press, Climb));
            for (int row = 1; row < ServicesAppRules.RowCount(Climb); row++)
                Assert.AreEqual(CookOutcome.ComesLater, ServicesAppRules.Decide(row, TerminalOptions.Press, Climb), row.ToString());
            Assert.AreEqual(CookOutcome.InvalidOption, ServicesAppRules.Decide(5, TerminalOptions.Press, Climb));
            Assert.AreEqual(CookOutcome.InvalidOption, ServicesAppRules.Decide(1, TerminalOptions.Toggle, Climb), "only the row itself");
            Assert.AreEqual("the counter stays open", ServicesAppRules.PressWords(0, Climb));
            Assert.AreEqual("delivery comes with stage 3", ServicesAppRules.PressWords(1, Climb));
        }
    }
}
