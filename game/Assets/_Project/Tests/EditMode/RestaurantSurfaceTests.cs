using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Worktown.Game.Climb;
using Worktown.Game.Net;
using Worktown.Game.UI;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 057 AC9 and AC14: the wire grows only by ShopStage's restaurant fields (no RPC, no broadcast; Revision 7, 054b takes 6), and every
    /// new player-facing text passes the D27 word check.
    /// </summary>
    public class RestaurantSurfaceTests
    {
        /// <summary>The synced fields spec 057 adds (the spec's seven, plus the window's pips, the crew size of D40 and the last rent paid).</summary>
        public static readonly IReadOnlyList<string> RestaurantFields = new[]
        {
            "sync Worktown.Game.Climb.ShopStage.MovePending:FishNet.Object.Synchronizing.SyncVar`1<System.Boolean>",
            "sync Worktown.Game.Climb.ShopStage.RecipesOwned:FishNet.Object.Synchronizing.SyncVar`1<System.Byte>",
            "sync Worktown.Game.Climb.ShopStage.MenuNext:FishNet.Object.Synchronizing.SyncVar`1<System.Byte>",
            "sync Worktown.Game.Climb.ShopStage.MenuNow:FishNet.Object.Synchronizing.SyncVar`1<System.Byte>",
            "sync Worktown.Game.Climb.ShopStage.WindowAverageTenths:FishNet.Object.Synchronizing.SyncVar`1<System.Int16>",
            "sync Worktown.Game.Climb.ShopStage.WindowCount:FishNet.Object.Synchronizing.SyncVar`1<System.Byte>",
            "sync Worktown.Game.Climb.ShopStage.WindowPips:FishNet.Object.Synchronizing.SyncVar`1<System.Byte>",
            "sync Worktown.Game.Climb.ShopStage.RentOwed:FishNet.Object.Synchronizing.SyncVar`1<System.Int64>",
            "sync Worktown.Game.Climb.ShopStage.CrewPlayers:FishNet.Object.Synchronizing.SyncVar`1<System.Byte>",
            "sync Worktown.Game.Climb.ShopStage.RentLast:FishNet.Object.Synchronizing.SyncVar`1<System.Int64>", // 057 review: the summary's rent
        };

        [Test]
        public void TheWire_GrowsOnlyByTheRestaurantsSyncedFields()
        {
            List<string> lines = ProtocolSurface.Lines(typeof(NetBootstrap).Assembly);
            foreach (string field in RestaurantFields) Assert.Contains(field, lines);
            Assert.IsFalse(lines.Any(l => l.Contains("Worktown.Game.Climb.") && !l.StartsWith("sync ", StringComparison.Ordinal)), "no RPC on the stage");
            Assert.IsFalse(lines.Any(l => l.Contains("GoalBoard")), "the goal board syncs nothing of its own (its piece's fields only)");
            Assert.AreEqual(3, lines.Count(l => l.StartsWith("broadcast ", StringComparison.Ordinal)), "still the three connection messages");
            Assert.AreEqual(8, ProtocolSurface.Revision); // spec 059 made it 8
            Assert.Contains("revision 8", lines);
        }

        [Test]
        public void EveryNewText_PassesTheD27WordCheck()
        {
            var texts = new List<string>
            {
                GoalBoardRules.Name, GoalBoardRules.RentedText, GoalBoardRules.MovingText, GoalBoardRules.RestaurantName, GoalBoardRules.NextServiceText,
                GoalBoardRules.MenuFullText(4), GoalBoardRules.OrderStockText, GoalBoardRules.ConfirmText, GoalBoardRules.MoveWaitsText,
                GoalBoardRules.PressWords(0), GoalBoardRules.PressWords(2), GoalBoardRules.BoughtText(2, "Falafel Wrap Recipe", 2700),
                GoalBoardRules.RentPaidText(1, 4100), GoalBoardRules.ToggledText("Falafel wrap", true), GoalBoardRules.ToggledText("Drinks", false),
                GateRules.ReachedText, GateRules.GateLine(32, 5, 5, 35, 4120, 16400), GateRules.PipText(0b101, 4, 5), RentRules.PaidText(1200),
                RentRules.OwedText(3050), RentRules.PayFirstText, FoodCornerRules.GoalReachedText,
                HudView.GoalText(ShopStageRules.FirstRestaurant, 4120, 16400, false, 32, 3, 300),
                ShiftSummaryView.RestaurantText(4120, 16400, false, 32, 3, 0b011, 300, 1200),
                Cooking.StationRules.Prompt(Cooking.StationKind.GoalBoard, GoalBoardRules.Name, "E", "V"),
            };
            foreach (string row in GoalBoardView.Texts(2, 4120, 16400, false, 0b0011, 0b0111, 0b0011, 300, 32, 3, 1, false,
                         Sim.Economy.EconomyCatalog.CreateDefault(), FoodCornerRules.DefaultClimb))
                texts.Add(row);
            foreach (string row in GoalBoardView.Texts(1, 100, 4100, false, 0, 0, 0, 0, 0, 0, 1, false, Sim.Economy.EconomyCatalog.CreateDefault(),
                         FoodCornerRules.DefaultClimb))
                texts.Add(row);
            foreach (string text in texts)
            {
                Assert.IsNotNull(text);
                Assert.IsNull(ContentRules.FindForbidden(text), text);
            }
            StringAssert.DoesNotContain("later update", FoodCornerRules.GoalReachedText, "the restaurant is in this build (D28)");
        }
    }
}
