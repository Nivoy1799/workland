using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Net
{
    /// <summary>
    /// 054 review: the session's one physics rule, on one global scene object instead of one copy per player: how players meet.
    /// Spec 054b (D48): a mode byte (<see cref="PlayerCollisionMode"/>), default <see cref="PlayerCollisionMode.Soft"/> in every build:
    /// no rigid contact, the players' own movement pushes them apart (<see cref="PlayerSeparation"/>). <c>-player-collision hard|off</c>
    /// (dev builds) is the A/B: rigid capsules, or pass-through. The host decides when its server starts; every client applies the same
    /// rule (the layer matrix, Player against Player, collides only in hard) when the scene object arrives, which is before its own
    /// player spawns, so the server and the owner's prediction never disagree. The world, stations and NPCs are unaffected. No RPC.
    /// </summary>
    public sealed class SessionPhysics : NetworkBehaviour
    {
        /// <summary>Spec 054b (D48): players block each other softly unless the dev A/B says otherwise; release builds always.</summary>
        public const PlayerCollisionMode DefaultMode = PlayerCollisionMode.Soft;

        /// <summary>The host's <see cref="PlayerCollisionMode"/> as its byte (one byte on the wire).</summary>
        public readonly SyncVar<byte> PlayerCollision = new SyncVar<byte>((byte)DefaultMode);

        /// <summary>The rule this process uses now (the default before any session object arrived). Read by every player's tick.</summary>
        public static PlayerCollisionMode ModeNow { get; private set; } = DefaultMode;

        private void Awake()
        {
            PlayerCollision.OnChange += (_, _, asServer) => { if (!asServer) Apply(); };
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            PlayerCollisionMode mode = DefaultMode;
            NetBootstrap boot = FindFirstObjectByType<NetBootstrap>();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (boot != null && boot.Args.PlayerCollisionGiven) mode = boot.Args.PlayerCollision; // the A/B flag
#else
            // 054 review: a release build never takes the experiment's flag, and says so instead of ignoring it silently.
            if (boot != null && boot.Args.PlayerCollisionGiven)
                Debug.LogWarning("[Net] -player-collision is a development-build experiment; in this build players block each other softly");
#endif
            if (PlayerCollision.Value != (byte)mode) PlayerCollision.Value = (byte)mode;
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
            ApplyRule(DefaultMode); // the next session starts from the default
        }

        private void Apply() => ApplyRule(ModeOf(PlayerCollision.Value));

        /// <summary>The mode a received byte stands for; an unknown value is the default (a build with other values cannot join:
        /// the protocol revision is in the build hash).</summary>
        public static PlayerCollisionMode ModeOf(byte value) =>
            value <= (byte)PlayerCollisionMode.Soft ? (PlayerCollisionMode)value : DefaultMode;

        /// <summary>Player against Player in the physics layer matrix (rigid only in hard); the players' ground and headroom probes follow.</summary>
        public static void ApplyRule(PlayerCollisionMode mode)
        {
            PlayerCollisionMode before = ModeNow;
            bool rigidBefore = !Physics.GetIgnoreLayerCollision(PhysicsLayers.Player, PhysicsLayers.Player);
            ModeNow = mode;
            bool rigid = mode == PlayerCollisionMode.Hard;
            PlayerController.ApplyPlayerCollision(rigid);
            if (before != mode) Debug.Log("[Net] players: " + Describe(mode) + " (the host's rule)");
            if (rigidBefore == rigid) return;
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                if (player != null) player.RefreshBodyMask();
        }

        private static string Describe(PlayerCollisionMode mode) => mode switch
        {
            PlayerCollisionMode.Hard => "rigid capsules",
            PlayerCollisionMode.Off => "walk through each other",
            _ => "block each other softly",
        };
    }
}
