using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Worktown.Game.Climb;
using Worktown.Game.Net;
using Worktown.Game.Npc;
using Worktown.Game.Office;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 059 AC9 and AC11: the wire grows only by OfficeTerminal's three synced fields (no RPC, no broadcast; Revision 8), the screen's
    /// texts follow the no-table rule, and every new player-facing text passes the D27 word check.
    /// </summary>
    public class OfficeSurfaceTests
    {
        public static readonly IReadOnlyList<string> OfficeFields = new[]
        {
            "sync Worktown.Game.Office.OfficeTerminal.Special:FishNet.Object.Synchronizing.SyncVar`1<System.Byte>",
            "sync Worktown.Game.Office.OfficeTerminal.History:FishNet.Object.Synchronizing.SyncList`1<Worktown.Game.Office.ServiceRecord>",
            "sync Worktown.Game.Office.OfficeTerminal.Log:FishNet.Object.Synchronizing.SyncList`1<System.UInt32>",
        };

        [Test]
        public void TheWire_GrowsOnlyByTheOfficeTerminalsThreeFields()
        {
            List<string> lines = ProtocolSurface.Lines(typeof(NetBootstrap).Assembly);
            foreach (string field in OfficeFields) Assert.Contains(field, lines);
            CollectionAssert.AreEquivalent(OfficeFields, lines.Where(l => l.Contains("Worktown.Game.Office.")).ToList(), "nothing else of the office");
            Assert.AreEqual(3, lines.Count(l => l.StartsWith("broadcast ", StringComparison.Ordinal)), "still the three connection messages");
            Assert.AreEqual(9, ProtocolSurface.Revision); // spec 062b made it 9
            Assert.Contains("revision 9", lines);
        }

        private static IEnumerable<string> Screens()
        {
            var history = new List<ServiceRecord>();
            for (int day = 1; day <= 6; day++) history.Add(ServiceRecord.Of(day, 12 + day, 10 + day, 2, 9000, 500, 36 + day, 30 + day, new[] { 9, 1, 2, 3, 4, 5 }));
            var log = new[] { "Mira: fries $4.50", "Tobi: falafel wrap is the special", "Lu: drinks off the menu next" };
            var rows = new string[TerminalOptions.ScreenRows];
            var buttons = new string[TerminalOptions.ScreenRows * OfficeTerminalView.RowButtons];
            var appRows = new int[TerminalOptions.ScreenRows];
            var words = new string[TerminalOptions.ScreenRows * TerminalOptions.ActionCount];
            foreach (byte stage in new[] { ShopStageRules.OpenKitchen, ShopStageRules.FoodCorner, ShopStageRules.FirstRestaurant })
                foreach (TerminalApp app in new[] { TerminalApp.Menu, TerminalApp.Services, TerminalApp.Results })
                    for (int page = 0; page < 6; page++)
                    {
                        int p = page;
                        string header = OfficeTerminalView.Compose(app, ref p, stage, 0b1111, 0b0111, 0b1011, null, Menu.SeitanWrap, history, log,
                            FoodCornerRules.DefaultClimb, rows, buttons, appRows, words, out int pages);
                        Assert.Less(p, pages);
                        yield return header;
                        foreach (string row in rows) yield return row;
                        foreach (string b in buttons) yield return b;
                        foreach (string w in words) if (w != null) yield return w;
                    }
        }

        [Test]
        public void TheScreen_HasNoTable()
        {
            foreach (string text in Screens())
                if (!string.IsNullOrEmpty(text)) Assert.IsTrue(ResultsRules.PassesNoTable(text), "'" + text + "'");
            var rows = new string[TerminalOptions.ScreenRows];
            var buttons = new string[TerminalOptions.ScreenRows * OfficeTerminalView.RowButtons];
            var appRows = new int[TerminalOptions.ScreenRows];
            var words = new string[TerminalOptions.ScreenRows * TerminalOptions.ActionCount];
            int page = 0;
            OfficeTerminalView.Compose(TerminalApp.Menu, ref page, ShopStageRules.FirstRestaurant, 0b1001, 0b1001, 0b1001, null, Menu.None, null, null,
                FoodCornerRules.DefaultClimb, rows, buttons, appRows, words, out int menuPages);
            Assert.AreEqual(1, menuPages);
            Assert.AreEqual("Fries  $4.00  on", rows[0]);
            Assert.AreEqual(string.Empty, buttons[2], "the fries have no on/off button");
            Assert.AreEqual("on/off", buttons[OfficeTerminalView.RowButtons + 2], "the falafel wrap has");
            Assert.AreEqual(string.Empty, buttons[2 * OfficeTerminalView.RowButtons + 3], "a drink has no special button");
            Assert.IsNull(words[TerminalOptions.Press], "the row itself does nothing in the Menu app");
            Assert.AreEqual(5, appRows.Count(r => r >= 0));
            page = 99;
            OfficeTerminalView.Compose(TerminalApp.Menu, ref page, ShopStageRules.OpenKitchen, 0, 0, 0, null, Menu.None, null, null,
                FoodCornerRules.DefaultClimb, rows, buttons, appRows, words, out int openPages);
            Assert.AreEqual(3, openPages, "16 lines over three screens");
            Assert.AreEqual(2, page, "a page past the end shows the last one");
        }

        [Test]
        public void EveryNewText_PassesTheD27WordCheck()
        {
            var texts = new List<string>
            {
                MenuAppRules.AlwaysOnText, ServicesAppRules.ComesLaterText, SpecialRules.NotOnMenuText, SpecialRules.NoSpecialText, SpecialRules.Tag,
                ResultsRules.NoServiceText, ResultsRules.NoServiceHint, ResultsRules.NoChangesText, ResultsRules.SameText, ResultsRules.MoreText,
                ResultsRules.FewerText, ResultsRules.QuickerText, ResultsRules.SlowerText, ResultsRules.BetterText, ResultsRules.WorseText,
                ResultsRules.RivalText(250), ResultsRules.PageTitle(0), ResultsRules.PageTitle(1), ResultsRules.PageTitle(2),
                ClipboardRules.Title, ClipboardRules.MenuTitle, ClipboardRules.LastServiceTitle, ClipboardRules.CloseHint("J"),
                ChangeLog.UnknownName, Shop.MenuBoardRules.SpecialTag, Worktown.Game.UI.ControlsHelp.Text(Settings.ControlBindings.Defaults()),
                Cooking.StationRules.Prompt(Cooking.StationKind.OfficeTerminal, "Office terminal", "E", "V"),
            };
            for (int a = 0; a < TerminalOptions.ActionCount; a++) texts.Add(MenuAppRules.ButtonWords(a, 0) ?? string.Empty);
            foreach (TerminalApp app in new[] { TerminalApp.Menu, TerminalApp.Services, TerminalApp.Results }) texts.Add(OfficeTerminalView.AppName(app));
            for (int action = 1; action <= 4; action++) texts.Add(ChangeLog.Text(ChangeLog.Pack(1, action, 1), _ => "Mira"));
            texts.AddRange(Screens());
            foreach (string text in texts)
            {
                Assert.IsNotNull(text);
                Assert.IsNull(ContentRules.FindForbidden(text), text);
            }
        }
    }
}
