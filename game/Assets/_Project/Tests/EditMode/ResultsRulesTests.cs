using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Npc;
using Worktown.Game.Office;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 059 AC6: Results in words and bars: the literal of each comparison band, bars scaled to the largest count, and every line passes
    /// the no-table check (no "|", no tab, at most 40 characters, at most one column of numbers).
    /// </summary>
    public class ResultsRulesTests
    {
        private static ServiceRecord Rec(int day, int came, int served, int wait, int stars, params int[] sold) =>
            ServiceRecord.Of(day, came, served, came - served, served * 900L, served * 50L, stars, wait, sold);

        /// <summary>A bar line as the screen shows it: the name in 16 columns, the marks in 10, two spaces, the count.</summary>
        private static string Bar(string name, int marks, int count) => name.PadRight(16) + new string('#', marks).PadRight(10) + "  " + count;

        [Test]
        public void TheBands_ReadAsWords()
        {
            Assert.AreEqual("About the same.", ResultsRules.Band(110, 100, ResultsRules.MoreText, ResultsRules.FewerText), "within 10 %");
            Assert.AreEqual("About the same.", ResultsRules.Band(90, 100, ResultsRules.MoreText, ResultsRules.FewerText));
            Assert.AreEqual("More than usual.", ResultsRules.Band(111, 100, ResultsRules.MoreText, ResultsRules.FewerText));
            Assert.AreEqual("Fewer than usual.", ResultsRules.Band(89, 100, ResultsRules.MoreText, ResultsRules.FewerText));
            Assert.AreEqual("About the same.", ResultsRules.Band(0, 0, ResultsRules.MoreText, ResultsRules.FewerText));

            var history = new List<ServiceRecord> { Rec(1, 10, 10, 50, 40, 10), Rec(2, 10, 10, 50, 40, 10), Rec(3, 15, 15, 40, 45, 14, 0, 0, 3, 2) };
            List<string> page = ResultsRules.Page1(history);
            Assert.AreEqual("15 came, 15 served. More than usual.", page[0]);
            Assert.AreEqual(Bar("Fries", 10, 14), page[1]);
            Assert.AreEqual(Bar("Falafel wrap", 2, 3), page[2]);
            Assert.AreEqual(Bar("Drinks", 1, 2), page[3]);
            Assert.AreEqual("Waits about 40 s. Quicker than usual.", page[4]);
            Assert.AreEqual("4.5 stars. Better than usual.", page[5]);
            Assert.AreEqual(6, page.Count, "no rival line while 058 is not in (-1)");

            history.Add(Rec(4, 10, 8, 60, 30, 8));
            page = ResultsRules.Page1(history);
            Assert.AreEqual("10 came, 8 served. Fewer than usual.", page[0]);
            Assert.AreEqual("Waits about 60 s. Slower than usual.", page[2]);
            Assert.AreEqual("3.0 stars. Worse than usual.", page[3]);
            history.Add(Rec(5, 11, 11, 47, 39, 11));
            page = ResultsRules.Page1(history);
            Assert.AreEqual("11 came, 11 served. About the same.", page[0]);
            Assert.AreEqual("Waits about 47 s. About the same.", page[2]);
            Assert.AreEqual("3.9 stars. About the same.", page[3]);

            List<string> first = ResultsRules.Page1(new[] { Rec(1, 4, 3, 30, 42, 3) });
            Assert.AreEqual("4 came, 3 served.", first[0], "nothing to compare with yet");
            Assert.AreEqual("4.2 stars.", first[3]);
            CollectionAssert.AreEqual(new[] { ResultsRules.NoServiceText, ResultsRules.NoServiceHint }, ResultsRules.Page1(null));
            List<string> rival = ResultsRules.Page1(new[] { ServiceRecord.Of(1, 36, 9, 0, 0, 0, 40, 30, null, 250) });
            Assert.AreEqual("You won 25 % of the guests in town.", rival[rival.Count - 1], "058's line once it is known");
        }

        [Test]
        public void Bars_ScaleToTheLargestCount()
        {
            Assert.AreEqual(new string('#', 10), ResultsRules.Bar(14, 14));
            Assert.AreEqual(new string('#', 5), ResultsRules.Bar(7, 14));
            Assert.AreEqual("#", ResultsRules.Bar(1, 200), "a count above 0 shows one mark");
            Assert.AreEqual(string.Empty, ResultsRules.Bar(0, 14));
            Assert.AreEqual(new string('#', 10), ResultsRules.Bar(300, 300));
            var history = new List<ServiceRecord>();
            for (int day = 1; day <= 7; day++) history.Add(Rec(day, day * 2, day * 2, 30, 40, day));
            List<string> page2 = ResultsRules.Page2(history);
            Assert.AreEqual(ServiceHistory.Shown, page2.Count, "the last 5");
            Assert.AreEqual(Bar("Day 3", 4, 6), page2[0], "oldest first");
            Assert.AreEqual(Bar("Day 7", 10, 14), page2[4], "newest at the bottom, the largest bar");
            CollectionAssert.AreEqual(new[] { ResultsRules.NoChangesText }, ResultsRules.Page3(null));
            CollectionAssert.AreEqual(new[] { "Mira: fries $4.50" }, ResultsRules.Page3(new[] { "Mira: fries $4.50" }));
        }

        [Test]
        public void EveryLine_PassesTheNoTableCheck()
        {
            Assert.IsFalse(ResultsRules.PassesNoTable("Fries | 14"));
            Assert.IsFalse(ResultsRules.PassesNoTable("Fries\t14"));
            Assert.IsFalse(ResultsRules.PassesNoTable(new string('a', 41)));
            Assert.IsFalse(ResultsRules.PassesNoTable("Fries  14  $56.00"), "two columns of numbers");
            Assert.AreEqual(1, ResultsRules.NumberColumns("Fries           ##########  14"));
            var history = new List<ServiceRecord>();
            for (int day = 1; day <= 9; day++)
                history.Add(ServiceRecord.Of(day * 1000, 999, 255 + day, 300, 999999, 99999, 50, 9999, new[] { 255, 254, 3, 2, 1, 255 }));
            var lines = new List<string>();
            lines.AddRange(ResultsRules.Page1(history));
            lines.AddRange(ResultsRules.Page2(history));
            lines.AddRange(ResultsRules.Page1(null));
            var log = new List<string>();
            foreach (int line in new[] { 0, 5, 10, 15 })
                log.Add(ChangeLog.Text(ChangeLog.Price(30, line, 102350), _ => "A very long player name indeed"));
            lines.AddRange(ResultsRules.Page3(log));
            for (int i = 0; i < ResultsRules.SoldLines; i++) lines.Add(ResultsRules.BarLine(ResultsRules.SoldName(i), 255, 255));
            foreach (string line in lines) Assert.IsTrue(ResultsRules.PassesNoTable(line), "'" + line + "'");
            Assert.AreEqual("Fries", ResultsRules.SoldName(0));
            Assert.AreEqual(ResultsRules.DrinksLine, ResultsRules.SoldLineOf(Menu.None, true));
            Assert.AreEqual(3, ResultsRules.SoldLineOf(Menu.FalafelWrap, false));
            Assert.AreEqual(ResultsRules.OtherLine, ResultsRules.SoldLineOf(Menu.PotatoSoup, false));
        }
    }
}
