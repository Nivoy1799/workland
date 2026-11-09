using System;
using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 028 (D27): the world is vegan. Item ids, display names, grocer entries, menu dishes, name catalogs and
    /// patch notes never contain an animal-product word. Whole words only: "eggplant" is fine, "egg" and "eggs" are not.
    /// Art model file names are not content and are not checked.
    /// </summary>
    public static class ContentRules
    {
        private static readonly string[] Words =
        {
            "cheese", "milk", "butter", "egg", "meat", "beef", "pork", "chicken", "fish", "bacon", "ham", "gelatin",
            "honey", "cream", "yoghurt", "yogurt",
        };

        /// <summary>The forbidden words, singular; plurals ("eggs", "fishes") are matched as well.</summary>
        public static IReadOnlyList<string> ForbiddenWords { get; } = Array.AsReadOnly(Words);

        /// <summary>
        /// The first forbidden word in <paramref name="text"/> (case-insensitive, whole words; letters delimit words, so
        /// "vegan_cheese" and "egg-free" contain "cheese" and "egg"), or null.
        /// </summary>
        public static string? FindForbidden(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int i = 0;
            while (i < text!.Length)
            {
                if (!char.IsLetter(text[i])) { i++; continue; }
                int start = i;
                while (i < text.Length && char.IsLetter(text[i])) i++;
                string word = text.Substring(start, i - start).ToLowerInvariant();
                foreach (string forbidden in Words)
                    if (word == forbidden || word == forbidden + "s" || word == forbidden + "es") return forbidden;
            }
            return null;
        }
    }
}
