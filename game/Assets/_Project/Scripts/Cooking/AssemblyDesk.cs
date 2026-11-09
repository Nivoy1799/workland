using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 031b (review of 031), server, pure: the assembly board's charging steps around <see cref="KitchenState"/>, so the
    /// money rules are tested without the network (AGENTS rule 6). Every step checks in the sim first, then charges once, then
    /// commits; a refusal changes nothing and charges nothing.
    /// - A press records the squeeze (a repeated press of the same player keeps the first start, 10.0).
    /// - The release (or the server's auto-stop) previews the sauce for the time held, charges the portion
    ///   (<see cref="PriceRules.PortionPercent"/> of the bottle: 10 % per started 0.5 s, at most 30 %, computed here on the
    ///   server), then commits. A release without this player's squeeze is a harmless Ok: nothing charged.
    /// - A player who left: the squeeze is dropped at once, nothing charged (<see cref="Cancel"/>).
    /// - Packaging charges its full grocer price.
    /// The release does not need reach (<see cref="NeedsReach"/>): the press was in reach, and a player who stepped away must
    /// still be able to stop.
    /// </summary>
    public sealed class AssemblyDesk
    {
        private readonly KitchenState _kitchen;

        public AssemblyDesk(KitchenState kitchen)
        {
            _kitchen = kitchen ?? throw new System.ArgumentNullException(nameof(kitchen));
        }

        /// <summary>What one squeeze did (for the log): its own seconds, the top sauce's seconds after it, the cost.</summary>
        public readonly struct SqueezeResult
        {
            public SqueezeResult(float seconds, float topSeconds, long cost, Item build)
            {
                Seconds = seconds;
                TopSeconds = topSeconds;
                Cost = cost;
                Build = build;
            }

            public float Seconds { get; }
            public float TopSeconds { get; }
            public long Cost { get; }
            public Item Build { get; }
        }

        /// <summary>Whether RequestStation option <paramref name="option"/> on a station of <paramref name="kind"/> needs the
        /// player in reach: everything but a dispenser's release.</summary>
        public static bool NeedsReach(StationKind kind, int option) =>
            !(kind == StationKind.Assembly && BuildRules.TryParseSqueeze(option, out _, out bool press) && !press);

        /// <summary>The press: the squeeze starts at <paramref name="tick"/> (KitchenState.TryStartSqueeze). Nothing is charged.</summary>
        public CookOutcome Press(int board, int dispenser, int owner, uint tick) => _kitchen.TryStartSqueeze(board, dispenser, owner, tick);

        /// <summary>
        /// The release by <paramref name="owner"/> at <paramref name="tick"/>: Ok and nothing happens when this player is not
        /// squeezing that dispenser; else the squeeze ends (<see cref="End"/>).
        /// </summary>
        public CookOutcome Release(int board, int dispenser, int owner, uint tick, double tickRate, IAssemblyWallet wallet,
            out SqueezeResult result)
        {
            result = default;
            if (!_kitchen.IsSqueezing(board, dispenser, out int who, out _) || who != owner) return CookOutcome.Ok;
            return End(board, dispenser, owner, tick, tickRate, wallet, out result);
        }

        /// <summary>
        /// The squeeze of <paramref name="owner"/> ends at <paramref name="tick"/> (capped at the auto-stop): the sauce layer is
        /// previewed, its portion charged once, then committed. The squeeze record is gone whatever happens; a refusal (the
        /// preview's or the wallet's) leaves the build as it was.
        /// </summary>
        public CookOutcome End(int board, int dispenser, int owner, uint tick, double tickRate, IAssemblyWallet wallet,
            out SqueezeResult result)
        {
            result = default;
            CookOutcome made = _kitchen.TryEndSqueeze(board, dispenser, owner, tick, tickRate, out Item build, out float top,
                out float seconds);
            if (made != CookOutcome.Ok) return made;
            string sauce = BuildRules.SauceOf(dispenser);
            long cost = 0;
            if (wallet != null)
            {
                CookOutcome paid = wallet.Charge(owner, sauce, PriceRules.PortionPercent(seconds), out cost);
                if (paid != CookOutcome.Ok) return paid;
            }
            _kitchen.CommitBuild(_kitchen.BuildSpotOf(board), build, sauce, top);
            result = new SqueezeResult(seconds, top, cost, build);
            return CookOutcome.Ok;
        }

        /// <summary>Drops the squeeze without a result or a charge (its player left, or is gone).</summary>
        public void Cancel(int board, int dispenser) => _kitchen.CancelSqueeze(board, dispenser);

        /// <summary>
        /// Wrap paper, a box or a bag (<paramref name="packaging"/>) onto the build on spot <paramref name="spot"/>: checked in the
        /// sim, its full grocer price charged once, then applied. A refusal changes nothing and charges nothing.
        /// </summary>
        public CookOutcome Package(int spot, int packaging, int owner, IAssemblyWallet wallet, out Item packed, out long cost)
        {
            cost = 0;
            CookOutcome fits = _kitchen.TryPreviewPackaging(spot, packaging, out packed);
            if (fits != CookOutcome.Ok) return fits;
            if (wallet != null)
            {
                CookOutcome paid = wallet.Charge(owner, BuildRules.PackagingId(packaging), 100, out cost);
                if (paid != CookOutcome.Ok)
                {
                    packed = null;
                    return paid;
                }
            }
            _kitchen.CommitBuild(spot, packed);
            return CookOutcome.Ok;
        }
    }
}
