namespace Worktown.Game.Audio
{
    /// <summary>Spec 048: the volume group a sound belongs to (SoundRules.CategoryOf; one slider each under the master volume).</summary>
    public enum SoundCategory
    {
        /// <summary>What the hands and the kitchen do (chop, sizzle, pick, place, pour...).</summary>
        Effects,
        /// <summary>The steady background hums (the fryer's idle oil, the spit's motor).</summary>
        Ambience,
        /// <summary>The feedback cues (nope, perfect, warning, burnt, the coin of a sale).</summary>
        Ui
    }
}
