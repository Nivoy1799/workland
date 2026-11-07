namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 017: which catalog model draws a food, how many copies make the pile, and whether the model
    /// is tinted with the ingredient colour (generic pieces and stand-ins) or keeps its own colours.
    /// </summary>
    public readonly struct FoodPiece
    {
        public FoodPiece(ArtKey key, int count, bool tinted)
        {
            Key = key;
            Count = count;
            Tinted = tinted;
        }

        public ArtKey Key { get; }
        public int Count { get; }
        public bool Tinted { get; }
    }
}
