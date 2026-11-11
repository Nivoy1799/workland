using System;
using System.Collections.Generic;
using System.Linq;

namespace Worktown.Sim.Revival
{
    /// <summary>
    /// Spec 065: what one climb replay did to the town. Per district (catalog order) the play seconds (service end) at which it
    /// reached Patched, Lively and Thriving (-1: never), the meals eaten there (not counting spill); the climb's stage entry
    /// seconds (stages 1..5, -1: never), how many stage bonuses were given, whether any level ever dropped and the final
    /// state's digest. Built by <see cref="RevivalReplay.Replay"/>, or by hand for the evaluation tests. Immutable.
    /// </summary>
    public sealed class RevivalTrace
    {
        private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);

        public RevivalTrace(int players, ulong seed, long horizonSeconds, IReadOnlyList<string> districtIds,
            IReadOnlyList<IReadOnlyList<long>> levelSeconds, IReadOnlyList<long> stageEnteredSeconds, IReadOnlyList<int> meals,
            int stageEntries, bool levelDropped, string finalDigest)
        {
            if (districtIds == null || levelSeconds == null || stageEnteredSeconds == null || meals == null)
                throw new ArgumentException("Trace lists are required.");
            if (levelSeconds.Count != districtIds.Count || meals.Count != districtIds.Count)
                throw new ArgumentException("A trace needs one level row and one meal count per district.");
            if (levelSeconds.Any(r => r == null || r.Count != 3)) throw new ArgumentException("A level row needs 3 values (Patched, Lively, Thriving).");
            if (stageEnteredSeconds.Count != 5) throw new ArgumentException("A trace needs 5 stage entry values.");
            Players = players;
            Seed = seed;
            HorizonSeconds = horizonSeconds;
            DistrictIds = districtIds.ToList().AsReadOnly();
            for (int i = 0; i < DistrictIds.Count; i++)
            {
                if (DistrictIds[i] == null || _index.ContainsKey(DistrictIds[i])) throw new ArgumentException("Duplicate or missing district id.");
                _index[DistrictIds[i]] = i;
            }
            Levels = levelSeconds.Select(r => (IReadOnlyList<long>)r.ToList().AsReadOnly()).ToList().AsReadOnly();
            StageEntered = stageEnteredSeconds.ToList().AsReadOnly();
            Meals = meals.ToList().AsReadOnly();
            StageEntries = stageEntries;
            LevelDropped = levelDropped;
            FinalDigest = finalDigest ?? "";
        }

        public int Players { get; }
        public ulong Seed { get; }
        public long HorizonSeconds { get; }
        public IReadOnlyList<string> DistrictIds { get; }
        /// <summary>Per district, the seconds to Patched, Lively and Thriving (-1: never).</summary>
        public IReadOnlyList<IReadOnlyList<long>> Levels { get; }
        /// <summary>Seconds the climb entered stages 1..5 (index 0..4; -1: never).</summary>
        public IReadOnlyList<long> StageEntered { get; }
        public IReadOnlyList<int> Meals { get; }
        public int StageEntries { get; }
        public bool LevelDropped { get; }
        public string FinalDigest { get; }

        /// <summary>Seconds at which the district reached <paramref name="level"/> (0 for Grey, -1: never; throws for an unknown id).</summary>
        public long LevelSeconds(string districtId, RevivalLevel level) =>
            level == RevivalLevel.Grey ? 0 : Levels[Index(districtId)][(int)level - 1];

        /// <summary>Seconds the climb entered <paramref name="stage"/> (1..5; -1: never or out of range).</summary>
        public long StageEnteredSeconds(int stage) => stage < 1 || stage > 5 ? -1 : StageEntered[stage - 1];

        /// <summary>Meals eaten in the district (spill not counted; throws for an unknown id).</summary>
        public int MealsIn(string districtId) => Meals[Index(districtId)];

        private int Index(string id) =>
            id != null && _index.TryGetValue(id, out int i) ? i : throw new ArgumentException("Unknown district '" + id + "'.");
    }
}
