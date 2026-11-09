namespace Worktown.Sim.Economy
{
    /// <summary>Spec 032: a guest's review: 1-5 stars and what it says (<see cref="ReviewKey"/>).</summary>
    public sealed record GuestReview(int Stars, ReviewKey Key)
    {
        /// <summary>The template line for the key.</summary>
        public string Text => ReviewRules.TemplateText(Key);

        /// <summary>One byte for the network: the stars in the low 3 bits, the key above (0 = no review).</summary>
        public byte Packed => (byte)((Stars & 0x7) | ((int)Key << 3));

        /// <summary>The review of a packed byte, or null for 0 or a byte out of range.</summary>
        public static GuestReview? Unpack(byte packed)
        {
            int stars = packed & 0x7;
            int key = packed >> 3;
            if (stars < 1 || stars > 5 || key > (int)ReviewKey.WrongChange) return null;
            return new GuestReview(stars, (ReviewKey)key);
        }
    }
}
