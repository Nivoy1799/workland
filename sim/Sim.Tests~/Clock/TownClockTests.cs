using System;
using System.Linq;
using Worktown.Sim.Clock;
using Worktown.Sim.Core;
using Xunit;

namespace Worktown.Sim.Tests.Clock
{
    public class TownClockTests
    {
        private static readonly ClockCatalog C = ClockCatalog.CreateDefault();

        [Theory]
        [InlineData(0, 1, 8 * 60, DayPhase.Day)]
        [InlineData(420, 1, 11 * 60 + 30, DayPhase.Day)]
        [InlineData(1320, 1, 19 * 60, DayPhase.Dusk)]
        [InlineData(1500, 1, 21 * 60, DayPhase.Night)]
        [InlineData(2100, 2, 5 * 60, DayPhase.Dawn)]
        public void AtLiterals_AndPlaySecondsAtInverts(long play, long day, int minute, DayPhase phase)
        {
            TownTime t = TownClock.At(C, play);
            Assert.Equal(day, t.Day);
            Assert.Equal(minute, t.MinuteOfDay);
            Assert.Equal(minute / 60, t.Hour);
            Assert.Equal(phase, t.Phase);
            Assert.Equal(play, TownClock.PlaySecondsAt(C, day, minute));
        }

        [Fact]
        public void At_NegativeThrows() =>
            Assert.Throws<System.ArgumentOutOfRangeException>(() => TownClock.At(C, -1));

        [Fact]
        public void At_PhasePermilleIsProgress()
        {
            Assert.Equal(0, TownClock.At(C, 1500).PhasePermille);
            Assert.Equal(500, TownClock.At(C, 1500 + 300).PhasePermille);
        }

        [Fact]
        public void SecondsOfHour_ByPhase()
        {
            Assert.Equal(90, TownClock.SecondsOfHour(C, 5));
            Assert.Equal(120, TownClock.SecondsOfHour(C, 12));
            Assert.Equal(90, TownClock.SecondsOfHour(C, 20));
            Assert.Equal(75, TownClock.SecondsOfHour(C, 23));
            Assert.Equal(75, TownClock.SecondsOfHour(C, 2));
        }

        [Fact]
        public void Spans_FromEightGivesThreeHoursOf120()
        {
            var s = TownClock.Spans(C, 0, 360);
            Assert.Equal(new[] { 8, 9, 10 }, s.Select(x => x.Hour).ToArray());
            Assert.All(s, x => Assert.Equal(120, x.Seconds));
        }

        [Fact]
        public void Spans_FromHalfPastSixAcrossDusk()
        {
            long from = TownClock.PlaySecondsAt(C, 1, 18 * 60 + 30);
            var s = TownClock.Spans(C, from, from + 360);
            Assert.Equal(new[] { 18, 19, 20, 21, 22 }, s.Select(x => x.Hour).ToArray());
            Assert.Equal(new[] { 60, 90, 90, 75, 45 }, s.Select(x => x.Seconds).ToArray());
        }

        [Fact]
        public void Spans_EmptyIntervalAndBackwardsThrows()
        {
            Assert.Empty(TownClock.Spans(C, 10, 10));
            Assert.Throws<System.ArgumentException>(() => TownClock.Spans(C, 10, 9));
        }

        [Fact]
        public void Spans_SumToTheInterval_Over1000SeededIntervals()
        {
            var rng = new SeededRandom(7);
            for (int i = 0; i < 1000; i++)
            {
                long from = rng.NextInt(0, 20000);
                long to = from + rng.NextInt(0, 3000);
                var s = TownClock.Spans(C, from, to);
                Assert.Equal(to - from, s.Sum(x => (long)x.Seconds));
                Assert.All(s, x => Assert.True(x.Seconds > 0));
            }
        }

        [Fact]
        public void DaySecondOf_Literals()
        {
            Assert.Equal(0, TownClock.DaySecondOf(C, 5 * 60));
            Assert.Equal(180, TownClock.DaySecondOf(C, 7 * 60));
            Assert.Equal(1800, TownClock.DaySecondOf(C, 21 * 60));
            Assert.Equal(2400 - 75, TownClock.DaySecondOf(C, 4 * 60));
        }

        [Fact]
        public void PlaySecondsAt_InvertsAtEveryMinute()
        {
            for (int m = 0; m < 1440; m += 7)
            {
                long play = TownClock.PlaySecondsAt(C, 3, m) ;
                Assert.Equal(m, TownClock.At(C, play).MinuteOfDay);
            }
        }

        [Fact]
        public void SaveAndReload_RecomputesTheSame()
        {
            for (long play = 0; play < 10000; play += 137)
            {
                long stored = play;
                Assert.Equal(TownClock.At(C, play), TownClock.At(ClockCatalog.CreateDefault(), stored));
                Assert.Equal(
                    WeatherPlan.For(WeatherCatalog.CreateDefault(), 5, TownClock.At(C, play).Day),
                    WeatherPlan.For(WeatherCatalog.CreateDefault(), 5, TownClock.At(C, stored).Day));
            }
        }

        [Fact]
        public void NoPublicMethodTakesAWallClockType()
        {
            var types = typeof(TownClock).Assembly.GetTypes()
                .Where(t => t.Namespace == "Worktown.Sim.Clock" && t.IsPublic);
            foreach (var type in types)
                foreach (var m in type.GetMethods())
                    foreach (var p in m.GetParameters())
                        Assert.DoesNotContain(p.ParameterType.Name, new[] { "DateTime", "DateTimeOffset", "TimeSpan", "Stopwatch" });
        }

        [Fact]
        public void Spans_AcceptsExactlyMaxSpanSeconds()
        {
            var s = TownClock.Spans(C, 0, TownClock.MaxSpanSeconds);
            Assert.Equal(TownClock.MaxSpanSeconds, s.Sum(x => (long)x.Seconds));
        }

        [Fact]
        public void Spans_RefusesLongerThanMaxSpanSeconds() =>
            Assert.Throws<ArgumentException>(() => TownClock.Spans(C, 0, TownClock.MaxSpanSeconds + 1));
    }
}
