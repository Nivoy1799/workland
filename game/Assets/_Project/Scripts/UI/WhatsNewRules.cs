using System;
using System.Collections.Generic;
using UnityEngine;

namespace Worktown.Game.UI
{
    /// <summary>Pure "What's new" logic (spec 011): parse, newest entry, show once per build.</summary>
    public static class WhatsNewRules
    {
        /// <summary>PlayerPrefs key holding the id of the last entry the player closed.</summary>
        public const string SeenKey = "worktown.whatsnew.seen";

        /// <summary>Entries from JSON. Bad or empty JSON gives an empty list, never an exception.</summary>
        public static IReadOnlyList<WhatsNewEntry> Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<WhatsNewEntry>();
            try
            {
                WhatsNewFile file = JsonUtility.FromJson<WhatsNewFile>(json);
                if (file == null || file.entries == null) return Array.Empty<WhatsNewEntry>();
                var valid = new List<WhatsNewEntry>(file.entries.Length);
                foreach (WhatsNewEntry entry in file.entries)
                    if (entry != null && !string.IsNullOrEmpty(entry.id)) valid.Add(entry);
                return valid;
            }
            catch (ArgumentException)
            {
                return Array.Empty<WhatsNewEntry>();
            }
        }

        /// <summary>Newest by ISO date; on equal dates the later entry in the file wins. Null if none.</summary>
        public static WhatsNewEntry Newest(IReadOnlyList<WhatsNewEntry> entries)
        {
            WhatsNewEntry newest = null;
            foreach (WhatsNewEntry entry in entries)
                if (newest == null || string.CompareOrdinal(entry.date, newest.date) >= 0)
                    newest = entry;
            return newest;
        }

        /// <summary>Open the banner on start when there is an entry the player has not closed yet.</summary>
        public static bool ShouldShowOnStart(WhatsNewEntry newest, string lastClosedId)
        {
            return newest != null && newest.id != (lastClosedId ?? string.Empty);
        }
    }
}
