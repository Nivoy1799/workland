using System;
using Worktown.Sim.Clock;
using Worktown.Sim.Core;
using Xunit;

namespace Worktown.Sim.Tests.Clock
{
    public class WeatherPlanTests
    {
        private static readonly WeatherCatalog W = WeatherCatalog.CreateDefault();

        private sealed class Counting : IRandom
        {
            private readonly IRandom _inner = new SeededRandom(3);
            public int Draws;
            public float NextFloat() { Draws++; return _inner.NextFloat(); }
            public int NextInt(int a, int b) { Draws++; return _inner.NextInt(a, b); }
        }

        [Fact]
        public void SameSeedAndDay_Equal()
        {
            for (long d = 1; d < 50; d++) Assert.Equal(WeatherPlan.For(W, 9UL, d), WeatherPlan.For(W, 9UL, d));
        }

        [Fact]
        public void ExactlyThreeDrawsPerDay()
        {
            var r = new Counting();
            WeatherPlan.For(W, r, 5, false);
            Assert.Equal(3, r.Draws);
        }

        [Fact]
        public void DayZeroThrows() => Assert.Throws<ArgumentOutOfRangeException>(() => WeatherPlan.For(W, 1UL, 0));

        [Fact]
        public void Day1NeverStorm_NeverTwoStormsInARow_SharesAndRainHours()
        {
            int storms = 0, panels = 0;
            DayKind prev = DayKind.Normal;
            for (long d = 1; d <= 10000; d++)
            {
                DayWeather w = WeatherPlan.For(W, 1UL, d);
                if (d == 1) Assert.NotEqual(DayKind.Storm, w.Kind);
                if (w.Kind == DayKind.Storm) { storms++; Assert.NotEqual(DayKind.Storm, prev); }
                if (w.Kind == DayKind.OpenPanel) panels++;
                Assert.InRange(w.RainStartMinute, 10 * 60, 16 * 60);
                Assert.Equal(60, w.RainMinutes);
                prev = w.Kind;
            }
            Assert.InRange(storms, 700, 1000);
            Assert.InRange(panels, 2800, 3200);
        }

        [Fact]
        public void PanelsOnlyOnOpenPanelDays_ClosedDuringRainHour()
        {
            bool sawOpen = false;
            for (long d = 1; d <= 200; d++)
            {
                DayWeather w = WeatherPlan.For(W, 4UL, d);
                for (int m = 0; m < 1440; m += 5)
                {
                    TownTime t = Tm(m);
                    if (WeatherPlan.PanelsOpen(w, t))
                    {
                        sawOpen = true;
                        Assert.Equal(DayKind.OpenPanel, w.Kind);
                        Assert.False(WeatherPlan.InRainHour(w, t));
                        Assert.InRange(m, 9 * 60, 18 * 60 - 1);
                    }
                    if (w.Kind == DayKind.Storm) Assert.False(WeatherPlan.PanelsOpen(w, t));
                }
                if (w.Kind != DayKind.OpenPanel) Assert.Equal(-1, w.PanelsFromMinute);
                if (w.Kind != DayKind.Storm) Assert.Equal(-1, w.StormFromMinute);
            }
            Assert.True(sawOpen);
        }

        [Fact]
        public void RainHourAndStormHelpers()
        {
            var w = new DayWeather(2, DayKind.Storm, 12 * 60, 60, -1, -1, 7 * 60, 21 * 60, 1);
            Assert.True(WeatherPlan.InRainHour(w, Tm(12 * 60)));
            Assert.False(WeatherPlan.InRainHour(w, Tm(13 * 60)));
            Assert.True(WeatherPlan.InStorm(w, Tm(7 * 60)));
            Assert.False(WeatherPlan.InStorm(w, Tm(21 * 60)));
            Assert.False(WeatherPlan.PanelsOpen(w, Tm(10 * 60)));
        }

        private static TownTime Tm(int m) => new TownTime(2, 0, DayPhase.Day, 0, m, m / 60);

        [Fact]
        public void WeatherCatalog_Validation()
        {
            W.Validate();
            Assert.Throws<ArgumentException>(() => (W with { StormPermille = 800, OpenPanelPermille = 300 }).Validate());
            Assert.Throws<ArgumentException>(() => (W with { RainToHour = 24 }).Validate());
            Assert.Throws<ArgumentException>(() => (W with { PanelsToMinute = 1500 }).Validate());
            Assert.Throws<ArgumentException>(() => (W with { StormFromMinute = 900, StormToMinute = 800 }).Validate());
        }
    }
}
