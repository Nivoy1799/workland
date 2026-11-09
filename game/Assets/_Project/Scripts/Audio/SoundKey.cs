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
        Coin,
        /// <summary>Spec 029: the fryer's deep oil bubbling (loop), a basket's shake rattle and its drain drips.</summary>
        Fryer,
        Rattle,
        Drip,
        /// <summary>Spec 030: the flip on the griddle, a short "tssh" (a burst of sizzle as the raw side hits the plate).</summary>
        Flip,
        /// <summary>Spec 031: a layer lands on the build (a soft "plop"), the sauce squeeze (loop while held), the fold of a wrap and the
        /// crinkle of wrap paper, a box or a bag.</summary>
        Plop,
        Squeeze,
        Fold,
        Paper,
        /// <summary>Spec 032: a coin or note into the register's change tray.</summary>
        Clink,
        /// <summary>Spec 032: the card terminal.</summary>
        Beep,
        /// <summary>Spec 033: a tap pouring into a cup (loop while held), the lid clicking on, a spill splashing on the counter.</summary>
        Pour,
        LidClick,
        Splash
    }
}
