namespace Worktown.Game.Audio
{
    /// <summary>Spec 023: the kitchen sounds. Every key has a procedurally generated clip in <see cref="SoundCatalog"/>.
    /// Spec 027b: pick, the place tock per material, nope and the button click (appended, so earlier values stay).</summary>
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
        Button
    }
}
