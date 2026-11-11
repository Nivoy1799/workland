namespace Worktown.Game.Net
{
    /// <summary>Spec 047: a Steam friend who runs a shop in this game right now (the "Join a friend" list).</summary>
    public readonly struct FriendHost
    {
        public readonly string Name;
        /// <summary>The host's SteamID64 (from the friend's rich presence "connect").</summary>
        public readonly ulong SteamId;

        public FriendHost(string name, ulong steamId)
        {
            Name = name;
            SteamId = steamId;
        }
    }
}
