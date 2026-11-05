using System;
using System.Collections.Generic;
using Worktown.Sim.Cooking;
using Xunit;

namespace Worktown.Sim.Tests.Cooking
{
    public class DishScorerTests
    {
        private static Ingredient MakeIngredient(string id, float sweet, float salty, float sour, float bitter, float umami, float fat, float crunch = 0f, float heat = 0.5f)
            => new Ingredient(id, id, new IngredientStats(sweet, salty, sour, bitter, umami, fat, crunch, heat));

        private static readonly Ingredient Salt = MakeIngredient("salt", 0, 1, 0, 0, 0, 0);
        private static readonly Ingredient Tomato = MakeIngredient("tomato", 0.4f, 0.1f, 0.5f, 0, 0.6f, 0.1f);
        private static readonly Ingredient Cheese = MakeIngredient("cheese", 0.1f, 0.5f, 0.2f, 0.1f, 0.7f, 0.8f);
        private static readonly Ingredient Basil = MakeIngredient("basil", 0.2f, 0, 0.1f, 0.4f, 0.2f, 0.1f, 0.3f);
        private static readonly Ingredient Oil = MakeIngredient("oil", 0, 0, 0, 0.1f, 0, 1f);
        private static readonly Ingredient Onion = MakeIngredient("onion", 0.5f, 0, 0.2f, 0.3f, 0.3f, 0, 0.6f);

        private static IEnumerable<T[]> Permutations<T>(T[] items)
        {
            if (items.Length <= 1) { yield return items; yield break; }
            for (int i = 0; i < items.Length; i++)
            {
                var rest = new List<T>(items);
                rest.RemoveAt(i);
                foreach (var p in Permutations(rest.ToArray()))
                {
                    var r = new T[items.Length];
                    r[0] = items[i];
                    p.CopyTo(r, 1);
                    yield return r;
                }
            }
        }

        [Fact]
        public void Score_WholeResultEqualAcrossAllPermutations()
        {
            var set = new[] { Tomato, Cheese, Basil, Oil, Onion };
            var expected = DishScorer.Score(set);
            int count = 0;
            foreach (var p in Permutations(set))
            {
                Assert.Equal(expected, DishScorer.Score(p));
                count++;
            }
            Assert.Equal(120, count);
        }

        [Fact]
        public void ComboHash_EqualAcrossAllOrderingsOfThree()
        {
            var set = new[] { Tomato, Cheese, Basil };
            var expected = DishScorer.Score(set).ComboHash;
            int count = 0;
            foreach (var p in Permutations(set))
            {
                Assert.Equal(expected, DishScorer.Score(p).ComboHash);
                count++;
            }
            Assert.Equal(6, count);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("a\nb")]
        public void InvalidId_Throws(string? id)
        {
            var bad = new Ingredient(id!, "x", new IngredientStats(0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f));
            Assert.Throws<ArgumentException>(() => DishScorer.Score(new[] { bad }));
        }

        [Fact]
        public void NullElement_Throws()
        {
            Assert.Throws<ArgumentException>(() => DishScorer.Score(new Ingredient[] { Tomato, null! }));
        }

        [Fact]
        public void NullStats_Throws()
        {
            var bad = new Ingredient("bad", "bad", null!);
            Assert.Throws<ArgumentException>(() => DishScorer.Score(new[] { bad }));
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        [InlineData(1.5f)]
        [InlineData(-0.1f)]
        public void InvalidStat_Throws(float value)
        {
            var bad = MakeIngredient("bad", 0.1f, 0.1f, 0.1f, 0.1f, value, 0.1f);
            Assert.Throws<ArgumentException>(() => DishScorer.Score(new[] { bad }));
        }

        [Fact]
        public void Balance_EvenerTasteMixScoresHigher()
        {
            // Identical Umami (0.4) and Fat (0.4); only the other tastes differ.
            var even = MakeIngredient("even", 0.4f, 0.4f, 0.4f, 0.4f, 0.4f, 0.4f);
            var skewed = MakeIngredient("skewed", 1f, 0f, 0f, 0f, 0.4f, 0.4f);
            Assert.True(DishScorer.Score(new[] { even }).Quality > DishScorer.Score(new[] { skewed }).Quality);
        }

        [Fact]
        public void ModerateUmamiAndFat_ScoreHigherThanExtremes()
        {
            float Q(float umami, float fat) =>
                DishScorer.Score(new[] { MakeIngredient("x", 0.3f, 0.3f, 0.3f, 0.3f, umami, fat) }).Quality;
            // Umami also feeds the balance term, so vary fat and umami separately with the others fixed.
            Assert.True(Q(0.3f, 0.4f) > Q(0.3f, 0f));
            Assert.True(Q(0.3f, 0.4f) > Q(0.3f, 1f));
            var mid = DishScorer.Score(new[] { MakeIngredient("m", 0.4f, 0.4f, 0.4f, 0.4f, 0.4f, 0.4f) }).Quality;
            var lo = DishScorer.Score(new[] { MakeIngredient("l", 0.4f, 0.4f, 0.4f, 0.4f, 0f, 0.4f) }).Quality;
            var hi = DishScorer.Score(new[] { MakeIngredient("h", 0.4f, 0.4f, 0.4f, 0.4f, 1f, 0.4f) }).Quality;
            Assert.True(mid > lo);
            Assert.True(mid > hi);
        }

        [Fact]
        public void ComboHash_IsOrderIndependent()
        {
            var a = DishScorer.Score(new[] { Tomato, Cheese, Basil });
            var b = DishScorer.Score(new[] { Basil, Tomato, Cheese });
            Assert.Equal(a.ComboHash, b.ComboHash);
            Assert.Equal(64, a.ComboHash.Length);
        }

        [Fact]
        public void ComboHash_DiffersForDifferentSets()
        {
            var a = DishScorer.Score(new[] { Tomato, Cheese });
            var b = DishScorer.Score(new[] { Tomato, Basil });
            var c = DishScorer.Score(new[] { Tomato });
            var d = DishScorer.Score(new[] { Tomato, Tomato });
            Assert.NotEqual(a.ComboHash, b.ComboHash);
            Assert.NotEqual(c.ComboHash, d.ComboHash);
        }

        [Fact]
        public void ComboHash_IsKnownSha256()
        {
            // SHA-256 of "salt" (single id, no separator), pinned for cross-platform stability.
            var r = DishScorer.Score(new[] { Salt });
            Assert.Equal("63479ad69a090b258277ec8fba6f99419a2ffb248981510657c944ccd1148e97", r.ComboHash);
        }

        [Fact]
        public void SingleExtremeIngredient_ScoresLowerThanBalancedMix()
        {
            var extreme = DishScorer.Score(new[] { Salt });
            var balanced = DishScorer.Score(new[] { Tomato, Cheese, Basil });
            Assert.True(extreme.Quality < balanced.Quality);
        }

        [Fact]
        public void Quality_IsWithinZeroAndOne()
        {
            foreach (var set in new[] { new[] { Salt }, new[] { Oil }, new[] { Tomato, Cheese, Onion, Oil, Basil } })
            {
                var q = DishScorer.Score(set).Quality;
                Assert.InRange(q, 0f, 1f);
            }
        }

        [Fact]
        public void Profile_IsAverageOfStats()
        {
            var r = DishScorer.Score(new[] { Salt, Oil });
            Assert.Equal(0.5f, r.Profile.Salty, 5);
            Assert.Equal(0.5f, r.Profile.Fat, 5);
            Assert.Equal(0.05f, r.Profile.Bitter, 5);
        }

        [Fact]
        public void EmptyList_Throws()
        {
            Assert.Throws<ArgumentException>(() => DishScorer.Score(new List<Ingredient>()));
        }

        [Fact]
        public void NullList_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => DishScorer.Score(null!));
        }
    }
}
