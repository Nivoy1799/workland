using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Worktown.Sim.Cooking;
using Xunit;
using static Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Cooking
{
    public class ItemIdentityTests
    {
        private static void AssertSameItem(Item expected, Item actual)
        {
            Assert.Equal(expected.Hash, actual.Hash);
            Assert.Equal(expected.CanonicalTree, actual.CanonicalTree);
            Assert.Equal(expected.Stats, actual.Stats);
            Assert.Equal(expected.Surface, actual.Surface);
            Assert.True(System.Linq.Enumerable.SequenceEqual(expected.Components, actual.Components));
            Assert.Equal(ItemRater.Rate(expected), ItemRater.Rate(actual));
        }

        private static string Sha256Hex(string s)
        {
            using var sha = SHA256.Create();
            var sb = new StringBuilder();
            foreach (byte b in sha.ComputeHash(Encoding.UTF8.GetBytes(s))) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        [Fact]
        public void AC2_HashDiffersAcrossProcessing()
        {
            var potato = Base("potato");
            var sticks = Ok(Cut(potato, "sticks"));
            Assert.NotEqual(potato.Hash, Fries().Hash);
            Assert.NotEqual(potato.Hash, sticks.Hash);
            Assert.Equal(64, potato.Hash.Length);
        }

        [Fact]
        public void AC3_MixIdenticalAcrossAll120Permutations()
        {
            var set = new[] { Base("tomato"), Base("cashew"), Base("basil"), Base("oil"), Base("onion") };
            var expected = Ok(Mix(set));
            int count = 0;
            foreach (var p in Permutations(set))
            {
                AssertSameItem(expected, Ok(Mix(p)));
                count++;
            }
            Assert.Equal(120, count);
        }

        [Fact]
        public void MixHash_EqualAcrossAllOrderingsOfThree()
        {
            var set = new[] { Base("tomato"), Base("cashew"), Base("basil") };
            var expected = Ok(Mix(set)).Hash;
            int count = 0;
            foreach (var p in Permutations(set))
            {
                Assert.Equal(expected, Ok(Mix(p)).Hash);
                count++;
            }
            Assert.Equal(6, count);
        }

        [Fact]
        public void AC3_MixIsFlattened()
        {
            var a = Base("tomato"); var b = Base("cashew"); var c = Base("basil");
            AssertSameItem(Ok(Mix(a, b, c)), Ok(Mix(Ok(Mix(a, b)), c)));
            AssertSameItem(Ok(Mix(a, b, c)), Ok(Mix(c, Ok(Mix(b, a)))));
        }

        [Fact]
        public void MixHash_DiffersForDifferentSetsAndDuplicates()
        {
            var tomato = Base("tomato");
            var a = Ok(Mix(tomato, Base("cashew")));
            var b = Ok(Mix(tomato, Base("basil")));
            var c = Ok(Mix(tomato, tomato));
            var d = Ok(Mix(tomato, tomato, tomato));
            Assert.NotEqual(a.Hash, b.Hash);
            Assert.NotEqual(c.Hash, d.Hash);
            Assert.Equal(2, c.Weight);
        }

        [Fact]
        public void AC4_OrderOfChainMatters()
        {
            var potato = Base("potato");
            var friedThenCut = Ok(Cut(Ok(Fry(potato, 190f, 150f)), "sticks"));
            var cutThenFried = Ok(Fry(Ok(Cut(potato, "sticks")), 190f, 150f));
            Assert.NotEqual(friedThenCut.Hash, cutThenFried.Hash);
        }

        [Fact]
        public void AC5_SameBucketSameHashDifferentQuality()
        {
            var sticks = Ok(Cut(Base("potato"), "sticks"));
            var a = Ok(Fry(sticks, 190f, 100f));
            var b = Ok(Fry(sticks, 190f, 170f));
            Assert.Equal(a.Hash, b.Hash);
            Assert.Equal(ItemNaming.FallbackName(a, NameCatalog.Empty), ItemNaming.FallbackName(b, NameCatalog.Empty));
            Assert.NotEqual(ItemRater.Rate(a).Quality, ItemRater.Rate(b).Quality);
        }

        [Fact]
        public void AC7_RedundantCutReturnsInput()
        {
            var sticks = Ok(Cut(Base("potato"), "sticks"));
            Assert.Equal(sticks.Hash, Ok(Cut(sticks, "sticks")).Hash);
            var diced = Ok(Cut(Base("potato"), "dice"));
            Assert.Equal(diced.Hash, Ok(Cut(diced, "halves")).Hash);
        }

        [Fact]
        public void AC9_PinnedVectors()
        {
            var potato = Base("potato");
            // printf '%s' 'v1|b|potato' | shasum -a 256
            Assert.Equal("2a4e461a0b881d570bbee19bc9318cf890af884214314e73116f237a26d9abc6", potato.Hash);
            var sticks = Ok(Cut(potato, "sticks"));
            Assert.Equal(Sha256Hex("v1|o|cut|shape=sticks|" + potato.Hash), sticks.Hash);
            Assert.Equal("fry(temp=hot,time=medium;cut(shape=sticks;potato))", Fries().CanonicalTree);
        }

        [Fact]
        public void BaseItem_Properties()
        {
            var potato = Base("potato");
            Assert.Equal("potato", potato.BaseId);
            Assert.Null(potato.OperationId);
            Assert.Equal(1, potato.Depth);
            Assert.Equal(1, potato.Weight);
            Assert.Empty(potato.Inputs);
        }

        [Fact]
        public void OperationItem_Properties()
        {
            var fries = Fries();
            Assert.Equal("fry", fries.OperationId);
            Assert.Null(fries.BaseId);
            Assert.Equal(3, fries.Depth);
            Assert.Equal("hot", fries.Buckets["temp"]);
            Assert.Equal("medium", fries.Buckets["time"]);
            Assert.Equal("potato", fries.Components[0].BaseId);
        }
    }
}
