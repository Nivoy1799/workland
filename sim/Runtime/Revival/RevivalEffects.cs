using System;

namespace Worktown.Sim.Revival
{
    /// <summary>
    /// Spec 065: what a level does, static and pure. The footfall factor is spec 064's <c>revivalPermille</c> input; the
    /// ingredient ceiling sits on top of today's item quality (specs 028, 034) and replaces nothing; how the game applies it is
    /// a later spec.
    /// </summary>
    public static class RevivalEffects
    {
        /// <summary>The footfall factor of a district at <paramref name="level"/> (permille; 1000 at Grey).</summary>
        public static int FootfallPermille(RevivalCatalog c, RevivalLevel level)
        {
            if (c == null) throw new ArgumentException("A catalog is required.", nameof(c));
            if (level < RevivalLevel.Grey || level > RevivalLevel.Thriving) throw new ArgumentOutOfRangeException(nameof(level));
            return c.FootfallPermille[(int)level];
        }

        /// <summary>
        /// The best ingredient quality on sale (permille): the stage's ceiling plus Greenhouse Hill's level bonus, at most 1000.
        /// <paramref name="stage"/> is 1..5 (else <see cref="ArgumentOutOfRangeException"/>).
        /// </summary>
        public static int IngredientCeilingPermille(RevivalCatalog c, int stage, RevivalLevel greenhouseLevel)
        {
            if (c == null) throw new ArgumentException("A catalog is required.", nameof(c));
            if (stage < 1 || stage > 5) throw new ArgumentOutOfRangeException(nameof(stage), "stage must be 1..5.");
            if (greenhouseLevel < RevivalLevel.Grey || greenhouseLevel > RevivalLevel.Thriving) throw new ArgumentOutOfRangeException(nameof(greenhouseLevel));
            return Math.Min(1000, c.StageIngredientCeiling[stage - 1] + c.GreenhouseCeilingBonus[(int)greenhouseLevel]);
        }

        /// <summary>True when the unlock's district has reached its level (throws for an unknown unlock id).</summary>
        public static bool IsUnlocked(RevivalCatalog c, RevivalState s, string unlockId)
        {
            if (c == null) throw new ArgumentException("A catalog is required.", nameof(c));
            if (s == null) throw new ArgumentException("A state is required.", nameof(s));
            RevivalUnlock u = c.Unlock(unlockId);
            return s.LevelOf(u.DistrictId) >= u.MinLevel;
        }
    }
}
