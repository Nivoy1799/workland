using System;
using System.Collections.Generic;

namespace Worktown.Sim.Clock
{
    /// <summary>The day table (D46: dawn 3, day 24, dusk 3, night 10 minutes) and the new game start.</summary>
    public sealed record ClockCatalog(IReadOnlyList<PhaseDef> Phases, int DayLengthSeconds, int NewGameStartSecond)
    {
        public const int MinutesPerDay = 1440;

        public static ClockCatalog CreateDefault()
        {
            return new ClockCatalog(
                new[]
                {
                    new PhaseDef(DayPhase.Dawn, 180, 5 * 60, 120),
                    new PhaseDef(DayPhase.Day, 1440, 7 * 60, 720),
                    new PhaseDef(DayPhase.Dusk, 180, 19 * 60, 120),
                    new PhaseDef(DayPhase.Night, 600, 21 * 60, 480),
                },
                2400,
                300);
        }

        /// <summary>Throws <see cref="ArgumentException"/> when phases are not contiguous or the sums are wrong.</summary>
        public void Validate()
        {
            if (Phases.Count == 0) throw new ArgumentException("No phases.");
            int seconds = 0, minutes = 0;
            for (int i = 0; i < Phases.Count; i++)
            {
                PhaseDef p = Phases[i];
                if (p.Seconds <= 0 || p.Minutes <= 0) throw new ArgumentException("Phase " + p.Phase + " has no length.");
                PhaseDef next = Phases[(i + 1) % Phases.Count];
                if ((p.StartMinute + p.Minutes) % MinutesPerDay != next.StartMinute)
                    throw new ArgumentException("Phase " + p.Phase + " is not followed directly by " + next.Phase + ".");
                seconds += p.Seconds;
                minutes += p.Minutes;
            }
            if (seconds != DayLengthSeconds) throw new ArgumentException("Phase seconds sum to " + seconds + ", not " + DayLengthSeconds + ".");
            if (minutes != MinutesPerDay) throw new ArgumentException("Phase minutes sum to " + minutes + ", not 1440.");
            if (NewGameStartSecond < 0 || NewGameStartSecond >= DayLengthSeconds) throw new ArgumentException("Bad new game start.");
        }

        /// <summary>First real second of a phase inside the day.</summary>
        public int PhaseStartSecond(int phaseIndex)
        {
            int s = 0;
            for (int i = 0; i < phaseIndex; i++) s += Phases[i].Seconds;
            return s;
        }

        /// <summary>Index of the phase containing a second of the day.</summary>
        public int PhaseIndexAtSecond(int daySecond)
        {
            int s = 0;
            for (int i = 0; i < Phases.Count; i++)
            {
                s += Phases[i].Seconds;
                if (daySecond < s) return i;
            }
            throw new ArgumentOutOfRangeException(nameof(daySecond));
        }

        /// <summary>Index of the phase containing a clock minute of the day.</summary>
        public int PhaseIndexAtMinute(int minuteOfDay)
        {
            for (int i = 0; i < Phases.Count; i++)
            {
                int offset = ((minuteOfDay - Phases[i].StartMinute) % MinutesPerDay + MinutesPerDay) % MinutesPerDay;
                if (offset < Phases[i].Minutes) return i;
            }
            throw new ArgumentOutOfRangeException(nameof(minuteOfDay));
        }
    }
}
