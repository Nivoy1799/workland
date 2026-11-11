using System;

namespace Worktown.Game.Settings
{
    /// <summary>
    /// Spec 048: loads and saves the player's <see cref="GameSettings"/> as one JSON text under <see cref="SettingsRules.PrefsKey"/>.
    /// Not <see cref="Persistent"/> (a scripted dev run, SettingsRules.UsesSavedSettings): it loads the defaults and never writes, so a
    /// tour can change settings live without touching the player's saved ones.
    /// </summary>
    public sealed class SettingsStore
    {
        private readonly ISettingsPrefs _prefs;

        public SettingsStore(ISettingsPrefs prefs, bool persistent)
        {
            _prefs = prefs ?? throw new ArgumentNullException(nameof(prefs));
            Persistent = persistent;
        }

        /// <summary>Reads and writes the saved settings (false: a scripted run, defaults in memory only).</summary>
        public bool Persistent { get; }

        /// <summary>The saved settings, or the defaults when there are none, they are broken, or the store is not persistent.</summary>
        public GameSettings Load()
        {
            if (!Persistent) return GameSettings.Defaults();
            return GameSettings.FromJson(_prefs.GetString(SettingsRules.PrefsKey, string.Empty)) ?? GameSettings.Defaults();
        }

        /// <summary>Saves a sanitized copy; false when not persistent or the write failed.</summary>
        public bool Save(GameSettings settings)
        {
            if (!Persistent) return false;
            return _prefs.SetString(SettingsRules.PrefsKey, GameSettings.Sanitized(settings).ToJson());
        }
    }
}
