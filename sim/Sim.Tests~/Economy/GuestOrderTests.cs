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
            // Spec 037 re-pin (D39, cents): the old $382 bill with $50/$10/$20 notes is now $38.20 with $20/$10 notes; the
            // coins are $0.10 to $2, the notes $5 to $20, so the old 18 dollars of change are 180 cents.
            Assert.Equal(4000, CashRules.Handed(3820, 2000));
            Assert.Equal(3500, CashRules.Handed(3420, 500));
            Assert.Equal(4000, CashRules.Handed(3420, 1000));
            Assert.Equal(4000, CashRules.Handed(4000, 2000)); // exact: no change
            Assert.Equal(180, CashRules.ChangeDue(4000, 3820));
            Assert.Equal(new[] { 100, 50, 20, 10 }, CashRules.Coins(180));
            Assert.Equal(new[] { 10, 20, 50, 100, 200, 500, 1000, 2000 }, CashRules.Tray);
            Assert.Equal(new[] { 500, 1000, 2000 }, CashRules.GuestNotes);
            Assert.Equal(3820, CashRules.CashPayout(3820, 180, 180));
            Assert.Equal(3770, CashRules.CashPayout(3820, 180, 230)); // fifty cents too many: lost
            Assert.Equal(3770, CashRules.CashPayout(3820, 180, 130)); // fifty too few: the guest gets it back, and it costs the same
            Assert.Equal(0, CashRules.CashPayout(500, 0, 5000));
        }

        [Fact]
        public void Cash_AGuestPays950WithATenDollarNote_TheChangeIsOneFiftyCentCoin() // spec 037 AC3
        {
            long due = 950; // $9.50
            long handed = CashRules.Handed(due, 1000);
            Assert.Equal(1000, handed);
            Assert.Equal(50, CashRules.ChangeDue(handed, due));
            Assert.Equal(new[] { 50 }, CashRules.Coins(CashRules.ChangeDue(handed, due)));
            Assert.Equal(new[] { 1000, 500, 200, 50, 20, 20 }, CashRules.Coins(1790)); // $17.90: largest first, coins after notes
            Assert.Equal(2000, CashRules.Handed(1050, 1000)); // $10.50 with $10 notes: two of them
        }

        [Fact]
        public void Cash_EveryAmountAGuestOwes_IsWholeCoins_SoTheChangeIsAlwaysExact() // spec 037: exact change in cents
        {
            Assert.Equal(0, CashRules.ToCoins(-5));
            Assert.Equal(0, CashRules.ToCoins(9));
            Assert.Equal(10, CashRules.ToCoins(19));
            Assert.Equal(950, CashRules.ToCoins(950));
            Assert.Equal(1230, CashRules.ToCoins(1237));
            foreach (int note in CashRules.GuestNotes)
                for (long due = CashRules.CoinUnit; due <= 6000; due += CashRules.CoinUnit)
                {
                    long change = CashRules.ChangeDue(CashRules.Handed(due, note), due);
                    Assert.True(change < note, "the change is below one note");
                    Assert.Equal(change, CashRules.Coins(change).Sum(c => (long)c));
                    Assert.True(change <= CashRules.MaxTray);
                }
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
