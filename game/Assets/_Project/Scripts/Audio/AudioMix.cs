namespace Worktown.Game.Audio
{
    /// <summary>
    /// Spec 048: the player's volume per <see cref="SoundCategory"/> (0..1, from the settings; SoundCatalog.GainOf multiplies it in).
    /// The master volume is the AudioListener's. Client display only, like FeelSettings: set by Settings.GameSettings.Use.
    /// </summary>
    public static class AudioMix
    {
        private static float _effects = 1f;
        private static float _ambience = 1f;
        private static float _ui = 1f;

        /// <summary>Sets the three category volumes (each clamped to 0..1; NaN is 1).</summary>
        public static void Set(float effects, float ambience, float ui)
        {
            _effects = Clamp(effects);
            _ambience = Clamp(ambience);
            _ui = Clamp(ui);
        }

        public static float Of(SoundCategory category)
        {
            switch (category)
            {
                case SoundCategory.Ambience: return _ambience;
                case SoundCategory.Ui: return _ui;
                default: return _effects;
            }
        }

        private static float Clamp(float v) => float.IsNaN(v) ? 1f : v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
