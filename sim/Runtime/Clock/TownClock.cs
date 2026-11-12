using System;
using System.Collections.Generic;

namespace Worktown.Sim.Clock
{
    /// <summary>Pure maps between play seconds (the seconds the host game has run, D37) and town time.</summary>
    public static class TownClock
    {
        /// <summary>Town time at a play second. The catalog must be valid (call <see cref="ClockCatalog.Validate"/> once); an invalid one can divide by zero.</summary>
        public static TownTime At(ClockCatalog c, long playSeconds)
        {
            if (playSeconds < 0) throw new ArgumentOutOfRangeException(nameof(playSeconds));
            long total = playSeconds + c.NewGameStartSecond;
            long day = total / c.DayLengthSeconds + 1;
            int daySecond = (int)(total % c.DayLengthSeconds);
            int idx = c.PhaseIndexAtSecond(daySecond);
            PhaseDef p = c.Phases[idx];
            int into = daySecond - c.PhaseStartSecond(idx);
            int minute = (p.StartMinute + into * p.Minutes / p.Seconds) % ClockCatalog.MinutesPerDay;
            return new TownTime(day, daySecond, p.Phase, into * 1000 / p.Seconds, minute, minute / 60);
        }

        /// <summary>Real seconds one clock hour lasts (90, 120 or 75 by the default table).</summary>
        public static int SecondsOfHour(ClockCatalog c, int hour)
        {
            PhaseDef p = c.Phases[c.PhaseIndexAtMinute(hour * 60)];
            return p.Seconds * 60 / p.Minutes;
        }

        /// <summary>The longest interval Spans accepts, in play seconds (7 default game days). Spans walks second by second.</summary>
        public const long MaxSpanSeconds = 7L * 2400;

        /// <summary>The clock hours an interval covers, with the real seconds of each. Seconds sum to the interval.</summary>
        public static IReadOnlyList<HourSpan> Spans(ClockCatalog c, long fromPlay, long toPlay)
        {
            if (toPlay < fromPlay) throw new ArgumentException("toPlay is before fromPlay.");
            if (toPlay - fromPlay > MaxSpanSeconds) throw new ArgumentException("The interval is longer than MaxSpanSeconds.");
            var result = new List<HourSpan>();
            long day = 0;
            int hour = -1, seconds = 0;
            for (long t = fromPlay; t < toPlay; t++)
            {
                TownTime tt = At(c, t);
                if (tt.Day != day || tt.Hour != hour)
                {
                    if (seconds > 0) result.Add(new HourSpan(day, hour, seconds));
                    day = tt.Day;
                    hour = tt.Hour;
                    seconds = 0;
                }
                seconds++;
            }
            if (seconds > 0) result.Add(new HourSpan(day, hour, seconds));
            return result;
        }

        /// <summary>The second of the day (0 = dawn) at which a clock minute begins.</summary>
        public static int DaySecondOf(ClockCatalog c, int minuteOfDay)
        {
            int idx = c.PhaseIndexAtMinute(minuteOfDay);
            PhaseDef p = c.Phases[idx];
            int offset = ((minuteOfDay - p.StartMinute) % ClockCatalog.MinutesPerDay + ClockCatalog.MinutesPerDay) % ClockCatalog.MinutesPerDay;
            int into = (offset * p.Seconds + p.Minutes - 1) / p.Minutes;
            return c.PhaseStartSecond(idx) + into;
        }

        /// <summary>The inverse of <see cref="At"/>: the play second at which a clock minute of a day begins.</summary>
        public static long PlaySecondsAt(ClockCatalog c, long day, int minuteOfDay)
        {
            return (day - 1) * c.DayLengthSeconds + DaySecondOf(c, minuteOfDay) - c.NewGameStartSecond;
        }
    }
}
