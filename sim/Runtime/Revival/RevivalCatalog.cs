using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Worktown.Sim.Town;

namespace Worktown.Sim.Revival
{
    /// <summary>
    /// Spec 065: the revival numbers. Level goals (points to Patched, Lively, Thriving for a 4-player crew and a district goal of
    /// 1000), points per star (1..5), the spill to neighbours, the crew rows (D40), one row per town district, the stage bonus,
    /// the footfall per level and the ingredient ceilings, and the unlocks. The constructor copies every list and validates
    /// (throws <see cref="ArgumentException"/>); it takes the town so that "one row per town district" can be checked.
    /// <see cref="CreateDefault"/> numbers are provisional; tuning changes only those (and <see cref="RevivalPlan.Default"/>).
    /// </summary>
    public sealed class RevivalCatalog
    {
        public const int Levels = 4;
        public const int Ppm = 1_000_000;
        public const int MaxSpillPermille = 500;
        public const int MaxGoalPermille = 5000;
        public const int MinCrewCoverage = 16;
        public const int MaxFootfallPermille = 5000;

        private static readonly Regex IdPattern = new Regex("^[a-z0-9_]{1,32}$", RegexOptions.CultureInvariant);

        private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, RevivalUnlock> _unlocks = new Dictionary<string, RevivalUnlock>(StringComparer.Ordinal);

        public RevivalCatalog(TownCatalog town, IEnumerable<int> levelGoals, IEnumerable<int> pointsByStars, int spillPermille,
            IEnumerable<RevivalCrewRow> crew, IEnumerable<DistrictRevivalDef> districts, int stageBonusPpm,
            IEnumerable<int> footfallPermille, IEnumerable<int> stageIngredientCeiling, IEnumerable<int> greenhouseCeilingBonus,
            IEnumerable<RevivalUnlock> unlocks)
        {
            if (town == null) throw new ArgumentException("A town is required.", nameof(town));
            LevelGoals = Ints(levelGoals, nameof(levelGoals));
            PointsByStars = Ints(pointsByStars, nameof(pointsByStars));
            SpillPermille = spillPermille;
            Crew = Copy(crew, nameof(crew));
            Districts = Copy(districts, nameof(districts))
                .Select(d => d with { Neighbours = (d.Neighbours ?? throw new ArgumentException(d.DistrictId + ": neighbours are required.")).ToList().AsReadOnly() })
                .ToList().AsReadOnly();
            StageBonusPpm = stageBonusPpm;
            FootfallPermille = Ints(footfallPermille, nameof(footfallPermille));
            StageIngredientCeiling = Ints(stageIngredientCeiling, nameof(stageIngredientCeiling));
            GreenhouseCeilingBonus = Ints(greenhouseCeilingBonus, nameof(greenhouseCeilingBonus));
            Unlocks = Copy(unlocks, nameof(unlocks));
            Validate(town);
        }

        /// <summary>Points to go from level L to L + 1 (3 values, rising), for a 4-player crew and a district goal of 1000.</summary>
        public IReadOnlyList<int> LevelGoals { get; }
        /// <summary>Points of one meal by its stars (5 values, stars 1..5, non-decreasing).</summary>
        public IReadOnlyList<int> PointsByStars { get; }
        /// <summary>The share of a meal's points split equally among the district's neighbours (permille, 0..500).</summary>
        public int SpillPermille { get; }
        /// <summary>The crew rows, sorted by MaxPlayers; the last covers 16 players.</summary>
        public IReadOnlyList<RevivalCrewRow> Crew { get; }
        /// <summary>One row per town district, in town order.</summary>
        public IReadOnlyList<DistrictRevivalDef> Districts { get; }
        /// <summary>Progress every district gets on entering climb stages 2-5 (ppm of its current step).</summary>
        public int StageBonusPpm { get; }
        /// <summary>Footfall per level (permille, 4 values, non-decreasing from 1000): spec 064's revival input.</summary>
        public IReadOnlyList<int> FootfallPermille { get; }
        /// <summary>The best ingredient quality on sale per climb stage (permille, 5 values).</summary>
        public IReadOnlyList<int> StageIngredientCeiling { get; }
        /// <summary>Greenhouse Hill's addition to the ceiling per level (permille, 4 values).</summary>
        public IReadOnlyList<int> GreenhouseCeilingBonus { get; }
        public IReadOnlyList<RevivalUnlock> Unlocks { get; }

        /// <summary>True when the catalog has a row for this district.</summary>
        public bool HasDistrict(string id) => id != null && _index.ContainsKey(id);

        /// <summary>The position of a district in <see cref="Districts"/> (throws for an unknown id).</summary>
        public int IndexOf(string districtId) =>
            districtId != null && _index.TryGetValue(districtId, out int i) ? i : throw new ArgumentException("Unknown district '" + districtId + "'.");

        /// <summary>The row of a district (throws for an unknown id).</summary>
        public DistrictRevivalDef District(string districtId) => Districts[IndexOf(districtId)];

        /// <summary>An unlock by id (throws for an unknown id).</summary>
        public RevivalUnlock Unlock(string id) =>
            id != null && _unlocks.TryGetValue(id, out RevivalUnlock? u) ? u : throw new ArgumentException("Unknown unlock '" + id + "'.");

        /// <summary>The crew factor (permille): the first row with players &lt;= MaxPlayers; players clamp to 1..16.</summary>
        public int CrewPermille(int players)
        {
            int p = Math.Max(1, Math.Min(MinCrewCoverage, players));
            foreach (RevivalCrewRow r in Crew)
                if (p <= r.MaxPlayers) return r.GoalPermille;
            return Crew[Crew.Count - 1].GoalPermille;
        }

        /// <summary>
        /// The goal of the step from <paramref name="level"/> to the next for a district and crew, in milli-points:
        /// <c>levelGoals[level] * 1000 * goalPermille / 1000 * crew / 1000</c> (floored, at least 1). Throws at Thriving.
        /// </summary>
        public long GoalMilli(string districtId, RevivalLevel level, int players)
        {
            if (level < RevivalLevel.Grey || level >= RevivalLevel.Thriving) throw new ArgumentOutOfRangeException(nameof(level), "Thriving has no next step.");
            long g = LevelGoals[(int)level] * 1000L;
            g = g * District(districtId).GoalPermille / 1000;
            g = g * CrewPermille(players) / 1000;
            return Math.Max(1L, g);
        }

        private void Validate(TownCatalog town)
        {
            if (LevelGoals.Count != 3) throw new ArgumentException("levelGoals needs 3 values (to Patched, Lively, Thriving).");
            for (int i = 0; i < LevelGoals.Count; i++)
            {
                if (LevelGoals[i] <= 0) throw new ArgumentException("levelGoals must be > 0.");
                if (i > 0 && LevelGoals[i] <= LevelGoals[i - 1]) throw new ArgumentException("levelGoals must be rising.");
            }
            if (PointsByStars.Count != 5) throw new ArgumentException("pointsByStars needs 5 values (stars 1..5).");
            for (int i = 0; i < PointsByStars.Count; i++)
            {
                if (PointsByStars[i] < 0) throw new ArgumentException("pointsByStars must be >= 0.");
                if (i > 0 && PointsByStars[i] < PointsByStars[i - 1]) throw new ArgumentException("pointsByStars must be non-decreasing.");
            }
            if (SpillPermille < 0 || SpillPermille > MaxSpillPermille) throw new ArgumentException("spillPermille must be 0.." + MaxSpillPermille + ".");

            if (Crew.Count == 0) throw new ArgumentException("crew needs at least one row.");
            for (int i = 0; i < Crew.Count; i++)
            {
                RevivalCrewRow r = Crew[i];
                if (r.MaxPlayers < 1) throw new ArgumentException("crew MaxPlayers must be >= 1.");
                if (i > 0 && r.MaxPlayers <= Crew[i - 1].MaxPlayers) throw new ArgumentException("crew rows must be sorted by MaxPlayers.");
                if (r.GoalPermille < 1 || r.GoalPermille > MaxGoalPermille) throw new ArgumentException("crew GoalPermille must be 1.." + MaxGoalPermille + ".");
            }
            if (Crew[Crew.Count - 1].MaxPlayers < MinCrewCoverage) throw new ArgumentException("the last crew row must cover " + MinCrewCoverage + " players.");

            for (int i = 0; i < Districts.Count; i++)
            {
                DistrictRevivalDef d = Districts[i];
                if (d.DistrictId == null || _index.ContainsKey(d.DistrictId)) throw new ArgumentException("Duplicate or missing district id '" + d.DistrictId + "'.");
                if (!town.Districts.Any(t => t.Id == d.DistrictId)) throw new ArgumentException("District '" + d.DistrictId + "' is not a town district.");
                _index[d.DistrictId] = i;
                if (d.GoalPermille < 1 || d.GoalPermille > MaxGoalPermille) throw new ArgumentException(d.DistrictId + ": GoalPermille must be 1.." + MaxGoalPermille + ".");
                if (d.StartProgressPpm < 0 || d.StartProgressPpm >= Ppm) throw new ArgumentException(d.DistrictId + ": StartProgressPpm must be 0..999,999.");
            }
            foreach (DistrictDef t in town.Districts)
                if (!_index.ContainsKey(t.Id)) throw new ArgumentException("Town district '" + t.Id + "' has no revival row.");
            foreach (DistrictRevivalDef d in Districts)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string n in d.Neighbours)
                {
                    if (n == null || !_index.ContainsKey(n)) throw new ArgumentException(d.DistrictId + ": unknown neighbour '" + n + "'.");
                    if (n == d.DistrictId) throw new ArgumentException(d.DistrictId + ": a district is not its own neighbour.");
                    if (!seen.Add(n)) throw new ArgumentException(d.DistrictId + ": duplicate neighbour " + n + ".");
                }
            }

            if (StageBonusPpm < 0 || StageBonusPpm >= Ppm) throw new ArgumentException("stageBonusPpm must be 0..999,999.");
            if (FootfallPermille.Count != Levels) throw new ArgumentException("footfallPermille needs 4 values (one per level).");
            if (FootfallPermille[0] != 1000) throw new ArgumentException("footfallPermille must start at 1000 (Grey).");
            for (int i = 1; i < FootfallPermille.Count; i++)
                if (FootfallPermille[i] < FootfallPermille[i - 1] || FootfallPermille[i] > MaxFootfallPermille)
                    throw new ArgumentException("footfallPermille must be non-decreasing and at most " + MaxFootfallPermille + ".");
            if (StageIngredientCeiling.Count != 5) throw new ArgumentException("stageIngredientCeiling needs 5 values (stages 1..5).");
            if (GreenhouseCeilingBonus.Count != Levels) throw new ArgumentException("greenhouseCeilingBonus needs 4 values (one per level).");
            if (StageIngredientCeiling.Concat(GreenhouseCeilingBonus).Any(v => v < 0 || v > 1000)) throw new ArgumentException("ingredient ceilings must be 0..1000.");

            foreach (RevivalUnlock u in Unlocks)
            {
                if (u.Id == null || !IdPattern.IsMatch(u.Id)) throw new ArgumentException("Bad unlock id '" + u.Id + "': use ^[a-z0-9_]{1,32}$.");
                if (_unlocks.ContainsKey(u.Id)) throw new ArgumentException("Duplicate unlock id " + u.Id + ".");
                if (!HasDistrict(u.DistrictId)) throw new ArgumentException(u.Id + ": unknown district '" + u.DistrictId + "'.");
                if (!Enum.IsDefined(typeof(RevivalLevel), u.MinLevel)) throw new ArgumentException(u.Id + ": unknown level.");
                _unlocks[u.Id] = u;
            }
        }

        private static IReadOnlyList<int> Ints(IEnumerable<int> values, string name) =>
            (values ?? throw new ArgumentException(name + " are required.", name)).ToList().AsReadOnly();

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> items, string name) where T : class
        {
            if (items == null) throw new ArgumentException(name + " are required.", name);
            var list = items.ToList();
            if (list.Any(i => i == null)) throw new ArgumentException(name + " contain null.", name);
            return list.AsReadOnly();
        }

        /// <summary>
        /// The default revival of Haven Nine (spec 065 "Default numbers"; provisional): one row per town district in town
        /// order, neighbours copied from the town. Tuning round 1 (see the spec's tuning log): level goals 400 / 3,100 /
        /// 15,000; crew rows 290 / 540 / 800 for 1-3 players and 250 x players from 4 (052b's guest demand D = n / 4 has no
        /// plateau, so a 16-player crew serves 4x the meals of 4); footfall 1000 / 1040 / 1070 / 1100 (V6).
        /// </summary>
        public static RevivalCatalog CreateDefault(TownCatalog town)
        {
            if (town == null) throw new ArgumentException("A town is required.", nameof(town));
            var districts = town.Districts.Select(d => new DistrictRevivalDef(d.Id,
                d.Id == "low_ring" ? 1500 : 1000,
                d.Id == "station_square" ? 500_000 : 0,
                d.Neighbours)).ToList();
            var crew = new List<RevivalCrewRow>
            {
                new RevivalCrewRow(1, 290),
                new RevivalCrewRow(2, 540),
                new RevivalCrewRow(3, 800),
            };
            for (int n = 4; n <= MinCrewCoverage; n++) crew.Add(new RevivalCrewRow(n, n * 250));
            var unlocks = new[]
            {
                new RevivalUnlock("robot_workshop", "workshop_lane", RevivalLevel.Lively),
                new RevivalUnlock("greenhouse_stall", "greenhouse_hill", RevivalLevel.Patched),
            };
            return new RevivalCatalog(town,
                levelGoals: new[] { 400, 3100, 15000 },
                pointsByStars: new[] { 0, 1, 2, 4, 8 },
                spillPermille: 250,
                crew: crew,
                districts: districts,
                stageBonusPpm: 100_000,
                footfallPermille: new[] { 1000, 1040, 1070, 1100 },
                stageIngredientCeiling: new[] { 650, 720, 790, 860, 930 },
                greenhouseCeilingBonus: new[] { 0, 20, 40, 70 },
                unlocks: unlocks);
        }
    }
}
