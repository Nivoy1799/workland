namespace Worktown.Game.Audio
{
    /// <summary>Spec 023: the kitchen sounds. Every key has a procedurally generated clip in <see cref="SoundCatalog"/>.
    /// Spec 027b: pick, the place tock per material, nope and the button click (appended, so earlier values stay).
    /// Spec 027c: the cooking cues (perfect, warning 3 s before overdone, burnt) and the coin of a sale.</summary>
    public enum SoundKey : byte
    {
        Chop,
        Tick,
        Sizzle,
        Bubble,
        Pick,
        PlaceWood,
        PlaceMetal,
        PlaceCeramic,
        PlaceFood,
        Nope,
        Button,
        Perfect,
        Warning,
        Burnt,
        Coin
    }
}
