using System;

namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 032: the guest's review, deterministic. It starts at the order's stars (1-5) and the guest's mood takes stars off: one
    /// for a wait past <see cref="SlowPermille"/> of the patience, one for one step short of the order, one for wrong
    /// change; the wrong dish (two or more steps short) is at most 2 stars. Clamped to 1-5. The key names the main complaint (wrong
    /// dish, wrong change, order a bit off, slow, in that order), else it follows the stars. The text is a template line per key
    /// (no AI text yet, spec 032 out of scope).
    /// </summary>
    public static class ReviewRules
    {
        public const int MinStars = 1;
        public const int MaxStars = 5;
        /// <summary>A wrong dish gets at most this many stars.</summary>
        public const int WrongDishCap = 2;
        /// <summary>A wait past this share of the patience (permille, integer math) is slow: the review notices.</summary>
        public const int SlowPermille = 750;

        public static GuestReview Review(int orderStars, int waitedTenths, int patienceTenths, int misses, bool wrongChange)
        {
            int stars = Math.Max(MinStars, Math.Min(MaxStars, orderStars));
            bool slow = patienceTenths > 0 && waitedTenths * 1000L > (long)SlowPermille * patienceTenths;
            bool wrongDish = misses >= 2;
            if (slow) stars--;
            if (misses == 1) stars--;
            if (wrongChange) stars--;
            if (wrongDish) stars = Math.Min(stars, WrongDishCap);
            stars = Math.Max(MinStars, Math.Min(MaxStars, stars));

            ReviewKey key;
            if (wrongDish) key = ReviewKey.WrongOrder;
            else if (wrongChange) key = ReviewKey.WrongChange;
            else if (misses == 1) key = ReviewKey.OrderOff;
            else if (slow) key = ReviewKey.Slow;
            else key = ByStars(stars);
            return new GuestReview(stars, key);
        }

        public static ReviewKey ByStars(int stars)
        {
            switch (Math.Max(MinStars, Math.Min(MaxStars, stars)))
            {
                case 5: return ReviewKey.Great;
                case 4: return ReviewKey.Good;
                case 3: return ReviewKey.Fine;
                case 2: return ReviewKey.Meh;
                default: return ReviewKey.Bad;
            }
        }

        /// <summary>The stable text key for the AI gateway later ("review.great").</summary>
        public static string KeyId(ReviewKey key)
        {
            switch (key)
            {
                case ReviewKey.Great: return "review.great";
                case ReviewKey.Good: return "review.good";
                case ReviewKey.Fine: return "review.fine";
                case ReviewKey.Meh: return "review.meh";
                case ReviewKey.Bad: return "review.bad";
                case ReviewKey.Slow: return "review.slow";
                case ReviewKey.OrderOff: return "review.order_off";
                case ReviewKey.WrongOrder: return "review.wrong_order";
                case ReviewKey.WrongChange: return "review.wrong_change";
                default: return "review.unknown";
            }
        }

        /// <summary>The template line of a key (short: it fits a speech bubble and one summary line).</summary>
        public static string TemplateText(ReviewKey key)
        {
            switch (key)
            {
                case ReviewKey.Great: return "Perfect, just how I asked.";
                case ReviewKey.Good: return "Tasty, I'd come back.";
                case ReviewKey.Fine: return "Fine. Nothing special.";
                case ReviewKey.Meh: return "Not great, to be honest.";
                case ReviewKey.Bad: return "I won't order that again.";
                case ReviewKey.Slow: return "Good food, but such a wait.";
                case ReviewKey.OrderOff: return "Nice, but not quite what I asked.";
                case ReviewKey.WrongOrder: return "That's not what I ordered.";
                case ReviewKey.WrongChange: return "They got my change wrong.";
                default: return "...";
            }
        }
    }
}
