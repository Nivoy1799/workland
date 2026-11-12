using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Climb;
using Worktown.Sim.Revival;
using Worktown.Sim.Tests.Climb;

namespace Worktown.Sim.Tests.Revival
{
    /// <summary>Spec 065 test fixture: hand-built climb runs and revival traces.</summary>
    internal static class TestRuns
    {
        public const int Step = 420;

        /// <summary>A normal service with per-channel guests (channel ids in id order).</summary>
        public static ServiceResult Normal(int index, ClimbStage stage, int served, int quality, params (string Id, int Guests)[] channels) =>
            new ServiceResult(index, stage, null, (long)index * Step, Step, served, served, 0, 0, 0, 0, 0, 0, 0, 0, quality, 60, 40, 500,
                channels.Select(c => c.Id).ToList(), channels.Select(c => c.Guests).ToList(), channels.Select(c => -1).ToList(),
                Array.Empty<string>(), Array.Empty<long>());

        /// <summary>A big job service (event or competition).</summary>
        public static ServiceResult Job(int index, ClimbStage stage, string jobId, int served, int quality) =>
            new ServiceResult(index, stage, jobId, (long)index * Step, Step, served, served, 0, 0, 0, 0, 0, 0, 0, 0, quality, 0, 0, 500,
                Array.Empty<string>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<string>(), Array.Empty<long>());

        public static ClimbRun Run(int players, IEnumerable<ServiceResult> services, ulong seed = 1, long horizon = 40 * 3600) =>
            new ClimbRun(new CrewSetup(players, "average", ClimbStrategy.Reference, seed), horizon, services.ToList(),
                TestClimb.Engine.Start(TestClimb.Setup()));

        /// <summary>
        /// The AC9 run: 3 services, 2 channels (corner_counter, delivery), a stage change (1 to 3, so two stage bonuses) and an
        /// event; quality 650 = 4 stars.
        /// </summary>
        public static ClimbRun Ac9() => Run(4, new[]
        {
            Normal(0, ClimbStage.FoodCorner, 10, 650, ("corner_counter", 10)),
            Normal(1, ClimbStage.FirstRestaurant, 20, 650, ("corner_counter", 9), ("delivery", 3)),
            Job(2, ClimbStage.FirstRestaurant, "street_festival", 30, 450),
        });

        /// <summary>
        /// A hand-built trace: <paramref name="levels"/> maps a district to its (Patched, Lively, Thriving) hours (-1 never);
        /// unlisted districts never level. Stage entries are hours for S1..S5 (-1 never).
        /// </summary>
        public static RevivalTrace Trace(int players, double[] stages, Dictionary<string, double[]> levels, bool dropped = false, ulong seed = 1,
            long horizon = 40 * 3600)
        {
            RevivalCatalog c = TestRevival.Spec();
            long S(double h) => h < 0 ? -1 : (long)Math.Round(h * 3600);
            var rows = c.Districts.Select(d => (IReadOnlyList<long>)(levels.TryGetValue(d.DistrictId, out double[]? l) ? l.Select(S).ToList() : new List<long> { -1, -1, -1 })).ToList();
            return new RevivalTrace(players, seed, horizon, c.Districts.Select(d => d.DistrictId).ToList(), rows, stages.Select(S).ToList(),
                c.Districts.Select(_ => 0).ToList(), 0, dropped, "");
        }

        /// <summary>The climb's reference stage hours (S1..S5) used by the evaluation tests.</summary>
        public static readonly double[] Stages = { 0, 1.2, 3.9, 7.9, 15.3 };

        /// <summary>A trace where every target passes: shop Patched 1.5 h, Lively 5 h, Thriving 16 h; every district Patched; the Low Ring last.</summary>
        public static Dictionary<string, double[]> Good() => TestRevival.Spec().Districts.ToDictionary(d => d.DistrictId, d => d.DistrictId switch
        {
            "main_street" => new[] { 1.5, 5.0, 16.0 },
            "low_ring" => new[] { 30.0, -1, -1 },
            _ => new[] { 10.0, -1, -1 },
        });
    }
}
