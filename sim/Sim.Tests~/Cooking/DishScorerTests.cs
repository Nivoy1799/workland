using System;
using System.Collections.Generic;
using Worktown.Sim.Cooking;
using Xunit;

namespace Worktown.Sim.Tests.Cooking
{
    public class DishScorerTests
    {
        private static Ingredient I(string id, float sweet, float salty, float sour, float bitter, float umami, float fat, float crunch = 0f, float heat = 0.5f)
            => new Ingredient(id, id, new IngredientStats(sweet, salty, sour, bitter, umami, fat, crunch, heat));

        private static readonly Ingredient Salt = I("salt", 0, 1, 0, 0, 0, 0);
        private static readonly Ingredient Tomato = I("tomato", 0.4f, 0.1f, 0.5f, 0, 0.6f, 0.1f);
        private static readonly Ingredient Cheese = I("cheese", 0.1f, 0.5f, 0.2f, 0.1f, 0.7f, 0.8f);
        private static readonly Ingredient Basil = I("basil", 0.2f, 0, 0.1f, 0.4f, 0.2f, 0.1f, 0.3f);
        private static readonly Ingredient Oil = I("oil", 0, 0, 0, 0.1f, 0, 1f);
        private static readonly Ingredient Onion = I("onion", 0.5f, 0, 0.2f, 0.3f, 0.3f, 0, 0.6f);

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
