using System;
using System.Collections.Generic;
using Worktown.Sim.Clock;
using Xunit;

namespace Worktown.Sim.Tests.Clock
{
    public class ClockCatalogTests
    {
        [Fact]
        public void Default_IsValid_AndMatchesD46()
        {
            ClockCatalog c = ClockCatalog.CreateDefault();
            c.Validate();
            Assert.Equal(2400, c.DayLengthSeconds);
            Assert.Equal(300, c.NewGameStartSecond);
            Assert.Equal(new[] { 180, 1440, 180, 600 }, new[] { c.Phases[0].Seconds, c.Phases[1].Seconds, c.Phases[2].Seconds, c.Phases[3].Seconds });
        }

        private static ClockCatalog With(Func<IReadOnlyList<PhaseDef>, IReadOnlyList<PhaseDef>> edit, int length = 2400)
        {
            ClockCatalog d = ClockCatalog.CreateDefault();
            return d with { Phases = edit(d.Phases), DayLengthSeconds = length };
        }

        [Fact]
        public void Validate_GapBetweenPhasesThrows() =>
            Assert.Throws<ArgumentException>(() => With(p => new[] { p[0], p[1] with { StartMinute = 8 * 60 }, p[2], p[3] }).Validate());

        [Fact]
        public void Validate_SecondsNot2400Throws() =>
            Assert.Throws<ArgumentException>(() => With(p => p, 2300).Validate());

        [Fact]
        public void Validate_MinutesNot1440Throws() =>
            Assert.Throws<ArgumentException>(() => With(p => new[] { p[0], p[1] with { Minutes = 700 }, p[2], p[3] }).Validate());

        [Fact]
        public void Validate_BadStartThrows() =>
            Assert.Throws<ArgumentException>(() => (ClockCatalog.CreateDefault() with { NewGameStartSecond = 2400 }).Validate());

        [Fact]
        public void PhaseStartSecond_AndIndexLookups()
        {
            ClockCatalog c = ClockCatalog.CreateDefault();
            Assert.Equal(180, c.PhaseStartSecond(1));
            Assert.Equal(1800, c.PhaseStartSecond(3));
            Assert.Equal(1, c.PhaseIndexAtSecond(180));
            Assert.Equal(0, c.PhaseIndexAtSecond(179));
            Assert.Equal(3, c.PhaseIndexAtSecond(2399));
            Assert.Throws<ArgumentOutOfRangeException>(() => c.PhaseIndexAtSecond(2400));
            Assert.Equal(3, c.PhaseIndexAtMinute(2 * 60));
            Assert.Equal(0, c.PhaseIndexAtMinute(5 * 60));
            Assert.Equal(2, c.PhaseIndexAtMinute(20 * 60));
        }
    }
}
