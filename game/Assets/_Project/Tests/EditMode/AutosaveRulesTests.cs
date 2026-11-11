using NUnit.Framework;
using Worktown.Game.Save;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 053 AC4: who saves when.</summary>
    public class AutosaveRulesTests
    {
        private static readonly SaveTrigger[] Moments =
            { SaveTrigger.Created, SaveTrigger.ServiceEnd, SaveTrigger.GoalReached, SaveTrigger.HostClosing, SaveTrigger.AppQuit };

        [Test]
        public void EachTrigger_SavesOnTheServerWithASlot()
        {
            foreach (SaveTrigger t in Moments)
            {
                Assert.IsTrue(AutosaveRules.ShouldSave(t, true, true, 0f, false), t.ToString());
                Assert.IsTrue(AutosaveRules.ShouldSave(t, true, true, 0f, true), t + " during a service");
            }
            Assert.IsTrue(AutosaveRules.ShouldSave(SaveTrigger.Periodic, true, true, 300f, true));
        }

        [Test]
        public void AClientOrARunWithoutASlot_NeverSaves()
        {
            foreach (SaveTrigger t in System.Enum.GetValues(typeof(SaveTrigger)))
            {
                Assert.IsFalse(AutosaveRules.ShouldSave(t, false, true, 1000f, true), "a client: " + t);
                Assert.IsFalse(AutosaveRules.ShouldSave(t, true, false, 1000f, true), "no slot: " + t);
            }
        }

        [Test]
        public void Periodic_OnlyAfter300sWhileAServiceIsOpen()
        {
            Assert.AreEqual(300f, AutosaveRules.PeriodicSeconds);
            Assert.IsFalse(AutosaveRules.ShouldSave(SaveTrigger.Periodic, true, true, 299.9f, true));
            Assert.IsTrue(AutosaveRules.ShouldSave(SaveTrigger.Periodic, true, true, 300f, true));
            Assert.IsFalse(AutosaveRules.ShouldSave(SaveTrigger.Periodic, true, true, 5000f, false), "no service open");
        }

        [Test]
        public void TheSavedDay_IsTheNextDayToPlay()
        {
            Assert.AreEqual(3, AutosaveRules.NextDay(true, 3, true, 2, 3), "during day 3's service: day 3 again");
            Assert.AreEqual(4, AutosaveRules.NextDay(false, 3, true, 3, 3), "after day 3's service closed: day 4");
            Assert.AreEqual(1, AutosaveRules.NextDay(false, 0, false, 0, 1), "before the first service");
            Assert.AreEqual(5, AutosaveRules.NextDay(false, 4, true, 4, 5), "the night passed already: the clock's day");
        }
    }
}
