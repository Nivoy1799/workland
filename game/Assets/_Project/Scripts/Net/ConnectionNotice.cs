using FishNet.Broadcast;

namespace Worktown.Game.Net
{
    /// <summary>
    /// Spec 047, server to one client, right before the server disconnects it: why (a Rules.DisconnectReason as a byte: refused for
    /// the version or a full shop, kicked, no answer, the shop closed) and a short detail for the error screen (the host's build,
    /// the places). A connection message: no game data.
    /// </summary>
    public struct ConnectionNotice : IBroadcast
    {
        public byte Reason;
        public string Detail;
    }
}
