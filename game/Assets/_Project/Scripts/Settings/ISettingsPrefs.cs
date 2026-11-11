namespace Worktown.Game.Settings
{
    /// <summary>Spec 048: where the settings text lives (PlayerPrefs in the game, a dictionary in the tests).</summary>
    public interface ISettingsPrefs
    {
        /// <summary>The stored text, or <paramref name="fallback"/> when there is none or it cannot be read.</summary>
        string GetString(string key, string fallback);

        /// <summary>Stores the text and writes it out; false when that failed.</summary>
        bool SetString(string key, string value);
    }
}
