namespace Worktown.Game.Cooking
{
    /// <summary>Turns a sim quality (0..1) into 1..5 stars at 0.2 steps.</summary>
    public static class StarRating
    {
        public const int MaxStars = 5;

        public static int Stars(float quality)
        {
            if (float.IsNaN(quality) || quality <= 0f) return 1;
            if (quality >= 1f) return MaxStars;
            int stars = 1 + (int)(quality * MaxStars);
            return stars > MaxStars ? MaxStars : stars;
        }

        /// <summary>"★★★★☆" for 4 stars.</summary>
        public static string Format(int stars)
        {
            if (stars < 0) stars = 0;
            if (stars > MaxStars) stars = MaxStars;
            return new string('★', stars) + new string('☆', MaxStars - stars);
        }
    }
}
