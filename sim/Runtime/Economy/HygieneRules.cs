using System;
using System.Collections.Generic;

namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 035, pure and deterministic: the shop's hygiene score (0-100 %). Time comes in from the caller as whole game minutes (the
    /// injected clock: the economy's game minute); there is no randomness. Per game minute:
    /// - each dirty dish left out longer than <see cref="DishGraceMinutes"/> costs <see cref="DishPerMinute"/> %;
    /// - a full trash costs <see cref="TrashFullPerMinute"/> %, each open spill <see cref="SpillPerMinute"/> %, something burning on the
    ///   heat (the smell) <see cref="BurningPerMinute"/> %;
    /// - with nothing dirty at all the score recovers <see cref="RecoveryPerMinute"/> % up to 100.
    /// Guests notice it (032's rules, small): below <see cref="ReviewBelow"/> % a review loses <see cref="ReviewStars"/> star, below
    /// <see cref="PatienceBelow"/> % a new guest has <see cref="PatiencePermilleWhenDirty"/> permille of its patience.
    /// Spec 035 defaults to review (Design notes); the burning rate is the builder's default (the spec names the source, not its rate).
    /// </summary>
    public static class HygieneRules
    {
        public const int MaxPercent = 100;
        public const int StartPercent = 100;
        /// <summary>A dirty dish may wait this long (game minutes) before it counts.</summary>
        public const int DishGraceMinutes = 2;
        public const int DishPerMinute = 1;
        public const int TrashFullPerMinute = 2;
        public const int SpillPerMinute = 2;
        public const int BurningPerMinute = 1;
        public const int RecoveryPerMinute = 1;
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

        /// <summary>What one game minute costs with <paramref name="s"/> (0 when nothing counts yet).</summary>
        public static int DecayPerMinute(HygieneSources s)
        {
            long d = (long)s.OverdueDishes * DishPerMinute + (s.TrashFull ? TrashFullPerMinute : 0) + (long)s.Spills * SpillPerMinute
                + (s.Burning ? BurningPerMinute : 0);
            return (int)Math.Min(MaxPercent, d);
        }

        /// <summary>The score after one game minute with <paramref name="s"/>: down by the decay, or up by the recovery when all is clean.</summary>
        public static int Step(int percent, HygieneSources s)
        {
            percent = Clamp(percent);
            if (s.IsClean) return Math.Min(MaxPercent, percent + RecoveryPerMinute);
            return Math.Max(0, percent - DecayPerMinute(s));
        }

        /// <summary>The score after <paramref name="minutes"/> game minutes with the same sources (at most <see cref="MaxCatchUpMinutes"/>).</summary>
        public static int Advance(int percent, HygieneSources s, long minutes)
        {
            percent = Clamp(percent);
            long n = Math.Max(0L, Math.Min(MaxCatchUpMinutes, minutes));
            for (long i = 0; i < n; i++) percent = Step(percent, s);
            return percent;
        }

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
    }
}
