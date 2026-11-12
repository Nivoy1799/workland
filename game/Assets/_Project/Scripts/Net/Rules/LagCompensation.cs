namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 054b option (a) (the netcode owner's decision, 2026-11-11), pure: the server pushes each player against the other players
    /// where that player's client drew them, so the server and the owner's prediction use the same positions and agree.
    /// The owner sends, with every input (MoveData.ViewTick), the server tick its drawn players stand for: the newest server tick it has
    /// received minus the remote players' display delay (<see cref="DisplayTicks"/>: the 15 Hz send interval times the interpolation
    /// buffer). The server clamps it to at most <see cref="MaxViewTicks"/> (667 ms) behind its own tick, so nobody can reach further
    /// back by claiming a long delay, and never ahead of it. Only the separation push uses these positions; movement limits, world
    /// collision and the no-pass-through floor (<see cref="PlayerSeparation.Block"/>) use the real ones.
    /// </summary>
    public static class LagCompensation
    {
        /// <summary>The longest view delay the server accepts (s); a longer claim is clamped to it.</summary>
        public const float MaxViewSeconds = MaxViewTicks / (float)NetRateRules.TickRate;
        /// <summary>The longest view delay the server accepts, in ticks (20 at 30 Hz, 667 ms; the netcode owner, 2026-11-12: 9 ticks
        /// clamped about 60 % of the inputs in the 15-bot rig). The delay only steers the soft push; the floor, the deep-contact push and
        /// world collision use real positions, so a long claim cannot walk anyone through a player. Within <see cref="HistoryTicks"/>.</summary>
        public const int MaxViewTicks = 20;
        /// <summary>The server's position history per player (ticks, about a second).</summary>
        public const int HistoryTicks = 32;
        /// <summary>How far behind the newest received server tick a client draws the other players: the send interval times the
        /// interpolation buffer (2 x 2 = 4 ticks, 133 ms; NetRateRules).</summary>
        public const int DisplayTicks = NetRateRules.RemotePlayerInterval * NetRateRules.RemoteInterpolation;
        /// <summary>A ViewTick that says "now" (the host's own player, a default input).</summary>
        public const uint Now = 0;

        /// <summary>The owner's view tick: the server tick its drawn players stand for, from the newest server tick it has received
        /// (<paramref name="newestServerTick"/>; 0 = nothing yet, then <see cref="Now"/>).</summary>
        public static uint ClientViewTick(uint newestServerTick) =>
            newestServerTick > DisplayTicks ? newestServerTick - DisplayTicks : Now;

        /// <summary>
        /// The server tick whose positions the server pushes a player against, for its input claiming <paramref name="claimedViewTick"/>
        /// while the server runs <paramref name="serverTick"/>: the claim, but never ahead of the server and never more than
        /// <see cref="MaxViewTicks"/> behind it. <see cref="Now"/> or a claim from the future: <paramref name="serverTick"/>.
        /// </summary>
        public static uint ServerViewTick(uint serverTick, uint claimedViewTick)
        {
            if (claimedViewTick == Now || claimedViewTick >= serverTick) return serverTick;
            uint oldest = serverTick > MaxViewTicks ? serverTick - MaxViewTicks : 0;
            return claimedViewTick < oldest ? oldest : claimedViewTick;
        }
    }
}
