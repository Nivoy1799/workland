using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Worktown.Game.Net
{
    /// <summary>
    /// 054 review: the session's one physics rule, on one global scene object instead of one copy per player: whether players collide
    /// with each other (spec 054 experiment, <c>-player-collision</c>, dev builds; on by default). The host decides when its server
    /// starts; every client applies the same layer rule (Player against Player) when the scene object arrives, which is before its own
    /// player spawns, so the server and the owner's prediction never disagree. The world, stations and NPCs are unaffected. No RPC.
    /// </summary>
    public sealed class SessionPhysics : NetworkBehaviour
    {
        public readonly SyncVar<bool> PlayersCollide = new SyncVar<bool>(true);

        private void Awake()
        {
            PlayersCollide.OnChange += (_, _, asServer) => { if (!asServer) Apply(); };
        }

        /// <summary>The rule this process uses now (the default before any session object arrived).</summary>
        public static bool CollideNow => !Physics.GetIgnoreLayerCollision(Rules.PhysicsLayers.Player, Rules.PhysicsLayers.Player);

        public override void OnStartServer()
        {
            base.OnStartServer();
            bool collide = true;
            NetBootstrap boot = FindFirstObjectByType<NetBootstrap>();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            collide = boot == null || boot.Args.PlayerCollision;
#else
            // 054 review: a release build never takes the experiment's flag, and says so instead of ignoring it silently.
            if (boot != null && boot.Args.PlayerCollisionGiven)
                Debug.LogWarning("[Net] -player-collision is a development-build experiment; this build keeps players colliding");
#endif
            if (PlayersCollide.Value != collide) PlayersCollide.Value = collide;
            Apply();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerStarted) Apply(); // the host's rule, before the own player's first predicted tick
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            ApplyRule(true); // the next session starts from the default
        }

        private void Apply() => ApplyRule(PlayersCollide.Value);

        /// <summary>Player against Player in the physics layer matrix; the players' ground and headroom probes follow it.</summary>
        public static void ApplyRule(bool collide)
        {
            bool before = CollideNow;
            PlayerController.ApplyPlayerCollision(collide);
            if (before == collide) return;
            Debug.Log("[Net] players " + (collide ? "collide" : "walk through each other") + " (the host's rule)");
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                if (player != null) player.RefreshBodyMask();
        }
    }
}
