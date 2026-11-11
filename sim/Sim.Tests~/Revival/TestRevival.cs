using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Revival;
using Worktown.Sim.Town;

namespace Worktown.Sim.Tests.Revival
{
    /// <summary>
    /// Spec 065 test fixture: catalogs on the default town with the spec's literal (pre-tuning) numbers, and editable parts so
    /// a test can change one rule. <see cref="Flat"/> has every district at goal 1000 and start 0 (easy literals).
    /// </summary>
    internal static class TestRevival
    {
        public static readonly TownCatalog Town = TownCatalog.CreateDefault();

        /// <summary>The spec's crew rows (open question 1's default).</summary>
        public static RevivalCrewRow[] SpecCrew() => new[]
        {
            new RevivalCrewRow(1, 450), new RevivalCrewRow(2, 650), new RevivalCrewRow(3, 850), new RevivalCrewRow(7, 1000),
            new RevivalCrewRow(16, 1000),
        };

        public sealed class Parts
        {
            public List<int> Goals = new List<int> { 300, 4000, 20000 };
            public List<int> Points = new List<int> { 0, 1, 2, 4, 8 };
            public int Spill = 250;
            public List<RevivalCrewRow> Crew = SpecCrew().ToList();
            public List<DistrictRevivalDef> Districts = Town.Districts
                .Select(d => new DistrictRevivalDef(d.Id, d.Id == "low_ring" ? 1500 : 1000, d.Id == "station_square" ? 500_000 : 0, d.Neighbours)).ToList();
            public int StageBonus = 100_000;
            public List<int> Footfall = new List<int> { 1000, 1100, 1200, 1300 };
            public List<int> Ceiling = new List<int> { 650, 720, 790, 860, 930 };
            public List<int> Greenhouse = new List<int> { 0, 20, 40, 70 };
            public List<RevivalUnlock> Unlocks = new List<RevivalUnlock>
            {
                new RevivalUnlock("robot_workshop", "workshop_lane", RevivalLevel.Lively),
                new RevivalUnlock("greenhouse_stall", "greenhouse_hill", RevivalLevel.Patched),
            };

            public RevivalCatalog Build() => new RevivalCatalog(Town, Goals, Points, Spill, Crew, Districts, StageBonus, Footfall, Ceiling,
                Greenhouse, Unlocks);
        }

        /// <summary>The spec's literal default numbers.</summary>
        public static RevivalCatalog Spec() => new Parts().Build();

        /// <summary>The spec's numbers with every district at goal 1000 and start 0, and the given spill.</summary>
        public static RevivalCatalog Flat(int spill = 250)
        {
            var p = new Parts { Spill = spill };
            p.Districts = p.Districts.Select(d => d with { GoalPermille = 1000, StartProgressPpm = 0 }).ToList();
            return p.Build();
        }
    }
}
