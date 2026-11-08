namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 026: one row of <see cref="ArtSizeTable"/>: the real-world size of a key in metres, its class and axis,
    /// and the row of the guide's reference table it comes from (<see cref="GuideRow"/> is null for things the
    /// guide has no row for; <see cref="GuideIndex"/> picks a value of a "4 / 8" row).
    /// </summary>
    public readonly struct ArtSize
    {
        public readonly ArtKey Key;
        public readonly SizeClass Class;
        public readonly float Real;
        public readonly SizeAxis Axis;
        public readonly string GuideRow;
        public readonly int GuideIndex;

        public ArtSize(ArtKey key, SizeClass sizeClass, float real, SizeAxis axis, string guideRow = null, int guideIndex = 0)
        {
            Key = key;
            Class = sizeClass;
            Real = real;
            Axis = axis;
            GuideRow = guideRow;
            GuideIndex = guideIndex;
        }

        /// <summary>The size in the game: <see cref="Real"/> x the class factor.</summary>
        public float Target => Real * ArtSizeTable.Factor(Class);

        public override string ToString() => Key + " " + Class + " " + Real + " m -> " + Target + " m (" + Axis + ")";
    }
}
