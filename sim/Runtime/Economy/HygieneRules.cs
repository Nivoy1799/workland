using System;
using System.Collections.Generic;

namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 035, pure and deterministic: the shop's hygiene score (0-100 %). Time comes in from the caller as whole game minutes (the
    /// injected clock: the economy's game minute); there is no randomness. The score is kept in tenths of a percent
    /// (<see cref="MaxTenths"/> = 100 %), because the rates are a tenth of the spec's (the 036 tuning: a game minute is one real second).
    /// Per game minute:
    /// - each dirty dish left out longer than <see cref="DishGraceMinutes"/> costs <see cref="DishTenthsPerMinute"/> tenths;
    /// - a full trash costs <see cref="TrashFullTenthsPerMinute"/>, each open spill <see cref="SpillTenthsPerMinute"/>, something burning
    ///   on the heat (the smell) <see cref="BurningTenthsPerMinute"/> tenths;
    /// - with nothing dirty at all the score recovers <see cref="RecoveryTenthsPerMinute"/> tenths up to 100 %.
    /// Guests notice it (032's rules, small): below <see cref="ReviewBelow"/> % a review loses <see cref="ReviewStars"/> star, below
    /// <see cref="PatienceBelow"/> % a new guest has <see cref="PatiencePermilleWhenDirty"/> permille of its patience.
    /// Spec 035 defaults to review (Design notes), all ten times slower since spec 036 (orchestrator decision: one spill took the score
    /// from 100 to 49 % in 40 s); the burning rate is the builder's default (the spec names the source, not its rate).
    /// </summary>
    public static class HygieneRules
    {
        public const int MaxPercent = 100;
        public const int StartPercent = 100;
        /// <summary>The score's unit is a tenth of a percent: 100 % is 1000.</summary>
        public const int TenthsPerPercent = 10;
        public const int MaxTenths = MaxPercent * TenthsPerPercent;
        /// <summary>A dirty dish may wait this long (game minutes) before it counts.</summary>
        public const int DishGraceMinutes = 2;
        /// <summary>0.1 % a game minute per overdue dish (the spec's 1 %, ten times slower).</summary>
        public const int DishTenthsPerMinute = 1;
        /// <summary>0.2 % a game minute (the spec's 2 %, ten times slower).</summary>
        public const int TrashFullTenthsPerMinute = 2;
        /// <summary>0.2 % a game minute per open spill (the spec's 2 %, ten times slower).</summary>
        public const int SpillTenthsPerMinute = 2;
        /// <summary>0.1 % a game minute while something burns (the builder's 1 %, ten times slower).</summary>
        public const int BurningTenthsPerMinute = 1;
        /// <summary>+0.1 % a game minute with nothing dirty (the spec's 1 %, ten times slower).</summary>
        public const int RecoveryTenthsPerMinute = 1;
        /// <summary>Below this score a review loses <see cref="ReviewStars"/> star.</summary>
        public const int ReviewBelow = 50;
        public const int ReviewStars = 1;
        /// <summary>Below this score a guest's patience is <see cref="PatiencePermilleWhenDirty"/> permille (−20 %).</summary>
        public const int PatienceBelow = 30;
        public const int PatiencePermilleWhenDirty = 800;
        /// <summary>The trash can holds this many items before it is full (spec 035).</summary>
        public const int TrashCapacity = 12;
        /// <summary>
        /// A jump of the clock (the night skipped after a shift) counts at most this many minutes, so the score never collapses over a
        /// closed night.
        /// </summary>
        public const int MaxCatchUpMinutes = 10;

        /// <summary>How many of the dishes dirty since the given game minutes were left out longer than the grace at <paramref name="now"/>.</summary>
        public static int OverdueDishes(IEnumerable<long> dirtySince, long now)
        {
            if (dirtySince == null) return 0;
            int n = 0;
            foreach (long since in dirtySince)
                if (now - since > DishGraceMinutes) n++;
            return n;
        }

        /// <summary>What one game minute costs with <paramref name="s"/>, in tenths of a percent (0 when nothing counts yet).</summary>
        public static int DecayPerMinute(HygieneSources s)
        {
            long d = (long)s.OverdueDishes * DishTenthsPerMinute + (s.TrashFull ? TrashFullTenthsPerMinute : 0)
                + (long)s.Spills * SpillTenthsPerMinute + (s.Burning ? BurningTenthsPerMinute : 0);
            return (int)Math.Min(MaxTenths, d);
        }

        /// <summary>
        /// The score (tenths) after one game minute with <paramref name="s"/>: down by the decay, or up by the recovery when all is clean.
        /// </summary>
        public static int Step(int tenths, HygieneSources s)
        {
            tenths = ClampTenths(tenths);
            if (s.IsClean) return Math.Min(MaxTenths, tenths + RecoveryTenthsPerMinute);
            return Math.Max(0, tenths - DecayPerMinute(s));
        }

        /// <summary>
        /// The score (tenths) after <paramref name="minutes"/> game minutes with the same sources (at most <see cref="MaxCatchUpMinutes"/>).
        /// </summary>
        public static int Advance(int tenths, HygieneSources s, long minutes)
        {
            tenths = ClampTenths(tenths);
            long n = Math.Max(0L, Math.Min(MaxCatchUpMinutes, minutes));
            for (long i = 0; i < n; i++) tenths = Step(tenths, s);
            return tenths;
        }

        /// <summary>The whole percent a score of <paramref name="tenths"/> shows (rounded down: 49.9 % is 49 %).</summary>
        public static int PercentOf(int tenths) => ClampTenths(tenths) / TenthsPerPercent;

        /// <summary>Stars a review loses at score <paramref name="percent"/> (1 below 50 %, else 0).</summary>
        public static int ReviewPenalty(int percent) => Clamp(percent) < ReviewBelow ? ReviewStars : 0;

        /// <summary>The share of its patience (permille) a guest arriving at score <paramref name="percent"/> gets (800 below 30 %, else 1000).</summary>
        public static int PatiencePermille(int percent) => Clamp(percent) < PatienceBelow ? PatiencePermilleWhenDirty : 1000;

        /// <summary>A patience of <paramref name="seconds"/> at score <paramref name="percent"/> (integer permille, deterministic).</summary>
        public static float Patience(float seconds, int percent) =>
            float.IsNaN(seconds) || seconds <= 0f ? 0f : seconds * PatiencePermille(percent) / 1000f;

        /// <summary>The trash at <paramref name="fill"/> items is full (nothing more goes in until the bag is taken out).</summary>
        public static bool IsTrashFull(int fill) => fill >= TrashCapacity;

        public static int Clamp(int percent) => percent < 0 ? 0 : percent > MaxPercent ? MaxPercent : percent;

        public static int ClampTenths(int tenths) => tenths < 0 ? 0 : tenths > MaxTenths ? MaxTenths : tenths;
    }
}
