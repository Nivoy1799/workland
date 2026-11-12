using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Revival;
using Worktown.Sim.Town;
using Xunit;
using R = Worktown.Sim.Tests.Revival.TestRevival;

namespace Worktown.Sim.Tests.Revival
{
    public class RevivalCatalogTests
    {
        private static void Throws(R.Parts p, string fragment)
        {
            var e = Assert.Throws<ArgumentException>(() => p.Build());
            Assert.Contains(fragment, e.Message);
        }

        private static R.Parts With(Action<R.Parts> change)
        {
            var p = new R.Parts();
            change(p);
            return p;
        }

        [Fact]
        public void CreateDefaultHasOneRowPerTownDistrictInTownOrder() // AC3
        {
            RevivalCatalog c = RevivalCatalog.CreateDefault(TownCatalog.CreateDefault());
            Assert.Equal(12, c.Districts.Count);
            Assert.Equal(R.Town.Districts.Select(d => d.Id), c.Districts.Select(d => d.DistrictId));
            foreach (DistrictRevivalDef d in c.Districts) Assert.Equal(R.Town.District(d.DistrictId).Neighbours, d.Neighbours);
            Assert.Equal(1500, c.District("low_ring").GoalPermille);
            Assert.Equal(500_000, c.District("station_square").StartProgressPpm);
            Assert.All(c.Districts.Where(d => d.DistrictId != "low_ring"), d => Assert.Equal(1000, d.GoalPermille));
            Assert.Equal(new[] { 0, 1, 2, 4, 8 }, c.PointsByStars);
            Assert.Equal(250, c.SpillPermille);
            Assert.Equal(100_000, c.StageBonusPpm);
        }

        [Fact]
        public void CreateDefaultNeedsATown() => Assert.Throws<ArgumentException>(() => RevivalCatalog.CreateDefault(null!));

        [Fact]
        public void SpecNumbersBuild() => Assert.Equal(12, R.Spec().Districts.Count);

        // AC3: one throwing literal per rule.
        [Fact] public void TwoLevelGoalsThrow() => Throws(With(p => p.Goals = new List<int> { 300, 4000 }), "needs 3 values");
        [Fact] public void ZeroLevelGoalThrows() => Throws(With(p => p.Goals = new List<int> { 0, 4000, 20000 }), "must be > 0");
        [Fact] public void FallingLevelGoalsThrow() => Throws(With(p => p.Goals = new List<int> { 300, 300, 20000 }), "must be rising");
        [Fact] public void FourStarValuesThrow() => Throws(With(p => p.Points = new List<int> { 0, 1, 2, 4 }), "needs 5 values");
        [Fact] public void NegativePointsThrow() => Throws(With(p => p.Points = new List<int> { -1, 1, 2, 4, 8 }), "pointsByStars must be 0..1000");
        [Fact] public void DecreasingPointsThrow() => Throws(With(p => p.Points = new List<int> { 0, 2, 1, 4, 8 }), "non-decreasing");
        [Fact] public void PointsAbove1000Throw() => Throws(With(p => p.Points = new List<int> { 0, 1, 2, 4, 1001 }), "pointsByStars must be 0..1000");
        [Fact] public void SpillAbove500Throws() => Throws(With(p => p.Spill = 501), "spillPermille must be 0..500");
        [Fact] public void NegativeSpillThrows() => Throws(With(p => p.Spill = -1), "spillPermille must be 0..500");
        [Fact] public void NoCrewRowThrows() => Throws(With(p => p.Crew.Clear()), "at least one row");
        [Fact] public void UnsortedCrewThrows() => Throws(With(p => p.Crew.Reverse()), "sorted by MaxPlayers");
        [Fact] public void CrewNotCovering16Throws() => Throws(With(p => p.Crew.RemoveAt(p.Crew.Count - 1)), "must cover 16 players");
        [Fact] public void CrewPermilleZeroThrows() => Throws(With(p => p.Crew[0] = new RevivalCrewRow(1, 0)), "GoalPermille must be 1..5000");
        [Fact] public void CrewPermilleAbove5000Throws() => Throws(With(p => p.Crew[0] = new RevivalCrewRow(1, 5001)), "GoalPermille must be 1..5000");
        [Fact] public void CrewMaxPlayersZeroThrows() => Throws(With(p => p.Crew.Insert(0, new RevivalCrewRow(0, 100))), "MaxPlayers must be >= 1");
        [Fact] public void MissingTownDistrictThrows() => Throws(With(p => p.Districts.RemoveAt(0)), "Town district 'station_square' has no revival row");
        [Fact] public void ExtraDistrictThrows() => Throws(With(p => p.Districts.Add(new DistrictRevivalDef("moon_base", 1000, 0, new string[0]))), "is not a town district");
        [Fact] public void DuplicateDistrictThrows() => Throws(With(p => p.Districts.Add(p.Districts[0])), "Duplicate or missing district id");
        [Fact] public void DistrictGoalZeroThrows() => Throws(With(p => p.Districts[1] = p.Districts[1] with { GoalPermille = 0 }), "main_street: GoalPermille must be 1..5000");
        [Fact] public void StartAtAFullStepThrows() => Throws(With(p => p.Districts[0] = p.Districts[0] with { StartProgressPpm = 1_000_000 }), "StartProgressPpm must be 0..999,999");
        [Fact] public void NegativeStartThrows() => Throws(With(p => p.Districts[0] = p.Districts[0] with { StartProgressPpm = -1 }), "StartProgressPpm must be 0..999,999");
        [Fact] public void UnknownNeighbourThrows() => Throws(With(p => p.Districts[0] = p.Districts[0] with { Neighbours = new[] { "nowhere" } }), "unknown neighbour 'nowhere'");
        [Fact] public void SelfNeighbourThrows() => Throws(With(p => p.Districts[0] = p.Districts[0] with { Neighbours = new[] { "station_square" } }), "not its own neighbour");
        [Fact] public void DuplicateNeighbourThrows() => Throws(With(p => p.Districts[0] = p.Districts[0] with { Neighbours = new[] { "main_street", "main_street" } }), "duplicate neighbour");
        [Fact] public void StageBonusOfAFullStepThrows() => Throws(With(p => p.StageBonus = 1_000_000), "stageBonusPpm must be 0..999,999");
        [Fact] public void ThreeFootfallValuesThrow() => Throws(With(p => p.Footfall = new List<int> { 1000, 1100, 1200 }), "needs 4 values");
        [Fact] public void FootfallNotFrom1000Throws() => Throws(With(p => p.Footfall = new List<int> { 900, 1100, 1200, 1300 }), "must start at 1000");
        [Fact] public void FallingFootfallThrows() => Throws(With(p => p.Footfall = new List<int> { 1000, 1200, 1100, 1300 }), "non-decreasing");
        [Fact] public void FourStageCeilingsThrow() => Throws(With(p => p.Ceiling = new List<int> { 650, 720, 790, 860 }), "needs 5 values");
        [Fact] public void ThreeGreenhouseBonusesThrow() => Throws(With(p => p.Greenhouse = new List<int> { 0, 20, 40 }), "needs 4 values");
        [Fact] public void CeilingAbove1000Throws() => Throws(With(p => p.Ceiling[4] = 1001), "ingredient ceilings must be 0..1000");
        [Fact] public void NegativeGreenhouseBonusThrows() => Throws(With(p => p.Greenhouse[0] = -1), "ingredient ceilings must be 0..1000");
        [Fact] public void BadUnlockIdThrows() => Throws(With(p => p.Unlocks.Add(new RevivalUnlock("Robot-Shop", "workshop_lane", RevivalLevel.Lively))), "Bad unlock id 'Robot-Shop'");
        [Fact] public void UnlockInAnUnknownDistrictThrows() => Throws(With(p => p.Unlocks.Add(new RevivalUnlock("moon_shop", "moon_base", RevivalLevel.Lively))), "unknown district 'moon_base'");
        [Fact] public void DuplicateUnlockThrows() => Throws(With(p => p.Unlocks.Add(p.Unlocks[0])), "Duplicate unlock id robot_workshop");

        [Fact]
        public void NullTownThrows() => Assert.Throws<ArgumentException>(() => new RevivalCatalog(null!, new[] { 1, 2, 3 }, new[] { 0, 1, 2, 3, 4 }, 0,
            R.SpecCrew(), new DistrictRevivalDef[0], 0, new[] { 1000, 1000, 1000, 1000 }, new[] { 0, 0, 0, 0, 0 }, new[] { 0, 0, 0, 0 }, new RevivalUnlock[0]));

        [Fact]
        public void CatalogCopiesItsInputs()
        {
            var p = new R.Parts();
            var neighbours = new List<string> { "main_street" };
            p.Districts[0] = p.Districts[0] with { Neighbours = neighbours };
            RevivalCatalog c = p.Build();
            p.Goals[0] = 1;
            neighbours.Add("low_ring");
            Assert.Equal(300, c.LevelGoals[0]);
            Assert.Equal(new[] { "main_street" }, c.District("station_square").Neighbours);
        }

        [Fact]
        public void LookupsFindAndThrow()
        {
            RevivalCatalog c = R.Spec();
            Assert.True(c.HasDistrict("low_ring"));
            Assert.False(c.HasDistrict("moon_base"));
            Assert.False(c.HasDistrict(null!));
            Assert.Equal(11, c.IndexOf("low_ring"));
            Assert.Throws<ArgumentException>(() => c.IndexOf("moon_base"));
            Assert.Equal("low_ring", c.District("low_ring").DistrictId);
            Assert.Throws<ArgumentException>(() => c.District("moon_base"));
            Assert.Equal(RevivalLevel.Lively, c.Unlock("robot_workshop").MinLevel);
            Assert.Throws<ArgumentException>(() => c.Unlock("nope"));
        }

        [Fact]
        public void CrewPermilleTakesTheFirstRowAndClampsPlayers()
        {
            RevivalCatalog c = R.Spec();
            Assert.Equal(450, c.CrewPermille(1));
            Assert.Equal(450, c.CrewPermille(0));   // clamps to 1
            Assert.Equal(650, c.CrewPermille(2));
            Assert.Equal(850, c.CrewPermille(3));
            Assert.Equal(1000, c.CrewPermille(4));
            Assert.Equal(1000, c.CrewPermille(16));
            Assert.Equal(1000, c.CrewPermille(40));  // clamps to 16
        }

        [Fact]
        public void DefaultCrewFollowsTheGuestDemandFromFourPlayers() // tuning round 1: 052b's demand D = n / 4 has no plateau
        {
            RevivalCatalog c = RevivalCatalog.CreateDefault(R.Town);
            Assert.Equal(new[] { 137, 375, 675 }, new[] { 1, 2, 3 }.Select(c.CrewPermille)); // 065 review: n x h(n) / 4 (D40)
            for (int n = 4; n <= 16; n++) Assert.Equal(n * 250, c.CrewPermille(n));
            Assert.Equal(new[] { 400, 3100, 15000 }, c.LevelGoals);
        }

        [Fact]
        public void GoalMilliIsTheSpecFormula()
        {
            RevivalCatalog c = R.Spec();
            Assert.Equal(300_000, c.GoalMilli("main_street", RevivalLevel.Grey, 4));
            Assert.Equal(135_000, c.GoalMilli("main_street", RevivalLevel.Grey, 1));          // 300 x 450 / 1000
            Assert.Equal(6_000_000, c.GoalMilli("low_ring", RevivalLevel.Patched, 4));        // 4000 x 1500 / 1000
            Assert.Equal(20_000_000, c.GoalMilli("main_street", RevivalLevel.Lively, 16));
            Assert.Throws<ArgumentOutOfRangeException>(() => c.GoalMilli("main_street", RevivalLevel.Thriving, 4));
        }
    }
}
