using System.Collections.Generic;

namespace Worktown.Game.Social
{
    /// <summary>Predefined quick-chat lines (spec 012 default list). No free text, nothing to moderate.</summary>
    public static class QuickChatCatalog
    {
        public static readonly IReadOnlyList<SocialPhrase> All = new[]
        {
            new SocialPhrase(0, "On my way", "On my way"),
            new SocialPhrase(1, "I'll cook", "I'll cook"),
            new SocialPhrase(2, "Need potatoes", "Need potatoes"),
            new SocialPhrase(3, "Selling now", "Selling now"),
            new SocialPhrase(4, "Good job", "Good job"),
            new SocialPhrase(5, "Sorry", "Sorry")
        };

        public static bool TryGet(byte id, out SocialPhrase phrase)
        {
            phrase = id < All.Count ? All[id] : null;
            return phrase != null;
        }
    }
}
