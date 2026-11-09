using System.Collections.Generic;
using Worktown.Sim.Core;
using Worktown.Sim.Economy;
using Xunit;

namespace Worktown.Sim.Tests.Economy
{
    /// <summary>
    /// Spec 035 AC2: the hygiene model: its sources (dirty dishes after the grace, a full trash, spills, burning), the decay and the
    /// recovery per game minute, the effects on reviews and patience, the eat-in draw, and determinism.
    /// </summary>
    public class HygieneTests
    {
        [Fact]
        public void ANewShop_StartsAt100_AndAllCleanStaysThere()
        {
            HygieneMeter m = HygieneMeter.Start(480);
            Assert.Equal(100, m.Percent);
            Assert.Equal(100, m.Advance(500, HygieneSources.Clean).Percent);
        }

        [Fact]
        public void ADirtyDish_CountsOnlyAfterTwoGameMinutes_ThenCostsOnePercentAMinute()
        {
            var since = new List<long> { 100 };
            Assert.Equal(0, HygieneRules.OverdueDishes(since, 101));
            Assert.Equal(0, HygieneRules.OverdueDishes(since, 102)); // exactly two minutes: still within the grace
            Assert.Equal(1, HygieneRules.OverdueDishes(since, 103));
            // A fresh dish costs nothing yet, but stops the recovery (nothing dirty at all is needed for it).
            var fresh = new HygieneSources(1, 0, false, 0, false);
            Assert.Equal(90, HygieneRules.Step(90, fresh));
            Assert.Equal(89, HygieneRules.Step(90, new HygieneSources(1, 1, false, 0, false)));
            Assert.Equal(87, HygieneRules.Step(90, new HygieneSources(3, 3, false, 0, false)));
        }

        [Fact]
        public void AFullTrash_CostsTwo_EverySpillTwo_BurningOne_AndTheyAddUp()
        {
            Assert.Equal(2, HygieneRules.DecayPerMinute(new HygieneSources(0, 0, true, 0, false)));
            Assert.Equal(2, HygieneRules.DecayPerMinute(new HygieneSources(0, 0, false, 1, false)));
            Assert.Equal(4, HygieneRules.DecayPerMinute(new HygieneSources(0, 0, false, 2, false)));
            Assert.Equal(1, HygieneRules.DecayPerMinute(new HygieneSources(0, 0, false, 0, true)));
            Assert.Equal(1 + 2 + 2 + 1, HygieneRules.DecayPerMinute(new HygieneSources(2, 1, true, 1, true)));
            Assert.True(HygieneRules.IsTrashFull(12));
            Assert.False(HygieneRules.IsTrashFull(11));
        }

        [Fact]
        public void Recovery_IsOnePercentAMinute_UpTo100_AndTheScoreNeverLeaves0To100()
        {
            Assert.Equal(51, HygieneRules.Step(50, HygieneSources.Clean));
            Assert.Equal(100, HygieneRules.Step(100, HygieneSources.Clean));
            Assert.Equal(0, HygieneRules.Step(1, new HygieneSources(5, 5, true, 3, true)));
            Assert.Equal(0, HygieneRules.Step(-20, new HygieneSources(0, 0, true, 0, false)));
            Assert.Equal(100, HygieneRules.Step(250, HygieneSources.Clean));
        }

        [Fact]
        public void TheMeter_AppliesEveryWholeMinute_CapsAJump_AndNeverRunsBackwards()
        {
            var spill = new HygieneSources(0, 0, false, 1, false);
            HygieneMeter m = HygieneMeter.Start(0).Advance(5, spill);
            Assert.Equal(90, m.Percent);
            Assert.Equal(5, m.Minute);
            Assert.Same(m, m.Advance(5, spill)); // the same minute: nothing
            HygieneMeter back = m.Advance(3, spill);
            Assert.Equal(90, back.Percent); // a clock that went back changes no score
            Assert.Equal(3, back.Minute);
            // The night skipped after a shift (18 hours) counts at most ten minutes.
            Assert.Equal(90 - 2 * HygieneRules.MaxCatchUpMinutes, m.Advance(5 + 18 * 60, spill).Percent);
        }

        [Fact]
        public void Reviews_LoseAStarBelow50Percent_WithTheDirtyKey()
        {
            Assert.Equal(0, HygieneRules.ReviewPenalty(50));
            Assert.Equal(1, HygieneRules.ReviewPenalty(49));
            GuestReview clean = ReviewRules.Review(5, 0, 600, 0, false, 50);
            GuestReview dirty = ReviewRules.Review(5, 0, 600, 0, false, 49);
            Assert.Equal(5, clean.Stars);
            Assert.Equal(ReviewKey.Great, clean.Key);
            Assert.Equal(4, dirty.Stars);
            Assert.Equal(ReviewKey.Dirty, dirty.Key);
            Assert.Equal("review.dirty", ReviewRules.KeyId(ReviewKey.Dirty));
            Assert.False(string.IsNullOrEmpty(ReviewRules.TemplateText(ReviewKey.Dirty)));
            // A worse complaint keeps its key; the stars still lose one; never below one star.
            GuestReview slowDirty = ReviewRules.Review(5, 500, 600, 0, false, 10);
            Assert.Equal(3, slowDirty.Stars);
            Assert.Equal(ReviewKey.Slow, slowDirty.Key);
            Assert.Equal(1, ReviewRules.Review(1, 0, 600, 0, false, 0).Stars);
            // The old overload is the clean shop.
            Assert.Equal(ReviewRules.Review(4, 0, 600, 1, false, 100), ReviewRules.Review(4, 0, 600, 1, false));
        }

        [Fact]
        public void Patience_IsTwentyPercentShorterBelow30Percent()
        {
            Assert.Equal(1000, HygieneRules.PatiencePermille(30));
            Assert.Equal(800, HygieneRules.PatiencePermille(29));
            Assert.Equal(75f, HygieneRules.Patience(75f, 100), 3);
            Assert.Equal(60f, HygieneRules.Patience(75f, 10), 3);
            Assert.Equal(0f, HygieneRules.Patience(float.NaN, 10));
        }

        [Fact]
        public void EatIn_IsAboutHalf_AndTheSameForTheSameSeed()
        {
            var a = new SeededRandom(35);
            var b = new SeededRandom(35);
            int eatIn = 0;
            for (int i = 0; i < 400; i++)
            {
                bool x = GuestOrders.DrawEatIn(a);
                Assert.Equal(x, GuestOrders.DrawEatIn(b));
                if (x) eatIn++;
            }
            Assert.InRange(eatIn, 160, 240);
        }

        [Fact]
        public void TheSameSourcesAndClock_GiveTheSameScore()
        {
            int Run()
            {
                HygieneMeter m = HygieneMeter.Start(480);
                var dishes = new List<long>();
                for (long minute = 481; minute < 720; minute++)
                {
                    if (minute % 7 == 0) dishes.Add(minute);
                    if (minute % 11 == 0 && dishes.Count > 0) dishes.RemoveAt(0);
                    var s = new HygieneSources(dishes.Count, HygieneRules.OverdueDishes(dishes, minute), minute % 50 < 10,
                        minute % 90 < 5 ? 1 : 0, minute % 60 == 0);
                    m = m.Advance(minute, s);
                }
                return m.Percent;
            }
            Assert.Equal(Run(), Run());
        }
    }
}
