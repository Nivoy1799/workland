using System.IO;
using System.Linq;
using Worktown.ClockReport;
using Xunit;

namespace Worktown.Sim.Tests.Clock
{
    public class ClockReportTests
    {
        [Fact]
        public void OneLinePerDayThenServiceFit_Exit0()
        {
            var w = new StringWriter();
            int code = Program.Run(new[] { "--seed", "1", "--days", "28" }, w);
            Assert.Equal(0, code);
            string[] lines = w.ToString().Replace("\r", "").TrimEnd('\n').Split('\n');
            Assert.Equal(28, lines.Count(l => l.StartsWith("day ")));
            Assert.Contains(lines, l => l.StartsWith("day 01  ") && l.Contains("rain ") && l.Contains("strikes ") && l.Contains("Spring 1"));
            Assert.Contains(lines, l => l.Contains("blossom_fair"));
            Assert.Contains("service fit 360+60: 5 per day, 300 s left, 857 per 100 play hours", lines);
            Assert.Contains("service fit 360+15: 6 per day, 150 s left, 960 per 100 play hours", lines);
        }

        [Fact]
        public void BadArguments_Exit1() =>
            Assert.Equal(1, Program.Run(new[] { "--days", "x" }, new StringWriter()));
    }
}
