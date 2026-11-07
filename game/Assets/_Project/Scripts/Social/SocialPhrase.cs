namespace Worktown.Game.Social
{
    /// <summary>One emote or quick-chat line (spec 012): an id sent over the network and its text.</summary>
    public sealed class SocialPhrase
    {
        public SocialPhrase(byte id, string text, string bubble)
        {
            Id = id;
            Text = text;
            Bubble = bubble;
        }

        public byte Id { get; }
        /// <summary>Name in menus and toasts, e.g. "Thumbs up".</summary>
        public string Text { get; }
        /// <summary>Short text above the head, e.g. "(+)"; ASCII so every font shows it.</summary>
        public string Bubble { get; }
    }
}
