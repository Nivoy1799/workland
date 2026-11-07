using System;

namespace Worktown.Game.Social
{
    /// <summary>
    /// Client-side events for the HUD (spec 012): quick chat and pings that arrived from the server.
    /// Local display only; nothing here goes back to the network.
    /// </summary>
    public static class SocialFeed
    {
        /// <summary>(player id, text) for a quick-chat line or a ping toast.</summary>
        public static event Action<int, string> Message;

        internal static void Publish(int playerId, string text)
        {
            Message?.Invoke(playerId, text);
        }
    }
}
