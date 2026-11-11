using FishNet.Broadcast;

namespace Worktown.Game.Net
{
    /// <summary>
    /// Spec 047, server to every admitted client, whenever someone joins or leaves: who is in the shop (connection ids and names,
    /// the same order; the host's id). The lobby panel shows it. A connection message: no game data.
    /// </summary>
    public struct LobbyRoster : IBroadcast
    {
        public int[] Ids;
        public string[] Names;
        public int HostId;
        public int MaxPlayers;
    }
}
