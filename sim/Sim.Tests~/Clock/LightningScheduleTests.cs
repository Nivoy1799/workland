using System.Linq;
using Worktown.Sim.Clock;
using Xunit;

namespace Worktown.Sim.Tests.Clock
{
    public class LightningScheduleTests
    {
        private static readonly ClockCatalog C = ClockCatalog.CreateDefault();
        private static readonly WeatherCatalog W = WeatherCatalog.CreateDefault();

        [Fact]
        public void StormDays_StrikesInsideWindow_GapsAtLeastFour_Deterministic()
        {
            int stormDays = 0;
            for (long d = 1; d <= 300; d++)
            {
                DayWeather w = WeatherPlan.For(W, 2UL, d);
                var strikes = LightningSchedule.Strikes(w, C);
                Assert.Equal(strikes, LightningSchedule.Strikes(w, C));
                if (w.Kind != DayKind.Storm) { Assert.Empty(strikes); continue; }
                stormDays++;
                int from = TownClock.DaySecondOf(C, w.StormFromMinute), to = TownClock.DaySecondOf(C, w.StormToMinute);
                Assert.NotEmpty(strikes);
                Assert.All(strikes, s => Assert.InRange(s, from, to - 1));
                for (int i = 1; i < strikes.Count; i++) Assert.True(strikes[i] - strikes[i - 1] >= 4);
            }
            Assert.True(stormDays > 0);
        }

        [Fact]
        public void NormalAndOpenPanelDays_None()
        {
            var normal = new DayWeather(1, DayKind.Normal, 600, 60, -1, -1, -1, -1, 5);
            var panel = new DayWeather(1, DayKind.OpenPanel, 600, 60, 540, 1080, -1, -1, 5);
            Assert.Empty(LightningSchedule.Strikes(normal, C));
            Assert.Empty(LightningSchedule.Strikes(panel, C));
        }

        [Fact]
        public void GapsStayWithinTheDrawRange()
        {
            int stormDays = 0;
            for (long d = 1; d <= 300; d++)
            {
                DayWeather w = WeatherPlan.For(W, 2UL, d);
                if (w.Kind != DayKind.Storm) continue;
                stormDays++;
                var strikes = LightningSchedule.Strikes(w, C);
                Assert.NotEmpty(strikes);
                for (int i = 1; i < strikes.Count; i++)
                {
                    int gap = strikes[i] - strikes[i - 1];
                    Assert.True(gap >= LightningSchedule.GapFromSeconds);
                    Assert.True(gap < LightningSchedule.GapToSecondsExclusive);
                }
            }
            Assert.True(stormDays > 0);
        }
    }
}
