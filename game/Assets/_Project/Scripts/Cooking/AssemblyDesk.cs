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
        public CookOutcome Press(int board, int dispenser, int owner, uint tick) => Press(board, dispenser, owner, tick, null);

        /// <summary>
        /// 031b review: the press with the player's wallet: a player who cannot pay the smallest portion
        /// (<see cref="PriceRules.PortionStepPercent"/>) is refused (NotEnoughMoney) before the dispenser is taken, so a dispenser can
        /// never be held busy for free. A repeated press of the same player is checked like the first. Nothing is charged here.
        /// </summary>
        public CookOutcome Press(int board, int dispenser, int owner, uint tick, IAssemblyWallet wallet)
        {
            string sauce = BuildRules.SauceOf(dispenser);
            if (sauce != null && wallet != null && !_kitchen.IsSqueezing(board, dispenser, out _, out _)
                && !wallet.CanAfford(owner, sauce, PriceRules.PortionStepPercent))
                return wallet.Short; // spec 034: OutOfStock when the sauce's bottles are used up
            return _kitchen.TryStartSqueeze(board, dispenser, owner, tick);
        }

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
            if (!_kitchen.CommitBuild(_kitchen.BuildSpotOf(board), build, sauce, top))
            {
                wallet?.Refund(owner, sauce, PriceRules.PortionPercent(seconds), cost); // spec 034: the refund backstop
                return CookOutcome.SimRefused;
            }
            result = new SqueezeResult(seconds, top, cost, build);
            return CookOutcome.Ok;
        }

        /// <summary>Drops the squeeze without a result or a charge (its player left, or is gone).</summary>
        public void Cancel(int board, int dispenser) => _kitchen.CancelSqueeze(board, dispenser);

        /// <summary>The key of a running squeeze in the server's squeezer table: the board's index and the dispenser (as KitchenService keys it).</summary>
        public static long KeyOf(int board, int dispenser) => ((long)board << 8) | (uint)(dispenser & 0xFF);
        public static int BoardOfKey(long key) => (int)(key >> 8);
        public static int DispenserOfKey(long key) => (int)(key & 0xFF);

        /// <summary>
        /// 031b review, pure: the squeezes whose player is gone (<paramref name="present"/> says no: left, despawned, destroyed) are
        /// removed from <paramref name="squeezers"/> and cancelled without a charge; their keys end up in <paramref name="dropped"/>
        /// (cleared first). The others stay as they are.
        /// </summary>
        public void DropLeftSqueezes<T>(System.Collections.Generic.Dictionary<long, T> squeezers, System.Func<T, bool> present,
            System.Collections.Generic.List<long> dropped)
        {
            dropped.Clear();
            if (squeezers == null || present == null) return;
            foreach (System.Collections.Generic.KeyValuePair<long, T> kv in squeezers)
                if (!present(kv.Value)) dropped.Add(kv.Key);
            foreach (long key in dropped)
            {
                squeezers.Remove(key);
                Cancel(BoardOfKey(key), DispenserOfKey(key));
            }
        }

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
            if (!_kitchen.CommitBuild(spot, packed))
            {
                wallet?.Refund(owner, BuildRules.PackagingId(packaging), 100, cost); // spec 034: the refund backstop
                packed = null;
                return CookOutcome.SimRefused;
            }
            return CookOutcome.Ok;
        }
    }
}
