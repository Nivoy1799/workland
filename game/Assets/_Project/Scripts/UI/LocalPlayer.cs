using FishNet.Managing;
using FishNet.Object;
using Worktown.Game.Net;

namespace Worktown.Game.UI
{
    /// <summary>Finds this client's own player object for the views (spec 011).</summary>
    public static class LocalPlayer
    {
        public static PickupInteractor Find(NetworkManager networkManager)
        {
            if (networkManager == null || !networkManager.IsClientStarted) return null;
            foreach (NetworkObject obj in networkManager.ClientManager.Connection.Objects)
                if (obj != null && obj.TryGetComponent(out PickupInteractor player))
                    return player;
            return null;
        }
    }
}
