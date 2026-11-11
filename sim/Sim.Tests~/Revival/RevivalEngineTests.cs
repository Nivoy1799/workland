using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Revival;
using Xunit;
using R = Worktown.Sim.Tests.Revival.TestRevival;

namespace Worktown.Sim.Tests.Revival
{
    public class RevivalEngineTests
    {
        private const int Million = 1_000_000;

        [Fact]
        public void StartIsGreyWithTheStartProgress()
        {
            RevivalState s = RevivalEngine.Start(R.Spec());
            Assert.All(s.Districts, d => Assert.Equal(RevivalLevel.Grey, d.Level));
            Assert.Equal(500_000, s.ProgressPpm("station_square"));
            Assert.Equal(0, s.ProgressPpm("main_street"));
            Assert.Throws<ArgumentException>(() => RevivalEngine.Start(null!));
        }

        // ---------- AC4 ----------

        [Fact]
        public void AFourStarMealForFourPlayersAtGoal300AddsTheExactPpm()
        {
            RevivalCatalog c = R.Flat();
            // workshop_lane has one neighbour (market_hall): 4,000 milli, 1,000 spill, 3,000 home; goal 300,000 milli.
            (RevivalState s, IReadOnlyList<LevelUp> ups) = RevivalEngine.AddMeals(RevivalEngine.Start(c), c, "workshop_lane", 4, 1, 4);
            Assert.Equal(10_000, s.ProgressPpm("workshop_lane"));
            Assert.Equal(3_333, s.ProgressPpm("market_hall"));
            Assert.Empty(ups);
            Assert.Equal(0, s.ProgressPpm("main_street"));
        }

        [Fact]
        public void OneStarMealsAddNothing()
        {
            RevivalCatalog c = R.Flat();
            RevivalState start = RevivalEngine.Start(c);
            (RevivalState s, IReadOnlyList<LevelUp> ups) = RevivalEngine.AddMeals(start, c, "main_street", 1, 1000, 4);
            Assert.Equal(start, s);
            Assert.Empty(ups);
        }

        [Fact]
        public void ZeroMealsAddNothing()
        {
            RevivalCatalog c = R.Flat();
            RevivalState start = RevivalEngine.Start(c);
            Assert.Equal(start, RevivalEngine.AddMeals(start, c, "main_street", 5, 0, 4).State);
        }

        [Fact]
        public void SpillSplitsOverTwoNeighboursWithTheRemainderAtHome()
        {
            // Default spill 250: tower_rise has two neighbours (station_square, low_ring). 2 stars x 1 = 1,000 milli: 250 spill,
            // 125 each, 750 home. With spill 333 the 333 splits 166 + 166 and the remainder 1 stays: 668 home.
            RevivalCatalog c = R.Flat();
            RevivalState s = RevivalEngine.AddMeals(RevivalEngine.Start(c), c, "tower_rise", 2, 1, 4).State;
            Assert.Equal(750L * Million / 300_000, s.ProgressPpm("tower_rise"));
            Assert.Equal(125L * Million / 300_000, s.ProgressPpm("station_square"));
            Assert.Equal(125L * Million / 300_000, s.ProgressPpm("low_ring"));

            RevivalCatalog odd = R.Flat(spill: 333);
            RevivalState t = RevivalEngine.AddMeals(RevivalEngine.Start(odd), odd, "tower_rise", 2, 1, 4).State;
            Assert.Equal(668L * Million / 300_000, t.ProgressPpm("tower_rise")); // 2,226
            Assert.Equal(166L * Million / 300_000, t.ProgressPpm("station_square")); // 553
            Assert.Equal(166L * Million / 300_000, t.ProgressPpm("low_ring"));
            Assert.Equal(0, t.ProgressPpm("main_street")); // spill goes no further
        }

        [Fact]
        public void ALevelUpCarriesTheRescaledRest()
        {
            RevivalCatalog c = R.Flat(spill: 0);
            // 80 four-star meals = 320,000 milli of a 300,000 goal: 1,066,666 ppm, rest 66,666 x 300 / 4000 = 4,999.
            (RevivalState s, IReadOnlyList<LevelUp> ups) = RevivalEngine.AddMeals(RevivalEngine.Start(c), c, "main_street", 4, 80, 4);
            Assert.Equal(RevivalLevel.Patched, s.LevelOf("main_street"));
            Assert.Equal(4_999, s.ProgressPpm("main_street"));
            Assert.Equal(new[] { new LevelUp("main_street", RevivalLevel.Patched) }, ups);
        }

        [Fact]
        public void TwoLevelUpsInOneCall()
        {
            RevivalCatalog c = R.Flat(spill: 0);
            // 600 five-star meals = 4,800,000 milli: 16,000,000 ppm; Patched with 15,000,000 x 300/4000 = 1,125,000; Lively with
            // 125,000 x 4000 / 20000 = 25,000.
            (RevivalState s, IReadOnlyList<LevelUp> ups) = RevivalEngine.AddMeals(RevivalEngine.Start(c), c, "main_street", 5, 600, 4);
            Assert.Equal(RevivalLevel.Lively, s.LevelOf("main_street"));
            Assert.Equal(25_000, s.ProgressPpm("main_street"));
            Assert.Equal(new[] { new LevelUp("main_street", RevivalLevel.Patched), new LevelUp("main_street", RevivalLevel.Lively) }, ups);
        }

        [Fact]
        public void ThriveClampsAtAMillion()
        {
            RevivalCatalog c = R.Flat(spill: 0);
            (RevivalState s, IReadOnlyList<LevelUp> ups) = RevivalEngine.AddMeals(RevivalEngine.Start(c), c, "main_street", 5, 100_000, 4);
            Assert.Equal(RevivalLevel.Thriving, s.LevelOf("main_street"));
            Assert.Equal(Million, s.ProgressPpm("main_street"));
            Assert.Equal(3, ups.Count);
            (RevivalState t, IReadOnlyList<LevelUp> more) = RevivalEngine.AddMeals(s, c, "main_street", 5, 100, 4);
            Assert.Equal(Million, t.ProgressPpm("main_street"));
            Assert.Empty(more);
        }

        [Fact]
        public void BadStarsAndCountsThrow()
        {
            RevivalCatalog c = R.Flat();
            RevivalState s = RevivalEngine.Start(c);
            Assert.Throws<ArgumentOutOfRangeException>(() => RevivalEngine.AddMeals(s, c, "main_street", 0, 1, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => RevivalEngine.AddMeals(s, c, "main_street", 6, 1, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => RevivalEngine.AddMeals(s, c, "main_street", 3, -1, 4));
            Assert.Throws<ArgumentException>(() => RevivalEngine.AddMeals(s, c, "moon_base", 3, 1, 4));
        }

        // ---------- AC5 ----------

        [Fact]
        public void OnePlayerReachesPatchedWith450PermilleOfTheFourPlayerPoints()
        {
            RevivalCatalog c = R.Flat(spill: 0);
            RevivalState start = RevivalEngine.Start(c);
            // Two-star meals are 1 point: 4 players need 300, one player 135 (450 / 1000).
            Assert.Equal(RevivalLevel.Grey, RevivalEngine.AddMeals(start, c, "main_street", 2, 299, 4).State.LevelOf("main_street"));
            Assert.Equal(RevivalLevel.Patched, RevivalEngine.AddMeals(start, c, "main_street", 2, 300, 4).State.LevelOf("main_street"));
            Assert.Equal(RevivalLevel.Grey, RevivalEngine.AddMeals(start, c, "main_street", 2, 134, 1).State.LevelOf("main_street"));
            Assert.Equal(RevivalLevel.Patched, RevivalEngine.AddMeals(start, c, "main_street", 2, 135, 1).State.LevelOf("main_street"));
        }

        [Fact]
        public void SixteenPlayersNeedTheSameAsFourWithTheSpecRows()
        {
            RevivalCatalog c = R.Spec();
            RevivalState four = RevivalEngine.Start(c), sixteen = four;
            foreach ((string d, int stars, int count) in new[] { ("main_street", 4, 90), ("homes", 3, 40), ("low_ring", 5, 70) })
            {
                four = RevivalEngine.AddMeals(four, c, d, stars, count, 4).State;
                sixteen = RevivalEngine.AddMeals(sixteen, c, d, stars, count, 16).State;
            }
            Assert.Equal(four, sixteen);
        }

        [Fact]
        public void ACrewChangeBetweenCallsKeepsEveryLevelAndPpm()
        {
            RevivalCatalog c = R.Flat(spill: 0);
            RevivalState s = RevivalEngine.AddMeals(RevivalEngine.Start(c), c, "main_street", 4, 80, 4).State; // Patched, 4,999 ppm
            Assert.Equal(s, RevivalEngine.AddMeals(s, c, "main_street", 4, 0, 1).State); // the crew shrinks: nothing moves
            // One more two-star meal for a solo crew is 1,000 milli of a 4000 x 450 = 1,800,000 milli step: + 555 ppm.
            RevivalState t = RevivalEngine.AddMeals(s, c, "main_street", 2, 1, 1).State;
            Assert.Equal(RevivalLevel.Patched, t.LevelOf("main_street"));
            Assert.Equal(4_999 + 555, t.ProgressPpm("main_street"));
            Assert.All(c.Districts.Where(d => d.DistrictId != "main_street"), d => Assert.Equal(s.Of(d.DistrictId), t.Of(d.DistrictId)));
        }

        // ---------- AC7 ----------

        [Fact]
        public void EnterStageGivesTheBonusForStagesTwoToFiveOnly()
        {
            RevivalCatalog c = R.Spec();
            RevivalState start = RevivalEngine.Start(c);
            foreach (int stage in new[] { -1, 0, 1, 6 }) Assert.Equal(start, RevivalEngine.EnterStage(start, c, stage).State);
            for (int stage = 2; stage <= 5; stage++)
            {
                (RevivalState s, IReadOnlyList<LevelUp> ups) = RevivalEngine.EnterStage(start, c, stage);
                Assert.Equal(600_000, s.ProgressPpm("station_square"));
                Assert.Equal(100_000, s.ProgressPpm("main_street"));
                Assert.Empty(ups);
            }
        }

        [Fact]
        public void EnterStageRaisesAtMostOneLevelPerCall()
        {
            var p = new R.Parts { StageBonus = 999_999 };
            RevivalCatalog c = p.Build();
            (RevivalState s, IReadOnlyList<LevelUp> ups) = RevivalEngine.EnterStage(RevivalEngine.Start(c), c, 2);
            // station_square: 500,000 + 999,999 = 1,499,999: Patched with 499,999 x 300 / 4000 = 37,499.
            Assert.Equal(RevivalLevel.Patched, s.LevelOf("station_square"));
            Assert.Equal(37_499, s.ProgressPpm("station_square"));
            Assert.Equal(RevivalLevel.Grey, s.LevelOf("main_street"));
            Assert.Equal(999_999, s.ProgressPpm("main_street"));
            Assert.Single(ups);
            for (int i = 0; i < 6; i++)
            {
                RevivalState before = s;
                (s, ups) = RevivalEngine.EnterStage(s, c, 3);
                foreach (DistrictRevival d in s.Districts) Assert.True(d.Level - before.LevelOf(d.DistrictId) <= 1);
            }
            Assert.All(s.Districts, d => Assert.Equal(RevivalLevel.Thriving, d.Level));
            Assert.All(s.Districts, d => Assert.Equal(Million, d.ProgressPpm));
        }

        [Fact]
        public void FromSavedClampsIgnoresUnknownIdsAndFillsMissingDistricts()
        {
            RevivalCatalog c = R.Spec();
            var saved = new SavedRevival(new[] { "main_street", "moon_base", "homes", "low_ring", "main_street", "lakefront" },
                new[] { 7, 2, -1, 2, 0, 1 }, new[] { 5, 9, -5, 2_000_000, 1, 123 });
            RevivalState s = RevivalState.FromSaved(c, saved);
            Assert.Equal(new DistrictRevival("main_street", RevivalLevel.Thriving, Million), s.Of("main_street")); // 7 -> 3, full
            Assert.Equal(new DistrictRevival("homes", RevivalLevel.Grey, 0), s.Of("homes"));                      // -1 -> 0, -5 -> 0
            Assert.Equal(new DistrictRevival("low_ring", RevivalLevel.Lively, Million), s.Of("low_ring"));         // ppm clamps
            Assert.Equal(new DistrictRevival("station_square", RevivalLevel.Grey, 500_000), s.Of("station_square")); // missing: start
            Assert.Equal(new DistrictRevival("lakefront", RevivalLevel.Patched, 123), s.Of("lakefront")); // read as saved
            Assert.Equal(c.Districts.Select(d => d.DistrictId), s.Districts.Select(d => d.DistrictId));
            Assert.Throws<ArgumentException>(() => s.Of("moon_base"));
        }

        [Fact]
        public void FromSavedReadsOnlyTheShortestList()
        {
            RevivalCatalog c = R.Spec();
            RevivalState s = RevivalState.FromSaved(c, new SavedRevival(new[] { "main_street", "homes" }, new[] { 2 }, new[] { 10, 20 }));
            Assert.Equal(RevivalLevel.Lively, s.LevelOf("main_street"));
            Assert.Equal(RevivalLevel.Grey, s.LevelOf("homes"));
            Assert.Equal(RevivalEngine.Start(c), RevivalState.FromSaved(c, null));
        }

        [Fact]
        public void ToSavedThenFromSavedRoundTrips()
        {
            RevivalCatalog c = R.Spec();
            RevivalState s = RevivalEngine.AddMeals(RevivalEngine.Start(c), c, "main_street", 5, 700, 4).State;
            s = RevivalEngine.EnterStage(s, c, 3).State;
            SavedRevival saved = s.ToSaved();
            Assert.Equal(c.Districts.Select(d => d.DistrictId), saved.DistrictIds);
            RevivalState back = RevivalState.FromSaved(c, saved);
            Assert.Equal(s, back);
            Assert.Equal(s.Digest, back.Digest);
            Assert.Equal(s.GetHashCode(), back.GetHashCode());
            Assert.True(s.Equals((object)back));
            Assert.False(s.Equals(null));
        }

        [Fact]
        public void StateCanonicalAndDigestAreStable()
        {
            RevivalState s = RevivalEngine.Start(R.Spec());
            Assert.StartsWith("revival065\nstation_square 0 500000\nmain_street 0 0\n", s.Canonical);
            Assert.Equal(64, s.Digest.Length);
            Assert.Equal(s.Digest, RevivalEngine.Start(R.Spec()).Digest);
            Assert.NotEqual(s.Digest, RevivalEngine.EnterStage(s, R.Spec(), 2).State.Digest);
        }

        [Fact]
        public void StateRejectsBadEntries()
        {
            Assert.Throws<ArgumentException>(() => new RevivalState(new[] { new DistrictRevival("a", RevivalLevel.Grey, -1) }));
            Assert.Throws<ArgumentException>(() => new RevivalState(new[] { new DistrictRevival("a", RevivalLevel.Grey, 0), new DistrictRevival("a", RevivalLevel.Grey, 0) }));
            Assert.Throws<ArgumentException>(() => new RevivalState(new[] { new DistrictRevival("a", (RevivalLevel)4, 0) }));
            Assert.Throws<ArgumentException>(() => RevivalEngine.EnterStage(new RevivalState(new[] { new DistrictRevival("a", RevivalLevel.Grey, 0) }), R.Spec(), 2));
        }
    }
}
