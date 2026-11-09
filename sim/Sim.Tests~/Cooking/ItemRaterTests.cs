using System.Collections.Generic;
using Worktown.Sim.Cooking;
using Xunit;
using static Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Cooking
{
    public class ItemRaterTests
    {
        [Fact]
        public void Mix_RatingEqualAcrossAllPermutations()
        {
            var set = new[] { Base("tomato"), Base("cashew"), Base("basil"), Base("oil"), Base("onion") };
            var expected = ItemRater.Rate(Ok(Mix(set)));
            int count = 0;
            foreach (var p in Permutations(set))
            {
                Assert.Equal(expected, ItemRater.Rate(Ok(Mix(p))));
                count++;
            }
            Assert.Equal(120, count);
        }

        [Fact]
        public void Balance_EvenerTasteMixScoresHigher()
        {
            // Identical Umami (0.4) and Fat (0.4); only the other tastes differ.
            var even = Def("even", 0.4f, 0.4f, 0.4f, 0.4f, 0.4f, 0.4f);
            var skewed = Def("skewed", 1f, 0f, 0f, 0f, 0.4f, 0.4f);
            Assert.True(Rate(even).Quality > Rate(skewed).Quality);
        }

        [Fact]
        public void ModerateUmamiAndFat_ScoreHigherThanExtremes()
        {
            float Q(float umami, float fat) => Rate(Def("x", 0.3f, 0.3f, 0.3f, 0.3f, umami, fat)).Quality;
            Assert.True(Q(0.3f, 0.4f) > Q(0.3f, 0f));
            Assert.True(Q(0.3f, 0.4f) > Q(0.3f, 1f));
            var mid = Rate(Def("m", 0.4f, 0.4f, 0.4f, 0.4f, 0.4f, 0.4f)).Quality;
            var lo = Rate(Def("l", 0.4f, 0.4f, 0.4f, 0.4f, 0f, 0.4f)).Quality;
            var hi = Rate(Def("h", 0.4f, 0.4f, 0.4f, 0.4f, 1f, 0.4f)).Quality;
            Assert.True(mid > lo);
            Assert.True(mid > hi);
        }

        [Fact]
        public void SingleExtremeIngredient_ScoresLowerThanBalancedMix()
        {
            var extreme = ItemRater.Rate(Base("salt"));
            var balanced = ItemRater.Rate(Ok(Mix(Base("tomato"), Base("cashew"), Base("basil"))));
            Assert.True(extreme.Quality < balanced.Quality);
        }

        [Fact]
        public void Quality_IsWithinZeroAndOne()
        {
            var sets = new[]
            {
                new[] { Base("salt") }, new[] { Base("oil") },
                new[] { Base("tomato"), Base("cashew"), Base("onion"), Base("oil"), Base("basil") },
            };
            foreach (var set in sets)
            {
                var item = set.Length == 1 ? set[0] : Ok(Mix(set));
                Assert.InRange(ItemRater.Rate(item).Quality, 0f, 1f);
            }
        }

        [Fact]
        public void Profile_IsAverageOfStats()
        {
            var s = Ok(Mix(Base("salt"), Base("oil"))).Stats;
            Assert.Equal(0.5f, s.Salty, 5);
            Assert.Equal(0.5f, s.Fat, 5);
            Assert.Equal(0.05f, s.Bitter, 5);
        }

        [Fact]
        public void InsideWindow_QualityEqualsTaste()
        {
            var r = ItemRater.Rate(Base("salt")); // window [0,1], doneness 0
            Assert.Equal(1f, r.DonenessFactor, 5);
            Assert.Equal(r.Taste, r.Quality, 5);
        }

        [Fact]
        public void FixtureOrdering_RawPotatoBelowAppleFriesAndBurntBelowFries()
        {
            float rawPotato = ItemRater.Rate(Base("potato")).Quality;
            float apple = ItemRater.Rate(Base("apple")).Quality;
            float fries = ItemRater.Rate(Fries()).Quality;
            float burnt = ItemRater.Rate(Ok(Fry(Ok(Cut(Base("potato"), "sticks")), 190f, 400f))).Quality;
            Assert.True(rawPotato < apple);
            Assert.True(rawPotato < fries);
            Assert.True(burnt < fries);
        }

        [Fact]
        public void Plate_IsWeightedMeanOfInputRatings()
        {
            var fries = Fries();
            var apple = Base("apple");
            var plate = Ok(Plate(fries, apple));
            var rf = ItemRater.Rate(fries);
            var ra = ItemRater.Rate(apple);
            var rp = ItemRater.Rate(plate);
            Assert.Equal((rf.Quality + ra.Quality) / 2f, rp.Quality, 5);
            Assert.Equal((rf.DonenessFactor + ra.DonenessFactor) / 2f, rp.DonenessFactor, 5);
        }

        [Fact]
        public void MoreDistanceFromWindow_NeverRaisesQuality()
        {
            var cut = Ok(Cut(Base("potato"), "sticks"));
            float prev = float.MaxValue;
            foreach (float time in new[] { 150f, 250f, 350f, 450f, 600f })
            {
                float q = ItemRater.Rate(Ok(Fry(cut, 190f, time))).Quality;
                Assert.True(q <= prev + 1e-6f);
                prev = q;
            }
        }
    }
}
