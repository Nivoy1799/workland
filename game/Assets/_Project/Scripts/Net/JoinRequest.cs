using FishNet.Broadcast;

namespace Worktown.Game.Net
{
    /// <summary>
    /// Spec 047, client to server, before authentication: the client's build hash (BuildHash; the server compares it) and the name
    /// other players see (only shown, cleaned by JoinRules.CleanName). A connection message: no game data.
    /// </summary>
    public struct JoinRequest : IBroadcast
    {
        public string Build;
        public string Name;
    }
}
