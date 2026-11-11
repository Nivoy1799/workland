using System;
using System.Collections.Generic;
using System.Linq;

namespace Worktown.Sim.Revival
{
    /// <summary>
    /// Spec 065 (D43): the revival rules, static and pure. Meals add milli-points to the district where they are eaten (a
    /// share spills to its neighbours), progress is ppm of the current step's goal (D40: the goal grows with the crew, so a
    /// crew change only changes the size of the next steps), and a level is never lost.
    /// A level-up carries the rest rescaled to the next step by the ratio of the level goals (the district and crew factors
    /// cancel); Thriving is the last level and its progress is always 1,000,000 ppm.
    /// </summary>
    public static class RevivalEngine
    {
        /// <summary>Every district at Grey with its start progress.</summary>
        public static RevivalState Start(RevivalCatalog c)
        {
            if (c == null) throw new ArgumentException("A catalog is required.", nameof(c));
            return new RevivalState(c.Districts.Select(d => new DistrictRevival(d.DistrictId, RevivalLevel.Grey, d.StartProgressPpm)));
        }

        /// <summary>
        /// Adds <paramref name="count"/> meals of <paramref name="stars"/> (1..5) eaten in <paramref name="districtId"/> by a crew
        /// of <paramref name="players"/> (clamped to 1..16). <c>milli = pointsByStars[stars - 1] * 1000 * count</c>; the home
        /// district gets <c>milli - spill</c> plus the rounding rest, each neighbour <c>spill / n</c> (no further spill).
        /// Returns the new state and the level-ups in order (home first, then neighbours in their listed order).
        /// </summary>
        public static (RevivalState State, IReadOnlyList<LevelUp> LevelUps) AddMeals(RevivalState s, RevivalCatalog c, string districtId,
            int stars, int count, int players)
        {
            Check(s, c);
            if (stars < 1 || stars > 5) throw new ArgumentOutOfRangeException(nameof(stars), "stars must be 1..5.");
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be >= 0.");
            DistrictRevivalDef home = c.District(districtId);
            long milli = c.PointsByStars[stars - 1] * 1000L * count;
            var ups = new List<LevelUp>();
            if (milli == 0) return (s, ups.AsReadOnly());

            int n = home.Neighbours.Count;
            long spill = n == 0 ? 0 : milli * c.SpillPermille / 1000;
            long each = n == 0 ? 0 : spill / n;
            var districts = s.Districts.ToArray();
            Apply(districts, c, home.DistrictId, milli - each * n, players, ups);
            if (each > 0)
                foreach (string nb in home.Neighbours) Apply(districts, c, nb, each, players, ups);
            return (new RevivalState(districts), ups.AsReadOnly());
        }

        /// <summary>
        /// Entering climb stage 2..5 gives every district <see cref="RevivalCatalog.StageBonusPpm"/> of its current step (at
        /// most one level each call); stage 1 or any other value does nothing.
        /// </summary>
        public static (RevivalState State, IReadOnlyList<LevelUp> LevelUps) EnterStage(RevivalState s, RevivalCatalog c, int stage)
        {
            Check(s, c);
            var ups = new List<LevelUp>();
            if (stage < 2 || stage > 5 || c.StageBonusPpm == 0) return (s, ups.AsReadOnly());
            var districts = s.Districts.ToArray();
            for (int i = 0; i < districts.Length; i++)
            {
                DistrictRevival d = districts[i];
                if (d.Level == RevivalLevel.Thriving) continue;
                long ppm = (long)d.ProgressPpm + c.StageBonusPpm;
                RevivalLevel level = d.Level;
                if (ppm >= RevivalCatalog.Ppm)
                {
                    (level, ppm) = Up(c, level, ppm);
                    if (level != RevivalLevel.Thriving) ppm = Math.Min(ppm, RevivalCatalog.Ppm - 1); // one level per call
                    ups.Add(new LevelUp(d.DistrictId, level));
                }
                districts[i] = d with { Level = level, ProgressPpm = (int)ppm };
            }
            return (new RevivalState(districts), ups.AsReadOnly());
        }

        private static void Apply(DistrictRevival[] districts, RevivalCatalog c, string id, long milli, int players, List<LevelUp> ups)
        {
            if (milli <= 0) return;
            int i = c.IndexOf(id);
            DistrictRevival d = districts[i];
            if (d.Level == RevivalLevel.Thriving) return;
            long ppm = d.ProgressPpm + milli * RevivalCatalog.Ppm / c.GoalMilli(id, d.Level, players);
            RevivalLevel level = d.Level;
            while (ppm >= RevivalCatalog.Ppm && level < RevivalLevel.Thriving)
            {
                (level, ppm) = Up(c, level, ppm);
                ups.Add(new LevelUp(id, level));
            }
            districts[i] = d with { Level = level, ProgressPpm = (int)ppm };
        }

        /// <summary>One level up: the rest above a full step carries over, rescaled to the next step's goal; Thriving is full.</summary>
        private static (RevivalLevel Level, long Ppm) Up(RevivalCatalog c, RevivalLevel level, long ppm)
        {
            RevivalLevel next = level + 1;
            if (next == RevivalLevel.Thriving) return (next, RevivalCatalog.Ppm);
            long rest = (ppm - RevivalCatalog.Ppm) * c.LevelGoals[(int)level] / c.LevelGoals[(int)next];
            return (next, rest);
        }

        private static void Check(RevivalState s, RevivalCatalog c)
        {
            if (s == null) throw new ArgumentException("A state is required.", nameof(s));
            if (c == null) throw new ArgumentException("A catalog is required.", nameof(c));
            if (s.Districts.Count != c.Districts.Count) throw new ArgumentException("The state does not match the catalog.");
            for (int i = 0; i < c.Districts.Count; i++)
                if (s.Districts[i].DistrictId != c.Districts[i].DistrictId) throw new ArgumentException("The state does not match the catalog.");
        }
    }
}
