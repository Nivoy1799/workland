using System;

namespace Worktown.Game.UI
{
    /// <summary>One "What's new" entry (spec 011). Plain fields for JsonUtility.</summary>
    [Serializable]
    public sealed class WhatsNewEntry
    {
        /// <summary>Unique, e.g. "2026-11-07-spec-010". Stored after the player closes the banner.</summary>
        public string id = string.Empty;
        /// <summary>ISO date "yyyy-MM-dd".</summary>
        public string date = string.Empty;
        public string title = string.Empty;
        public string[] items = Array.Empty<string>();
        public string[] test = Array.Empty<string>();
    }
}
