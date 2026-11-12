using System;
using System.Collections.Generic;
using System.Text;
using Worktown.Game.Npc;

namespace Worktown.Game.Office
{
    /// <summary>
    /// Spec 059, pure: the Results app in plain words and simple bars, never a table: no line has more than one column of numbers, no "|"
    /// or tab, and at most <see cref="MaxChars"/> characters (<see cref="PassesNoTable"/>). Page 1 is the last service, one fact per line,
    /// each compared with the usual (<see cref="ServiceHistory.TryUsual"/>: within 10 % "About the same.", else more or fewer, quicker or
    /// slower, better or worse); page 2 the last 5 services as one bar each (guests served, newest at the bottom); page 3 the change log.
    /// </summary>
    public static class ResultsRules
    {
        public const int MaxChars = 40;
        public const int BarWidth = 10;
        public const char BarChar = '#';
        public const int Pages = 3;

        /// <summary>The sold counts a ServiceRecord keeps: the four restaurant dishes in MenuPrices order, the drinks, every other dish.</summary>
        public const int SoldLines = 6;
        public const int DrinksLine = 4;
        public const int OtherLine = 5;

        private static readonly byte[] SoldDishes = { Menu.Fries, Menu.GrilledVeggies, Menu.SeitanWrap, Menu.FalafelWrap };
        private const int NamePad = 16;

        public const string NoServiceText = "No service yet.";
        public const string NoServiceHint = "How it went shows here.";
        public const string NoChangesText = "No changes yet.";
        public const string SameText = "About the same.";

        /// <summary>The title of Results page <paramref name="page"/> (0, 1, 2).</summary>
        public static string PageTitle(int page)
        {
            switch (page)
            {
                case 0: return "The last service";
                case 1: return "The last services";
                default: return "Who changed what";
            }
        }

        /// <summary>The sold line of a handed over dish or drink.</summary>
        public static int SoldLineOf(byte dish, bool drink)
        {
            if (drink) return DrinksLine;
            for (int i = 0; i < SoldDishes.Length; i++) if (SoldDishes[i] == dish) return i;
            return OtherLine;
        }

        /// <summary>The name of sold line <paramref name="i"/>: "Fries", "Grilled veggies", "Seitan wrap", "Falafel wrap", "Drinks", "Other dishes".</summary>
        public static string SoldName(int i)
        {
            if (i >= 0 && i < SoldDishes.Length) return Menu.NameOf(SoldDishes[i]);
            return i == DrinksLine ? "Drinks" : "Other dishes";
        }

        /// <summary><paramref name="text"/> in at most <see cref="MaxChars"/> characters: double spaces made single first, then cut.</summary>
        public static string Fit(string text)
        {
            if (text == null) return string.Empty;
            while (text.Length > MaxChars && text.Contains("  ")) text = text.Replace("  ", " ");
            return text.Length > MaxChars ? text.Substring(0, MaxChars) : text;
        }

        /// <summary>A bar of at most <see cref="BarWidth"/> marks, scaled to <paramref name="max"/> (a count above 0 shows at least one).</summary>
        public static string Bar(int count, int max)
        {
            if (count <= 0 || max <= 0) return string.Empty;
            int marks = (int)Math.Round(Math.Min(count, max) * (double)BarWidth / max, MidpointRounding.AwayFromZero);
            return new string(BarChar, Math.Max(1, Math.Min(BarWidth, marks)));
        }

        /// <summary>"Fries           ##########  14": the name, the bar (scaled to <paramref name="max"/>), the count.</summary>
        public static string BarLine(string name, int count, int max) =>
            Fit((name ?? string.Empty).PadRight(NamePad) + Bar(count, max).PadRight(BarWidth) + "  " + Math.Max(0, count));

        /// <summary>
        /// The comparison of <paramref name="value"/> with the usual: within 10 % "About the same.", else <paramref name="above"/> or
        /// <paramref name="below"/>.
        /// </summary>
        public static string Band(int value, int usual, string above, string below)
        {
            long diff = (long)value - usual;
            if (Math.Abs(diff) * 10 <= Math.Abs((long)usual)) return SameText;
            return diff > 0 ? above : below;
        }

        public const string MoreText = "More than usual.";
        public const string FewerText = "Fewer than usual.";
        public const string QuickerText = "Quicker than usual.";
        public const string SlowerText = "Slower than usual.";
        public const string BetterText = "Better than usual.";
        public const string WorseText = "Worse than usual.";

        /// <summary>Page 1: the last service, one fact per line (a hint when there is none yet).</summary>
        public static List<string> Page1(IReadOnlyList<ServiceRecord> history)
        {
            var lines = new List<string>();
            if (!ServiceHistory.TryLast(history, out ServiceRecord last))
            {
                lines.Add(NoServiceText);
                lines.Add(NoServiceHint);
                return lines;
            }
            string guests = last.Came + " came, " + last.Served + " served.";
            if (ServiceHistory.TryUsual(history, r => r.Served, out int usualServed)) guests += " " + Band(last.Served, usualServed, MoreText, FewerText);
            lines.Add(Fit(guests));
            int max = 0;
            for (int i = 0; i < SoldLines; i++) max = Math.Max(max, last.SoldAt(i));
            for (int i = 0; i < SoldLines; i++)
                if (last.SoldAt(i) > 0) lines.Add(BarLine(SoldName(i), last.SoldAt(i), max));
            if (last.WaitSeconds >= 0)
            {
                string wait = "Waits about " + last.WaitSeconds + " s.";
                if (ServiceHistory.TryUsual(history, r => r.WaitSeconds, out int usualWait)) wait += " " + Band(last.WaitSeconds, usualWait, SlowerText, QuickerText);
                lines.Add(Fit(wait));
            }
            if (last.StarsTenths >= 0)
            {
                string stars = Worktown.Game.Climb.GateRules.Stars(last.StarsTenths) + " stars.";
                if (ServiceHistory.TryUsual(history, r => r.StarsTenths, out int usualStars)) stars += " " + Band(last.StarsTenths, usualStars, BetterText, WorseText);
                lines.Add(Fit(stars));
            }
            if (last.RivalPermille >= 0) lines.Add(RivalText(last.RivalPermille)); // spec 058; hidden while unknown (-1)
            return lines;
        }

        /// <summary>Spec 058's town line (the follow-up fills RivalPermille): "You won 25 % of the guests in town."</summary>
        public static string RivalText(int permille) => Fit("You won " + (Math.Max(0, Math.Min(1000, permille)) + 5) / 10 + " % of the guests in town.");

        /// <summary>Page 2: the last <see cref="ServiceHistory.Shown"/> services as one bar each (guests served), newest at the bottom.</summary>
        public static List<string> Page2(IReadOnlyList<ServiceRecord> history)
        {
            var lines = new List<string>();
            ServiceRecord[] shown = ServiceHistory.Newest(history, ServiceHistory.Shown);
            if (shown.Length == 0)
            {
                lines.Add(NoServiceText);
                return lines;
            }
            int max = 0;
            foreach (ServiceRecord r in shown) max = Math.Max(max, r.Served);
            foreach (ServiceRecord r in shown) lines.Add(BarLine("Day " + r.Day, r.Served, max));
            return lines;
        }

        /// <summary>Page 3: the change log's lines, newest at the bottom (or "No changes yet.").</summary>
        public static List<string> Page3(IReadOnlyList<string> log)
        {
            var lines = new List<string>();
            if (log != null) foreach (string line in log) if (!string.IsNullOrEmpty(line)) lines.Add(Fit(line));
            if (lines.Count == 0) lines.Add(NoChangesText);
            return lines;
        }

        /// <summary>The lines of Results page <paramref name="page"/>.</summary>
        public static List<string> PageLines(int page, IReadOnlyList<ServiceRecord> history, IReadOnlyList<string> log) =>
            page == 0 ? Page1(history) : page == 1 ? Page2(history) : Page3(log);

        /// <summary>Whether <paramref name="line"/> reads as a note: at most 40 characters, no "|" or tab, at most one column of numbers.</summary>
        public static bool PassesNoTable(string line) =>
            line != null && line.Length <= MaxChars && line.IndexOf('|') < 0 && line.IndexOf('\t') < 0 && NumberColumns(line) <= 1;

        /// <summary>The cells of <paramref name="line"/> (split at two or more spaces) that start with a number or a price.</summary>
        public static int NumberColumns(string line)
        {
            if (string.IsNullOrEmpty(line)) return 0;
            int columns = 0;
            foreach (string cell in line.Split(new[] { "  " }, StringSplitOptions.RemoveEmptyEntries))
            {
                string c = cell.Trim();
                if (c.Length == 0) continue;
                char first = c[0] == '-' && c.Length > 1 ? c[1] : c[0];
                if (char.IsDigit(first) || first == '$') columns++;
            }
            return columns;
        }

        /// <summary>The lines joined for a screen or the clipboard.</summary>
        public static string Join(IReadOnlyList<string> lines)
        {
            var sb = new StringBuilder();
            if (lines != null)
                for (int i = 0; i < lines.Count; i++)
                {
                    if (i > 0) sb.Append('\n');
                    sb.Append(lines[i]);
                }
            return sb.ToString();
        }
    }
}
