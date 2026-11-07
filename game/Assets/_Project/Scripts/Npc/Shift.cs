namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 022: the kitchen shift, pure. Open from 08:00 to 14:00 in-game (the spec 008/009 clock, one tick per hour);
    /// at 14:00 it closes, everyone still waiting counts as missed and the summary shows for 15 s; then the caller moves
    /// the clock to 08:00 of the next day (<see cref="HoursToNextOpen"/>) and the next shift opens. Time comes in from
    /// the caller (economy tick, seconds), nothing is read from a clock here.
    /// </summary>
    public sealed class Shift
    {
        public const int OpenHour = 8;
        public const int CloseHour = 14;
        public const float SummarySeconds = 15f;

        private float _closedAt;

        /// <summary>Customers come and orders are taken.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>The 15 s after closing, while every player sees the summary.</summary>
        public bool SummaryShowing { get; private set; }

        /// <summary>In-game day of the open (or last) shift; 0 before the first.</summary>
        public long Day { get; private set; }

        /// <summary>Numbers of the shift so far (the open one, or the last one after closing).</summary>
        public ShiftSummary Live { get; private set; }

        /// <summary>Numbers of the last finished shift.</summary>
        public ShiftSummary Last { get; private set; }

        public long LastDay { get; private set; }
        public bool HasLast => LastDay > 0;

        public static long HourOf(long tick) => (tick < 0 ? 0 : tick) % 24;
        public static long DayOf(long tick) => (tick < 0 ? 0 : tick) / 24 + 1;

        public static bool IsShiftHour(long tick)
        {
            long hour = HourOf(tick);
            return hour >= OpenHour && hour < CloseHour;
        }

        /// <summary>Hours from <paramref name="tick"/> to the next 08:00 (1..24).</summary>
        public static int HoursToNextOpen(long tick)
        {
            long hour = HourOf(tick);
            return (int)(hour < OpenHour ? OpenHour - hour : 24 - hour + OpenHour);
        }

        /// <summary>
        /// The clock is at <paramref name="tick"/>. Opens a new shift inside the hours (not during the summary), closes
        /// the open one outside them or on another day; <paramref name="stillWaiting"/> customers then count as missed.
        /// True when <see cref="IsOpen"/> or <see cref="SummaryShowing"/> changed.
        /// </summary>
        public bool OnClock(long tick, float now, int stillWaiting)
        {
            bool inHours = IsShiftHour(tick);
            if (IsOpen)
            {
                if (inHours && DayOf(tick) == Day) return false;
                Live = Live.WithMissed(stillWaiting);
                Last = Live;
                LastDay = Day;
                IsOpen = false;
                SummaryShowing = true;
                _closedAt = now;
                return true;
            }
            if (SummaryShowing || !inHours) return false;
            IsOpen = true;
            Day = DayOf(tick);
            Live = ShiftSummary.Empty;
            return true;
        }

        /// <summary>True once, when the 15 s summary is over (the caller then moves the clock to the next shift).</summary>
        public bool UpdateSummary(float now)
        {
            if (!SummaryShowing || now - _closedAt < SummarySeconds) return false;
            SummaryShowing = false;
            return true;
        }

        /// <summary>A customer was served during the open shift (ignored when closed).</summary>
        public void RecordServed(int stars, long pay)
        {
            if (IsOpen) Live = Live.WithServed(stars, pay);
        }

        /// <summary>Customers left unserved during the open shift (ignored when closed).</summary>
        public void RecordMissed(int count)
        {
            if (IsOpen) Live = Live.WithMissed(count);
        }

        /// <summary>Server stopped: back to "no shift yet".</summary>
        public void Reset()
        {
            IsOpen = false;
            SummaryShowing = false;
            Day = 0;
            LastDay = 0;
            Live = ShiftSummary.Empty;
            Last = ShiftSummary.Empty;
            _closedAt = 0f;
        }
    }
}
