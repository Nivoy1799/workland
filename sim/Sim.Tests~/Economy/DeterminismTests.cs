using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Worktown.Sim.Economy;
using Xunit;

namespace Worktown.Sim.Tests.Economy
{
    public class DeterminismTests
    {
        private static readonly EconomyEngine E = TestEconomy.Engine;

        /// <summary>Scripted run: two players buy, sell fries, sell goods, discard, while the town ticks.</summary>
        private static EconomyState Script(ulong seed)
        {
            var s = E.Initial();
            s = TestEconomy.Open(E, s, "alice", 5000);
            s = TestEconomy.Open(E, s, "bob", 5000);
            long id = 1;
            for (int t = 0; t < 100; t++)
            {
                s = E.Tick(s, new TickInput(EconomyRng.ForTick(seed, s.Tick), 120));
                s = E.TryBuyBase(s, "alice", "potato", 1).State;
                s = E.TrySellItem(s, "alice", id++, TestEconomy.PlatedFries()).State;
                s = E.TrySellItem(s, "bob", id++, TestEconomy.PlatedTomatoCheese()).State;
                if (t % 5 == 0)
                {
                    s = E.Discard(s, "bob", id++, TestEconomy.PlatedRaw()).State;
                    s = E.TrySellGoods(s, "bob", "food_scraps", 1).State;
                }
                if (t % 7 == 0) s = E.TryDebit(s, "alice", 10).State;
            }
            return s;
        }

        [Fact]
        public void SameSeedAndInputsGiveTheSameDigestAfter100Ticks() // AC7
        {
            var a = Script(42);
            var b = Script(42);
            Assert.Equal(100, a.Tick);
            Assert.Equal(EconomySnapshot.Digest(a), EconomySnapshot.Digest(b));
            Assert.Equal(EconomySnapshot.Canonical(a), EconomySnapshot.Canonical(b));
            Assert.True(a.Totals.PaidToPlayers > 0);
        }

        [Fact]
        public void DifferentSeedGivesADifferentDigest() // AC7
        {
            Assert.NotEqual(EconomySnapshot.Digest(Script(42)), EconomySnapshot.Digest(Script(43)));
        }

        [Fact]
        public void ForTickIsReproducibleAndVariesWithSeedAndTick()
        {
            int[] Draw(ulong seed, long tick)
            {
                var r = EconomyRng.ForTick(seed, tick);
                return Enumerable.Range(0, 6).Select(_ => r.NextInt(0, 1_000_000)).ToArray();
            }
            Assert.Equal(Draw(42, 7), Draw(42, 7));
            Assert.NotEqual(Draw(42, 7), Draw(42, 8));
            Assert.NotEqual(Draw(42, 7), Draw(43, 7));
        }

        [Fact]
        public void RestoringNeedsNoRngState()
        {
            // running 10 ticks in one go equals stopping at 5 and continuing from the same state value
            var straight = TestEconomy.Advance(E, E.Initial(), 10);
            var half = TestEconomy.Advance(E, E.Initial(), 5);
            var resumed = TestEconomy.Advance(E, half, 5);
            Assert.Equal(EconomySnapshot.Digest(straight), EconomySnapshot.Digest(resumed));
        }

        [Fact]
        public void CanonicalInitialStateMatchesTheHandWrittenForm()
        {
            // 5 markets sorted by id; each: price then 11 zero counters (7 own fields + 5 Last fields - price)
            string Zeros = ",0,0,0,0,0,0,0,0,0,0,0";
            string expected = "v1|tick=0|markets=5"
                + "|m:drink,250" + Zeros.Substring(0)
                + "|m:food_scraps,3" + Zeros
                + "|m:meal,1200" + Zeros
                + "|m:snack,400" + Zeros
                + "|m:used_cooking_oil,4" + Zeros
                + "|accounts=0|consumed=0|totals,0,0,0,0";
            Assert.Equal(expected, EconomySnapshot.Canonical(E.Initial()));
        }

        [Fact]
        public void CanonicalCoversAccountsGoodsAndConsumedInstances()
        {
            var s = TestEconomy.Open(E, E.Initial(), "bob", 7);
            s = TestEconomy.Open(E, s, "alice", 5);
            s = E.AddGoods(s, "alice", new[] { new GoodAmount("food_scraps", 2) }).State;
            s = E.Discard(s, "alice", 11, TestEconomy.PlatedRaw()).State; // +1 scraps, consumed (11, tick 0)
            string c = EconomySnapshot.Canonical(s);
            Assert.Contains("|accounts=2|a:alice,5,g:food_scraps=3|a:bob,7|", c);
            Assert.Contains("|consumed=1|c:11,0|", c);
        }

        [Fact]
        public void CanonicalIsIndependentOfListOrder()
        {
            var s = TestEconomy.Open(E, TestEconomy.Open(E, E.Initial(), "alice", 1), "bob", 2);
            var shuffled = s with
            {
                Accounts = s.Accounts.Reverse().ToList().AsReadOnly(),
                Markets = s.Markets.Reverse().ToList().AsReadOnly(),
            };
            Assert.Equal(EconomySnapshot.Canonical(s), EconomySnapshot.Canonical(shuffled));
        }

        [Fact]
        public void DigestIsLowercaseHexSha256OfCanonical()
        {
            var s = TestEconomy.Advance(E, E.Initial(), 3);
            string digest = EconomySnapshot.Digest(s);
            Assert.Equal(64, digest.Length);
            Assert.Matches("^[0-9a-f]{64}$", digest);
            using var sha = SHA256.Create();
            string expected = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(EconomySnapshot.Canonical(s))).Select(b => b.ToString("x2")));
            Assert.Equal(expected, digest);
            Assert.NotEqual(digest, EconomySnapshot.Digest(TestEconomy.Advance(E, s, 1)));
        }

        [Fact]
        public void SnapshotRejectsNull()
        {
            Assert.Throws<System.ArgumentNullException>(() => EconomySnapshot.Canonical(null!));
            Assert.Throws<System.ArgumentNullException>(() => EconomySnapshot.Digest(null!));
        }
    }
}
