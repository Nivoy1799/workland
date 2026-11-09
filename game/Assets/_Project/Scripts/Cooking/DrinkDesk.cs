using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 033, server, pure: the drink dispenser's steps around <see cref="KitchenState"/>, the 031b AssemblyDesk pattern (AGENTS rule
    /// 6: money and items are decided here, tested without the network). Every step checks in the sim first, then charges once, then
    /// commits; a refusal changes nothing and charges nothing.
    /// - A press records the pour for the cup in the hands (a repeated press of the same player keeps the first start). A player who
    ///   cannot pay the smallest pour (the cup when it is empty, plus 1 %) is refused before the tap is taken.
    /// - The release (or the server's stop at <see cref="Drinks.MaxMl"/>) previews the cup for the ml held, charges it once (the cup
    ///   when it was empty plus <see cref="PriceRules.FillPercent"/> of the drink, computed here on the server), then commits; a pour
    ///   over the brim marks the dispenser's spill (spec 035 cleans it). A release without this player's pour is a harmless Ok.
    /// - The cup must still be in the hands at the release (put down, it is no pour: nothing charged).
    /// - A player who left: the pour is dropped at once, nothing charged (<see cref="Cancel"/>).
    /// - The lid is free (the cup's price includes it); the cup stack gives empty hands a cup for free (charged with its first pour).
    /// The release does not need reach (<see cref="NeedsReach"/>): the press was in reach.
    /// </summary>
    public sealed class DrinkDesk
    {
        private readonly KitchenState _kitchen;

        public DrinkDesk(KitchenState kitchen)
        {
            _kitchen = kitchen ?? throw new System.ArgumentNullException(nameof(kitchen));
        }

        /// <summary>What one pour did (for the log and the held cup): its ml, the cup's ml after it, the cost, the cup and its object.</summary>
        public readonly struct PourResult
        {
            public PourResult(float poured, float total, long cost, Item cup, int cupObjectId, bool spilled)
            {
                Poured = poured;
                Total = total;
                Cost = cost;
                Cup = cup;
                CupObjectId = cupObjectId;
                Spilled = spilled;
            }

            public float Poured { get; }
            public float Total { get; }
            public long Cost { get; }
            public Item Cup { get; }
            public int CupObjectId { get; }
            public bool Spilled { get; }
        }

        /// <summary>Whether RequestStation option <paramref name="option"/> on a station of <paramref name="kind"/> needs the player in
        /// reach: everything but a tap's release.</summary>
        public static bool NeedsReach(StationKind kind, int option) =>
            !(kind == StationKind.DrinkDispenser && DrinkRules.TryParsePour(option, out _, out bool press) && !press);

        /// <summary>
        /// The press of tap <paramref name="tap"/> holding cup <paramref name="heldCupId"/> (-1: nothing held) at <paramref name="tick"/>.
        /// Nothing is charged here.
        /// </summary>
        public CookOutcome Press(int station, int tap, int owner, int heldCupId, uint tick, IDrinkWallet wallet)
        {
            if (!DrinkRules.IsTap(tap)) return CookOutcome.InvalidOption;
            if (!_kitchen.IsPouring(station, tap, out _, out _, out _))
            {
                CookOutcome can = _kitchen.CanPourInto(heldCupId, tap);
                if (can != CookOutcome.Ok) return can;
                bool empty = _kitchen.TryGet(heldCupId, out Item cup) && Drinks.IsEmptyCup(cup);
                if (wallet != null && !wallet.CanAfford(owner, DrinkRules.DrinkOf(tap), 1, empty)) return wallet.Short; // 034: or OutOfStock
            }
            return _kitchen.TryStartPour(station, tap, owner, heldCupId, tick);
        }

        /// <summary>
        /// The release by <paramref name="owner"/> at <paramref name="tick"/>: Ok and nothing happens when this player is not pouring
        /// that tap; else the pour ends (<see cref="End"/>).
        /// </summary>
        public CookOutcome Release(int station, int tap, int owner, uint tick, double tickRate, int heldCupId, IDrinkWallet wallet,
            out PourResult result)
        {
            result = default;
            if (!_kitchen.IsPouring(station, tap, out int who, out _, out _) || who != owner) return CookOutcome.Ok;
            return End(station, tap, owner, tick, tickRate, heldCupId, wallet, out result);
        }

        /// <summary>
        /// The pour of <paramref name="owner"/> ends at <paramref name="tick"/> (capped at the stop): the cup is previewed, the pour charged
        /// once, then committed (and a spill marked). The pour record is gone whatever happens; a refusal (the preview's or the wallet's)
        /// leaves the cup as it was.
        /// </summary>
        public CookOutcome End(int station, int tap, int owner, uint tick, double tickRate, int heldCupId, IDrinkWallet wallet,
            out PourResult result)
        {
            result = default;
            CookOutcome made = _kitchen.TryEndPour(station, tap, owner, tick, tickRate, heldCupId, out Item cup, out float total,
                out float poured, out bool wasEmpty, out int cupId);
            if (made != CookOutcome.Ok) return made;
            long cost = 0;
            if (wallet != null)
            {
                CookOutcome paid = wallet.Charge(owner, DrinkRules.DrinkOf(tap), PriceRules.FillPercent(poured), wasEmpty, out cost);
                if (paid != CookOutcome.Ok) return paid;
            }
            if (!_kitchen.CommitCup(cupId, cup, total))
            {
                wallet?.Refund(owner, DrinkRules.DrinkOf(tap), PriceRules.FillPercent(poured), wasEmpty, cost); // spec 034: the backstop
                return CookOutcome.SimRefused;
            }
            bool spilled = total >= Drinks.SpillFrom;
            if (spilled) _kitchen.MarkSpill(station);
            result = new PourResult(poured, total, cost, cup, cupId, spilled);
            return CookOutcome.Ok;
        }

        /// <summary>Drops the pour without a result or a charge (its player left, or is gone).</summary>
        public void Cancel(int station, int tap) => _kitchen.CancelPour(station, tap);

        /// <summary>The key of a running pour in the server's pourer table: the dispenser's index and the tap.</summary>
        public static long KeyOf(int station, int tap) => ((long)station << 8) | (uint)(tap & 0xFF);
        public static int StationOfKey(long key) => (int)(key >> 8);
        public static int TapOfKey(long key) => (int)(key & 0xFF);

        /// <summary>
        /// Pure: the pours whose player is gone (<paramref name="present"/> says no: left, despawned, destroyed) are removed from
        /// <paramref name="pourers"/> and cancelled without a charge; their keys end up in <paramref name="dropped"/> (cleared first).
        /// </summary>
        public void DropLeftPours<T>(System.Collections.Generic.Dictionary<long, T> pourers, System.Func<T, bool> present,
            System.Collections.Generic.List<long> dropped)
        {
            dropped.Clear();
            if (pourers == null || present == null) return;
            foreach (System.Collections.Generic.KeyValuePair<long, T> kv in pourers)
                if (!present(kv.Value)) dropped.Add(kv.Key);
            foreach (long key in dropped)
            {
                pourers.Remove(key);
                Cancel(StationOfKey(key), TapOfKey(key));
            }
        }

        /// <summary>The lid on the cup in the hands (<paramref name="heldCupId"/>): checked in the sim, then applied. Free.</summary>
        public CookOutcome Lid(int heldCupId, out Item lidded)
        {
            CookOutcome fits = _kitchen.TryPreviewLid(heldCupId, out lidded);
            if (fits != CookOutcome.Ok) return fits;
            _kitchen.CommitLid(heldCupId, lidded);
            return CookOutcome.Ok;
        }

        /// <summary>Whether empty hands may take a cup now: HandsFull, NoCupsLeft (<see cref="DrinkRules.MaxEmptyCups"/>), else Ok.</summary>
        public CookOutcome CanTakeCup(bool holding)
        {
            CookOutcome hands = DrinkRules.CanTakeCup(holding);
            if (hands != CookOutcome.Ok) return hands;
            return _kitchen.EmptyCupCount() >= DrinkRules.MaxEmptyCups ? CookOutcome.NoCupsLeft : CookOutcome.Ok;
        }
    }
}
