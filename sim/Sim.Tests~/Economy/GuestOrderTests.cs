using System.Linq;
using Worktown.Sim.Cooking;
using Worktown.Sim.Core;
using Worktown.Sim.Economy;
using Xunit;

namespace Worktown.Sim.Tests.Economy
{
    /// <summary>
    /// Spec 032 AC2: a guest's order from the seed (modifiers, patience base, card or cash, the note), the patience, the tip, the
    /// cash rules and the review score; every one deterministic.
    /// </summary>
    public class GuestOrderTests
    {
        [Fact]
        public void GuestOrders_AreTheSameForTheSameSeed()
        {
            var a = new SeededRandom(32);
            var b = new SeededRandom(32);
            int cards = 0;
            for (int i = 0; i < 200; i++)
            {
                RecipeTemplate? t = i % 3 == 0 ? null : RecipeTemplates.Default[i % RecipeTemplates.Default.Count];
                GuestOrder x = GuestOrders.Draw(a, t);
                GuestOrder y = GuestOrders.Draw(b, t);
                Assert.Equal(x.Modifiers, y.Modifiers);
                Assert.Equal(x.PatienceBaseSeconds, y.PatienceBaseSeconds);
                Assert.Equal(x.PaysByCard, y.PaysByCard);
                Assert.Equal(x.Note, y.Note);
                Assert.InRange(x.PatienceBaseSeconds, 54, 66);
                Assert.Contains(x.Note, CashRules.GuestNotes);
                if (t == null) Assert.Empty(x.Modifiers);
                if (x.PaysByCard) cards++;
            }
            Assert.InRange(cards, 70, 130); // about half by card
        }

        [Fact]
        public void Patience_Is60sPlus15sPerLayer_AndDrains()
        {
            Assert.Equal(120, GuestPatience.Seconds(60, 4));
            Assert.Equal(75, GuestPatience.Seconds(60, 0)); // at least one layer
            Assert.Equal(1f, GuestPatience.ShareLeft(120f, 0f));
            Assert.Equal(0.5f, GuestPatience.ShareLeft(120f, 60f), 4);
            Assert.Equal(0f, GuestPatience.ShareLeft(120f, 200f));
            Assert.False(GuestPatience.RunsOut(120f, 119.9f));
            Assert.True(GuestPatience.RunsOut(120f, 120f));
            Assert.Equal(255, GuestPatience.ToByte(1f));
            Assert.Equal(0, GuestPatience.ToByte(0f));
            Assert.Equal(128, GuestPatience.ToByte(0.5f));
            Assert.True(GuestPatience.IsLow(0.25f));
            Assert.False(GuestPatience.IsLow(0.3f));
        }

        [Fact]
        public void TheTip_IsAtMost30Percent_AndFallsWithTimeMistakesAndQuality()
        {
            Assert.Equal(30, TipRules.Tip(100, 0, 1200, 0, 5)); // fast, exact, 5 stars: the cap
            Assert.Equal(30, TipRules.Tip(100, 600, 1200, 0, 5)); // half the patience: still full speed
            Assert.Equal(15, TipRules.Tip(100, 900, 1200, 0, 5)); // three quarters: half the speed
            Assert.Equal(0, TipRules.Tip(100, 1200, 1200, 0, 5));
            Assert.Equal(15, TipRules.Tip(100, 0, 1200, 1, 5)); // one modifier mistake
            Assert.Equal(0, TipRules.Tip(100, 0, 1200, 2, 5));
            Assert.Equal(15, TipRules.Tip(100, 0, 1200, 0, 3));
            Assert.Equal(0, TipRules.Tip(100, 0, 1200, 0, 1));
            Assert.Equal(0, TipRules.Tip(0, 0, 1200, 0, 5));
            for (long price = 1; price < 2000; price += 37)
                for (int stars = 1; stars <= 5; stars++)
                    Assert.True(TipRules.Tip(price, 0, 100, 0, stars) <= price * TipRules.MaxTipPercent / 100);
        }

        [Fact]
        public void Cash_RoundsUpToTheGuestsNote_AndWrongChangeCostsTheDifference()
        {
            Assert.Equal(400, CashRules.Handed(382, 50));
            Assert.Equal(390, CashRules.Handed(382, 10));
            Assert.Equal(400, CashRules.Handed(400, 20)); // exact: no change
            Assert.Equal(18, CashRules.ChangeDue(400, 382));
            Assert.Equal(new[] { 10, 5, 2, 1 }, CashRules.Coins(18));
            Assert.Equal(new[] { 1, 2, 5, 10, 20, 50 }, CashRules.Tray);
            Assert.Equal(382, CashRules.CashPayout(382, 18, 18));
            Assert.Equal(377, CashRules.CashPayout(382, 18, 23)); // five too many: lost
            Assert.Equal(377, CashRules.CashPayout(382, 18, 13)); // five too few: the guest gets it back, and it costs the same
            Assert.Equal(0, CashRules.CashPayout(5, 0, 50));
        }

        [Fact]
        public void TheReview_StartsAtTheStars_AndTheMoodTakesStarsOff()
        {
            Assert.Equal(new GuestReview(5, ReviewKey.Great), ReviewRules.Review(5, 100, 1200, 0, false));
            Assert.Equal(new GuestReview(4, ReviewKey.Slow), ReviewRules.Review(5, 1000, 1200, 0, false));
            Assert.Equal(new GuestReview(4, ReviewKey.OrderOff), ReviewRules.Review(5, 100, 1200, 1, false));
            Assert.Equal(new GuestReview(4, ReviewKey.WrongChange), ReviewRules.Review(5, 100, 1200, 0, true));
            Assert.Equal(new GuestReview(2, ReviewKey.WrongOrder), ReviewRules.Review(5, 100, 1200, 2, false));
            Assert.Equal(new GuestReview(1, ReviewKey.WrongChange), ReviewRules.Review(1, 1100, 1200, 0, true));
            Assert.Equal(new GuestReview(3, ReviewKey.Fine), ReviewRules.Review(3, 0, 1200, 0, false));
            Assert.Equal("review.wrong_change", ReviewRules.KeyId(ReviewKey.WrongChange));
            foreach (ReviewKey key in System.Enum.GetValues(typeof(ReviewKey)).Cast<ReviewKey>())
            {
                Assert.False(string.IsNullOrEmpty(ReviewRules.TemplateText(key)));
                Assert.True(ReviewRules.TemplateText(key).Length <= 36, key.ToString());
                var r = new GuestReview(3, key);
                Assert.Equal(r, GuestReview.Unpack(r.Packed));
            }
            Assert.Null(GuestReview.Unpack(0));
        }
    }
}
