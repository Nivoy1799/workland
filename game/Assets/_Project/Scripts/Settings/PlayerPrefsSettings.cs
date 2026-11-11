using UnityEngine;

namespace Worktown.Game.Settings
{
    /// <summary>Spec 048: <see cref="ISettingsPrefs"/> on Unity's PlayerPrefs (per Windows user, on this PC; the tutorial flag's pattern).</summary>
    public sealed class PlayerPrefsSettings : ISettingsPrefs
    {
        public string GetString(string key, string fallback)
        {
            try { return PlayerPrefs.GetString(key, fallback); }
            catch (PlayerPrefsException) { return fallback; }
        }

        public bool SetString(string key, string value)
        {
            try
            {
                PlayerPrefs.SetString(key, value);
                PlayerPrefs.Save();
                return true;
            }
            catch (PlayerPrefsException e)
            {
                Debug.LogWarning("[Settings] could not store the settings: " + e.Message);
                return false;
            }
        }
    }
}
