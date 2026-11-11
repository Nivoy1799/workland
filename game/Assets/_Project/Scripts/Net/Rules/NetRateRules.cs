namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 054, pure: every network send rate and interval in one place. The tick stays at <see cref="TickRate"/> (FishNet's
    /// TimeManager; the owner's prediction and the server's simulation run every tick). What changes is how often the host tells the
    /// others where things are: other players and customers through a server-authoritative NetworkTransform every
    /// <see cref="RemotePlayerInterval"/> ticks (15 Hz), drawn <see cref="RemoteInterpolation"/> updates behind (about 133 ms);
    /// a carried item sends nothing while it is held (every viewer draws it in the holder's hand, spec 024).
    /// </summary>
    public static class NetRateRules
    {
        /// <summary>The network tick (Hz). Not changed by this spec (scene builder and FishNet's default agree).</summary>
        public const int TickRate = 30;

        /// <summary>Ticks between two position updates of another player (2: 15 Hz at the 30 Hz tick).</summary>
        public const int RemotePlayerInterval = 2;
        /// <summary>Updates a remote player's NetworkTransform buffers before it moves (FishNet's interpolation, in received updates).</summary>
        public const int RemoteInterpolation = 2;
        /// <summary>Ticks between two position updates of a customer (NPC guest): 15 Hz like the players.</summary>
        public const int CustomerInterval = 2;
        /// <summary>Updates a customer's NetworkTransform buffers before it moves.</summary>
        public const int CustomerInterpolation = 2;
        /// <summary>Loose items (food, cookware, tools, boxes): every tick while they move, as before.</summary>
        public const int ItemInterval = 1;
        /// <summary>054 review: an item's NetworkTransform jumps (teleports) to a pose at least this far (m) from the last one.</summary>
        public const float ItemTeleportMetres = 0.5f;
        /// <summary>054 review: the remote player's extrapolation (NetworkTransform updates; FishNet's default 2 overshoots a stop).</summary>
        public const int RemoteExtrapolation = 1;

        /// <summary>A carried item's NetworkTransform sends neither position nor rotation (B2): nobody sees its root while held.</summary>
        public const bool SyncWhileHeld = false;

        /// <summary><c>-remote-hz</c> without the flag: 15 Hz.</summary>
        public const int DefaultRemoteHz = TickRate / RemotePlayerInterval;

        /// <summary>A rate <c>-remote-hz</c> accepts: the tick rate (every tick) or the default 15 Hz.</summary>
        public static bool IsRemoteHz(int hz) => hz == TickRate || hz == DefaultRemoteHz;

        /// <summary>The send interval in ticks for a remote-player rate (30 Hz: 1, 15 Hz: 2); 0 or an unknown rate: the default.</summary>
        public static int RemotePlayerIntervalFor(int hz) => IsRemoteHz(hz) ? TickRate / hz : RemotePlayerInterval;

        /// <summary>How far behind (ms) a viewer draws an object sent every <paramref name="interval"/> ticks and buffered
        /// <paramref name="interpolation"/> updates, without the half round trip: 2 x 2 at 30 Hz = 133 ms.</summary>
        public static double DisplayDelayMs(int interval, int interpolation) => 1000d / TickRate * interval * interpolation;

        /// <summary>B2: whether an item's NetworkTransform sends position and rotation; false while it is held.</summary>
        public static bool SyncsTransform(bool held) => !held || SyncWhileHeld;
    }
}
