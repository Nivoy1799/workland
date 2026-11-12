using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Climb;
using Worktown.Game.Npc;
using Worktown.Game.Office;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 059: the pocket clipboard shows today's menu with its prices and the special, the goal line and Results page 1.</summary>
    public class ClipboardRulesTests
    {
        [Test]
        public void TheClipboard_ReadsLikeANote()
        {
            var prices = new List<int>();
            foreach (MenuPrices.Line l in MenuPrices.Lines) prices.Add((int)l.Reference);
            prices[MenuPrices.LineOfDish(Menu.Fries)] = 450;
            var history = new[] { ServiceRecord.Of(3, 15, 12, 3, 9000, 500, 38, 40, new[] { 9, 0, 0, 3 }) };
            List<string> lines = ClipboardRules.Lines(ShopStageRules.FirstRestaurant, FoodCornerRules.DefaultClimb, 0b1001, 0b0001, prices, Menu.FalafelWrap,
                "Next: the service licence\nRent owed: $12.00", history);
            Assert.AreEqual(ClipboardRules.MenuTitle, lines[0]);
            Assert.AreEqual("Fries  $4.50", lines[1]);
            Assert.AreEqual("Falafel wrap  $9.00  special", lines[2]);
            Assert.AreEqual(2 + 1, lines.IndexOf(string.Empty), "the drinks are off this service: not listed");
            CollectionAssert.Contains(lines, "Rent owed: $12.00");
            CollectionAssert.Contains(lines, ClipboardRules.LastServiceTitle);
            CollectionAssert.Contains(lines, "15 came, 12 served.");
            foreach (string line in lines)
                if (line.Length > 0 && !line.StartsWith("Next") && !line.StartsWith("Rent")) Assert.IsTrue(ResultsRules.PassesNoTable(line), line);

            List<string> corner = ClipboardRules.Lines(ShopStageRules.FoodCorner, FoodCornerRules.DefaultClimb, 0, 0, null, Menu.None, string.Empty, null);
            CollectionAssert.AreEqual(new[] { ClipboardRules.MenuTitle, "Fries  $4.00", string.Empty, ClipboardRules.LastServiceTitle,
                ResultsRules.NoServiceText, ResultsRules.NoServiceHint }, corner);
            Assert.AreEqual("J or Esc puts it away", ClipboardRules.CloseHint("J"));
        }
    }
}
