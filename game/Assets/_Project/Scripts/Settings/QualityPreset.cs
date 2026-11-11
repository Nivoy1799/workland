namespace Worktown.Game.Settings
{
    /// <summary>Spec 048: the graphics presets (render scale, shadow distance and cascades; SettingsRules). High is the project's
    /// own render pipeline asset as it is. Stored by value, so new presets are appended.</summary>
    public enum QualityPreset
    {
        Low,
        Medium,
        High
    }
}
