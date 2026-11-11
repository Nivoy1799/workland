using System.Linq;
using Worktown.Sim.Clock;
using Xunit;

namespace Worktown.Sim.Tests.Clock
{
    public class ServiceFitTests
    {
        private static ClockCatalog OfLength(int length)
        {
            ClockCatalog d = ClockCatalog.CreateDefault();
            return d with { Phases = d.Phases.Select(p => p with { Seconds = p.Seconds * length / 2400 }).ToArray(), DayLengthSeconds = length };
        }

        [Fact]
        public void Literals()
        {
            Assert.Equal((5, 300, 857), ServiceFit.For(ClockCatalog.CreateDefault(), 360, 60));
            Assert.Equal((6, 150, 960), ServiceFit.For(ClockCatalog.CreateDefault(), 360, 15));
        }

        [Fact]
        public void PacePerPlayHourIgnoresDayLength()
        {
            foreach (int len in new[] { 1200, 2400, 3600 })
            {
                OfLength(len).Validate();
                Assert.Equal(857, ServiceFit.For(OfLength(len), 360, 60).ServicesPerPlayHourTimes100);
                Assert.Equal(960, ServiceFit.For(OfLength(len), 360, 15).ServicesPerPlayHourTimes100);
            }
        }
    }
}
