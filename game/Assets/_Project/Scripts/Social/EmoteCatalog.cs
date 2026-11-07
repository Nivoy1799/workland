using System.Collections.Generic;

namespace Worktown.Game.Social
{
    /// <summary>The 8 emotes of the wheel (spec 012 default list). Index = wheel slot, clockwise from the top.</summary>
    public static class EmoteCatalog
    {
        public static readonly IReadOnlyList<SocialPhrase> All = new[]
        {
            new SocialPhrase(0, "Wave", "o/  Hi!"),
            new SocialPhrase(1, "Thumbs up", "(+) Yes"),
            new SocialPhrase(2, "Point", "-->  There"),
            new SocialPhrase(3, "Thanks", "Thanks!"),
            new SocialPhrase(4, "Thumbs down", "(-) No"),
            new SocialPhrase(5, "Help", "Help!"),
            new SocialPhrase(6, "Follow me", "Follow me!"),
            new SocialPhrase(7, "Cheer", "\\o/  Yay!")
        };

        public static bool TryGet(byte id, out SocialPhrase phrase)
        {
            phrase = id < All.Count ? All[id] : null;
            return phrase != null;
        }
    }
}
