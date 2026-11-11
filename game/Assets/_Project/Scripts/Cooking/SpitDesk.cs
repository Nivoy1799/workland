using System.Collections.Generic;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 036, server, pure: the seitan spits' state around the sim's cone (Sim.Cooking.SpitCone; AGENTS rule 6: decided here, tested
    /// without the network), keyed by the spit station's index. Each spit has a heat level (the knob), maybe a cone (loaded from stock by
    /// the caller), its catch tray (at most <see cref="SpitRules.TrayCapacity"/> portions) and at most one shave hold. The outer layer
    /// cooks with the server's ticks: the cone is kept as of a tick and folded forward on every change (the knob, a shave).
    /// Shave holds follow the 031b dispenser rules: a press records (owner, start tick); the same player's repeated press keeps the first
    /// start (10.0); another player is DispenserBusy; the release ends it and the server-measured seconds give the portions
    /// (<see cref="Spit.PortionsFor"/>), each with the outer layer's doneness, then the layer resets. The server ends a hold at
    /// <see cref="Spit.MaxShaveSeconds"/> by itself (<see cref="DueHolds"/>); a player who left is dropped (<see cref="DropLeft"/>).
    /// Every step checks first and changes nothing on a refusal.
    /// </summary>
    public sealed class SpitDesk
    {
        private sealed class Record
        {
            public SpitCone Cone;
            public uint ConeTick;
            public int Level;
            public readonly List<Item> Tray = new List<Item>(SpitRules.TrayCapacity);
            public bool Holding;
            public int Owner;
            public uint HoldStart;
        }

        private readonly KitchenState _kitchen;
        private readonly Dictionary<int, Record> _spits = new Dictionary<int, Record>();
        private static readonly IReadOnlyList<Item> NoItems = new Item[0];

        public SpitDesk(KitchenState kitchen)
        {
            _kitchen = kitchen ?? throw new System.ArgumentNullException(nameof(kitchen));
        }

        /// <summary>Counts every change (the server republishes the bytes).</summary>
        public int Version { get; private set; }

        /// <summary>Spit station <paramref name="station"/> exists (bare, off, an empty tray).</summary>
        public void Register(int station)
        {
            if (!_spits.ContainsKey(station)) _spits[station] = new Record();
        }

        /// <summary>
        /// 036 review: the server stops at <paramref name="tick"/>: how many loaded cones are still untouched (all
        /// <see cref="Spit.PortionsPerCone"/> portions, never shaved), so the caller puts them back into the cone stock line; those spits
        /// are bare. Spec 045: a partly shaved cone is no longer lost: it stays on its spit with the portions it has left and the browning
        /// it had at the stop (folded to <paramref name="tick"/>), the knob off (an off cone keeps its layer, so the next session's ticks
        /// cannot heat it until the knob turns), no shave hold. Every catch tray is emptied (food left out, like food in a pan).
        /// </summary>
        public int ClearReturningUntouched(uint tick, double tickRate)
        {
            int untouched = 0;
            foreach (Record r in _spits.Values)
            {
                Fold(r, tick, tickRate);
                if (r.Cone != null && r.Cone.Portions == Spit.PortionsPerCone && r.Cone.Shaves == 0)
                {
                    untouched++;
                    r.Cone = null;
                }
                r.Level = Spit.Off;
                r.Holding = false;
                r.Tray.Clear();
            }
            Version++;
            return untouched;
        }

        /// <summary>Spec 045: the spits that keep a partly used cone over a server stop, sorted (the stop's log line).</summary>
        public void KeptCones(List<int> into)
        {
            into.Clear();
            foreach (KeyValuePair<int, Record> kv in _spits)
                if (kv.Value.Cone != null && !kv.Value.Cone.IsEmpty) into.Add(kv.Key);
            into.Sort();
        }

        /// <summary>
        /// 036 review: a cone may be loaded without a stock room only where nothing is paid at all (no economy either: tests, a bare
        /// scene). With an economy but no supply, a free cone would be a faucet: the load is refused (OutOfStock).
        /// </summary>
        public static bool MayLoadWithoutStock(bool hasStock, bool hasEconomy) => hasStock || !hasEconomy;

        public bool IsSpit(int station) => _spits.ContainsKey(station);

        /// <summary>The spit stations, sorted (stable logs).</summary>
        public void Stations(List<int> into)
        {
            into.Clear();
            foreach (int s in _spits.Keys) into.Add(s);
            into.Sort();
        }

        // ---- the cone and its heat ----

        /// <summary>The cone as it stands at <paramref name="tick"/> (folded forward with the heat; no change), or null when bare.</summary>
        public SpitCone ConeAt(int station, uint tick, double tickRate)
        {
            if (!_spits.TryGetValue(station, out Record r) || r.Cone == null) return null;
            return r.Cone.Heat((float)CookingTime.SimSeconds(CookingTime.ElapsedTicks(r.ConeTick, tick), tickRate), r.Level);
        }

        public bool HasCone(int station) => _spits.TryGetValue(station, out Record r) && r.Cone != null && !r.Cone.IsEmpty;
        public int LevelOf(int station) => _spits.TryGetValue(station, out Record r) ? r.Level : Spit.Off;

        private void Fold(Record r, uint tick, double tickRate)
        {
            if (r.Cone != null)
                r.Cone = r.Cone.Heat((float)CookingTime.SimSeconds(CookingTime.ElapsedTicks(r.ConeTick, tick), tickRate), r.Level);
            r.ConeTick = tick;
        }

        /// <summary>The knob: off, low, high, off (the heat so far is kept). UnknownStation for no spit.</summary>
        public CookOutcome TurnKnob(int station, uint tick, double tickRate)
        {
            if (!_spits.TryGetValue(station, out Record r)) return CookOutcome.UnknownStation;
            Fold(r, tick, tickRate);
            r.Level = Spit.NextLevel(r.Level);
            Version++;
            return CookOutcome.Ok;
        }

        /// <summary>Whether a new cone may go on now (the hands and the stock are the caller's): ConeLoaded, DispenserBusy (a shave runs).</summary>
        public CookOutcome CanLoad(int station, bool holding)
        {
            if (!_spits.TryGetValue(station, out Record r)) return CookOutcome.UnknownStation;
            return SpitRules.CanLoad(holding, HasCone(station), r.Holding);
        }

        /// <summary>A fresh cone goes on at <paramref name="tick"/> (the caller took it from stock after <see cref="CanLoad"/>). False for no spit.</summary>
        public bool Load(int station, uint tick)
        {
            if (!_spits.TryGetValue(station, out Record r)) return false;
            r.Cone = SpitCone.Fresh();
            r.ConeTick = tick;
            Version++;
            return true;
        }

        /// <summary>
        /// The outer layer's 027c reading at <paramref name="tick"/> (the zone, the progress, the warning) from the shave op's timeline;
        /// <see cref="HeatReading.Empty"/> when bare.
        /// </summary>
        public HeatReading Reading(int station, uint tick, double tickRate)
        {
            SpitCone cone = ConeAt(station, tick, tickRate);
            if (cone == null || cone.IsEmpty) return HeatReading.Empty;
            return HeatZoneRules.Read(cone.OuterSeconds, _kitchen.SpitTimeline());
        }

        /// <summary>The name a shave would give now (the cone's label: the take name), or null when bare.</summary>
        public string TakeName(int station, uint tick, double tickRate)
        {
            SpitCone cone = ConeAt(station, tick, tickRate);
            if (cone == null || cone.IsEmpty) return null;
            return _kitchen.TryShave(cone.OuterSeconds, out Item portion) == CookOutcome.Ok ? KitchenState.NameOf(portion) : null;
        }

        /// <summary>The station's byte (the level, someone shaving, a cone).</summary>
        public byte Flags(int station) =>
            _spits.TryGetValue(station, out Record r) ? SpitRules.Flags(r.Level, r.Holding, HasCone(station)) : (byte)0;

        /// <summary>The cone vessel's mode byte (portions left, the shave counter).</summary>
        public byte ConeMode(int station) =>
            _spits.TryGetValue(station, out Record r) && r.Cone != null ? SpitRules.ConeMode(r.Cone.Portions, r.Cone.Shaves) : (byte)0;

        // ---- the shave (a server-timed hold) ----

        /// <summary>
        /// The press of <paramref name="owner"/> at <paramref name="tick"/>: the same player's repeated press keeps its first start (Ok);
        /// another player's hold is DispenserBusy; else the knife in the hands (NoKnife), a cone (NoCone) and room in the tray (TrayFull).
        /// </summary>
        public CookOutcome Press(int station, int owner, bool holdsKnife, uint tick)
        {
            if (!_spits.TryGetValue(station, out Record r)) return CookOutcome.UnknownStation;
            if (r.Holding) return r.Owner == owner ? CookOutcome.Ok : CookOutcome.DispenserBusy;
            CookOutcome can = SpitRules.CanShave(HasCone(station), holdsKnife, r.Tray.Count, false);
            if (can != CookOutcome.Ok) return can;
            r.Holding = true;
            r.Owner = owner;
            r.HoldStart = tick;
            Version++;
            return CookOutcome.Ok;
        }

        public bool IsHeld(int station, out int owner, out uint start)
        {
            owner = -1;
            start = 0;
            if (!_spits.TryGetValue(station, out Record r) || !r.Holding) return false;
            owner = r.Owner;
            start = r.HoldStart;
            return true;
        }

        /// <summary>The seconds the server measured between two ticks (0 for a bad rate).</summary>
        public static double HeldSeconds(uint start, uint now, double tickRate) =>
            tickRate > 0d ? CookingTime.ElapsedTicks(start, now) / tickRate : 0d;

        /// <summary>
        /// The release by <paramref name="owner"/> at <paramref name="tick"/>: Ok and nothing when this player holds nothing there. Else the
        /// hold ends; with the knife still in the hands (<paramref name="holdsKnife"/>) the measured seconds shave their portions into the
        /// tray (<paramref name="portions"/>, maybe 0 for a short tap), each with the outer layer's doneness, and the layer resets.
        /// </summary>
        public CookOutcome Release(int station, int owner, bool holdsKnife, uint tick, double tickRate, out int portions)
        {
            portions = 0;
            if (!_spits.TryGetValue(station, out Record r) || !r.Holding || r.Owner != owner) return CookOutcome.Ok;
            double seconds = HeldSeconds(r.HoldStart, tick, tickRate);
            r.Holding = false;
            Version++;
            if (!holdsKnife) return CookOutcome.Ok; // the knife was put down meanwhile: no shave
            portions = Shave(r, seconds, tick, tickRate);
            return CookOutcome.Ok;
        }

        private int Shave(Record r, double seconds, uint tick, double tickRate)
        {
            if (r.Cone == null || r.Cone.IsEmpty) return 0;
            Fold(r, tick, tickRate);
            float layer = r.Cone.OuterSeconds;
            SpitCone after = r.Cone.Shave(System.Math.Min(seconds, Spit.MaxShaveSeconds), SpitRules.TrayCapacity - r.Tray.Count, out int n);
            if (n <= 0) return 0;
            var made = new List<Item>(n);
            for (int i = 0; i < n; i++)
            {
                if (_kitchen.TryShave(layer, out Item portion) != CookOutcome.Ok) return 0; // the sim refused: nothing changes
                made.Add(portion);
            }
            r.Tray.AddRange(made);
            r.Cone = after.IsEmpty ? null : after; // a cone that ran out leaves the spit bare: load a new one
            Version++;
            return n;
        }

        /// <summary>The spits whose hold reached <see cref="Spit.MaxShaveSeconds"/> by <paramref name="tick"/> (the auto-stop), into <paramref name="due"/>.</summary>
        public void DueHolds(uint tick, double tickRate, List<int> due)
        {
            due.Clear();
            foreach (KeyValuePair<int, Record> kv in _spits)
                if (kv.Value.Holding && HeldSeconds(kv.Value.HoldStart, tick, tickRate) >= Spit.MaxShaveSeconds - 1e-4) due.Add(kv.Key);
            due.Sort();
        }

        /// <summary>Drops a hold without a shave (its player left).</summary>
        public void Cancel(int station)
        {
            if (!_spits.TryGetValue(station, out Record r) || !r.Holding) return;
            r.Holding = false;
            Version++;
        }

        /// <summary>Every hold whose player is not <paramref name="present"/> any more is dropped without a shave; their stations into <paramref name="dropped"/>.</summary>
        public void DropLeft(System.Func<int, bool> present, List<int> dropped)
        {
            dropped.Clear();
            if (present == null) return;
            foreach (KeyValuePair<int, Record> kv in _spits)
                if (kv.Value.Holding && !present(kv.Value.Owner)) dropped.Add(kv.Key);
            dropped.Sort();
            foreach (int s in dropped) Cancel(s);
        }

        /// <summary>
        /// A new cone from stock: <see cref="CanLoad"/> first (nothing is taken on a refusal), then <paramref name="takeFromStock"/> (one
        /// unit of the cone line; OutOfStock and the like refuse and change nothing), then the cone goes on. Should that fail after the take,
        /// <paramref name="giveBack"/> puts the unit back (the refund backstop). Money never moves here: the order at the terminal paid.
        /// </summary>
        public CookOutcome LoadCone(int station, bool holding, uint tick, System.Func<CookOutcome> takeFromStock, System.Action giveBack)
        {
            CookOutcome can = CanLoad(station, holding);
            if (can != CookOutcome.Ok) return can;
            CookOutcome took = takeFromStock != null ? takeFromStock() : CookOutcome.Ok;
            if (took != CookOutcome.Ok) return took;
            if (Load(station, tick)) return CookOutcome.Ok;
            giveBack?.Invoke();
            return CookOutcome.UnknownStation;
        }

        // ---- the catch tray ----

        public int TrayCount(int station) => _spits.TryGetValue(station, out Record r) ? r.Tray.Count : 0;

        public IReadOnlyList<Item> TrayOf(int station) => _spits.TryGetValue(station, out Record r) ? r.Tray : NoItems;

        /// <summary>The newest portion leaves the tray (a scoop); TrayEmpty when there is none.</summary>
        public CookOutcome TakePortion(int station, out Item portion)
        {
            portion = null;
            if (!_spits.TryGetValue(station, out Record r) || r.Tray.Count == 0) return CookOutcome.TrayEmpty;
            portion = r.Tray[r.Tray.Count - 1];
            r.Tray.RemoveAt(r.Tray.Count - 1);
            Version++;
            return CookOutcome.Ok;
        }

        /// <summary>
        /// A scoop into empty hands: one portion leaves the tray and <paramref name="handOver"/> spawns it in the hands; should that fail,
        /// it goes back into the tray. TrayEmpty, HandsFull (holding something: see <see cref="ScoopOntoBuild"/>).
        /// </summary>
        public CookOutcome ScoopIntoHands(int station, bool holding, System.Func<Item, bool> handOver, out Item portion)
        {
            portion = null;
            CookOutcome can = SpitRules.CanScoop(TrayCount(station), holding, false);
            if (can != CookOutcome.Ok) return can;
            TakePortion(station, out portion);
            if (handOver != null && handOver(portion)) return CookOutcome.Ok;
            ReturnPortion(station, portion);
            portion = null;
            return CookOutcome.SimRefused;
        }

        /// <summary>
        /// A scoop onto the open build held as food <paramref name="foodObjectId"/>: one portion becomes the build's next layer (a 031
        /// layer, KitchenState.TryAddLayerToHeld). TrayEmpty; HandsFull for food that is no open build; the build's refusals (closed, full)
        /// put the portion back and change nothing.
        /// </summary>
        public CookOutcome ScoopOntoBuild(int station, int foodObjectId, out Item build)
        {
            build = null;
            CookOutcome can = SpitRules.CanScoop(TrayCount(station), true, _kitchen.IsOpenBuild(foodObjectId));
            if (can != CookOutcome.Ok) return can;
            TakePortion(station, out Item portion);
            CookOutcome added = _kitchen.TryAddLayerToHeld(foodObjectId, portion, out build);
            if (added == CookOutcome.Ok) return CookOutcome.Ok;
            ReturnPortion(station, portion);
            return added;
        }

        /// <summary>The backstop: a portion scooped but never handed over or put on the build goes back into the tray.</summary>
        public void ReturnPortion(int station, Item portion)
        {
            if (portion == null || !_spits.TryGetValue(station, out Record r)) return;
            r.Tray.Add(portion);
            Version++;
        }
    }
}
