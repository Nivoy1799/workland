using NUnit.Framework;
using Worktown.Game.Office;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 059 AC2: a terminal press packs app, row and action and unpacks them again; anything else is refused.</summary>
    public class TerminalOptionsTests
    {
        [Test]
        public void EveryApp_Row_AndAction_RoundTrips()
        {
            for (int app = 0; app < TerminalOptions.AppCount; app++)
                for (int row = 0; row <= TerminalOptions.MaxRow; row++)
                    for (int action = 0; action < TerminalOptions.ActionCount; action++)
                    {
                        int option = TerminalOptions.Pack((TerminalApp)app, row, action);
                        Assert.AreEqual(app << 12 | row << 4 | action, option);
                        Assert.IsTrue(TerminalOptions.TryUnpack(option, out TerminalApp a, out int r, out int x), option.ToString());
                        Assert.AreEqual((app, row, action), ((int)a, r, x));
                    }
        }

        [Test]
        public void UnknownValues_AreRefused()
        {
            Assert.AreEqual(-1, TerminalOptions.Pack((TerminalApp)3, 0, 0), "no fourth app yet");
            Assert.AreEqual(-1, TerminalOptions.Pack(TerminalApp.Menu, -1, 0));
            Assert.AreEqual(-1, TerminalOptions.Pack(TerminalApp.Menu, 256, 0));
            Assert.AreEqual(-1, TerminalOptions.Pack(TerminalApp.Menu, 0, 5));
            Assert.AreEqual(-1, TerminalOptions.Pack(TerminalApp.Menu, 0, -1));
            foreach (int option in new[] { -1, 3 << 12, 1 << 20, int.MaxValue, 5, 0xF, 1 << 12 | 7 })
                Assert.IsFalse(TerminalOptions.TryUnpack(option, out _, out _, out _), option.ToString());
        }

        [Test]
        public void TheScreensSlots_AreTabsPagesAndRowButtons()
        {
            for (int app = 0; app < TerminalOptions.AppCount; app++)
            {
                Assert.IsTrue(TerminalOptions.IsTabSlot(TerminalOptions.TabSlot((TerminalApp)app), out TerminalApp tab));
                Assert.AreEqual((TerminalApp)app, tab);
            }
            Assert.IsTrue(TerminalOptions.IsPageSlot(TerminalOptions.PrevPageSlot, out int back));
            Assert.AreEqual(-1, back);
            Assert.IsTrue(TerminalOptions.IsPageSlot(TerminalOptions.NextPageSlot, out int on));
            Assert.AreEqual(1, on);
            for (int slot = 5; slot < TerminalOptions.FirstRowSlot; slot++)
                Assert.IsFalse(TerminalOptions.IsTabSlot(slot, out _) || TerminalOptions.IsPageSlot(slot, out _) || TerminalOptions.TryRowSlot(slot, out _, out _));
            for (int row = 0; row < TerminalOptions.ScreenRows; row++)
                for (int action = 0; action < TerminalOptions.ActionCount; action++)
                {
                    int slot = TerminalOptions.RowSlot(row, action);
                    Assert.IsTrue(TerminalOptions.TryRowSlot(slot, out int r, out int a));
                    Assert.AreEqual((row, action), (r, a));
                    // The client's page turns a screen row into the app's row.
                    Assert.AreEqual(TerminalOptions.Pack(TerminalApp.Menu, 2 * TerminalOptions.ScreenRows + row, action),
                        TerminalOptions.OptionFor(slot, TerminalApp.Menu, 2));
                }
            Assert.AreEqual(-1, TerminalOptions.OptionFor(TerminalOptions.TabSlot(TerminalApp.Results), TerminalApp.Menu, 0), "a tab sends nothing");
            Assert.AreEqual(-1, TerminalOptions.OptionFor(TerminalOptions.SlotCount, TerminalApp.Menu, 0));
            Assert.AreEqual(1, TerminalOptions.Pages(0));
            Assert.AreEqual(1, TerminalOptions.Pages(6));
            Assert.AreEqual(3, TerminalOptions.Pages(16), "the open kitchen's 16 lines");
        }
    }
}
