using System.Collections.Generic;
using Worktown.Game.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 035, server, pure: the shop's cleaning state around the sim's hygiene model (AGENTS rule 6: decided here, tested without the
    /// network). The plates (<see cref="Dishes"/>), the trash can's fill and the bags carried out, the server-timed holds (the sink's
    /// scrub and the cloth's wipe: the 031b dispenser rules) and the hygiene meter (Sim.Economy.HygieneMeter; time is the economy's game
    /// minute, passed in). Every step checks first and changes nothing on a refusal. Discarding never moves money or stock: the trash
    /// only counts (anti-faucet).
    /// Holds: a press records (owner, start tick) under the key (station, kind); a repeated press by the same player keeps the first
    /// start (031b 10.0); another player gets DispenserBusy; one player holds one thing at a time. The release ends it: done when the
    /// server measured at least the hold's seconds (<see cref="CleaningRules.HoldSeconds"/>), else nothing happens. The server ends a
    /// hold at its time by itself (<see cref="DueHolds"/>: the auto-stop); a player who left is dropped (<see cref="DropLeft"/>).
    /// </summary>
    public sealed class CleaningRoom
    {
        private struct Hold
        {
            public int Owner;
            public uint StartTick;
        }

        private readonly Dictionary<long, Hold> _holds = new Dictionary<long, Hold>();
        private readonly Dictionary<int, int> _bags = new Dictionary<int, int>();
        private readonly List<long> _scratch = new List<long>();

        public CleaningRoom(long startMinute = 0)
        {
            Meter = HygieneMeter.Start(startMinute);
        }

        public DishRoom Dishes { get; } = new DishRoom();
        public HygieneMeter Meter { get; private set; }
        public int Hygiene => Meter.Percent;
        /// <summary>Items in the trash can (0..HygieneRules.TrashCapacity).</summary>
        public int TrashFill { get; private set; }
        /// <summary>Trash bags taken out of the can and not yet in the bin.</summary>
        public int BagsOut => _bags.Count;
        public bool TrashIsFull => HygieneRules.IsTrashFull(TrashFill);
        /// <summary>Counts every change of the trash, the holds or the meter (the dishes count their own).</summary>
        public int Version { get; private set; }

        /// <summary>A new game: clean plates on the shelf, nothing dirty, an empty trash, no holds, the meter at 100 % at <paramref name="minute"/>.</summary>
        public void Reset(long minute)
        {
            Dishes.Reset();
            _holds.Clear();
            _bags.Clear();
            TrashFill = 0;
            Meter = HygieneMeter.Start(minute);
            Version++;
        }

        /// <summary>
        /// The server stopped (FishNet despawned the dishes and bags): what was carried comes back (dishes to the return, bags into the
        /// can). 035 review: the plates are counted again for the restart (<see cref="DishRoom.Restarted"/>: the scene's two plates come
        /// back where they last were, 036 review; the pooled ones are gone), so the shop keeps exactly its plates.
        /// </summary>
        public void EndSession(List<int> scratch)
        {
            _holds.Clear();
            Dishes.CarriedIds(scratch);
            foreach (int id in scratch) Dishes.CarriedLost(id);
            Dishes.Restarted();
            scratch.Clear();
            foreach (int id in _bags.Keys) scratch.Add(id);
            foreach (int id in scratch) BagLost(id);
            Version++;
        }

        // ---- the trash ----

        /// <summary>A new game's trash at <paramref name="fill"/> items (the cook tour's host starts it nearly full). No money or stock.</summary>
        public void StartTrashAt(int fill)
        {
            TrashFill = System.Math.Max(0, System.Math.Min(HygieneRules.TrashCapacity, fill));
            Version++;
        }

        /// <summary>The check before a discard: CannotDiscard for something the trash does not take, TrashFull. No change.</summary>
        public CookOutcome CanDiscard(bool discardable) => CleaningRules.CanDiscard(discardable, TrashFill);

        /// <summary>One item went into the trash (the caller removed it after <see cref="CanDiscard"/>): the fill rises. TrashFull refuses.</summary>
        public CookOutcome Discarded()
        {
            if (TrashIsFull) return CookOutcome.TrashFull;
            TrashFill++;
            Version++;
            return CookOutcome.Ok;
        }

        /// <summary>Empty hands take the bag out of the can: its size is the fill (<paramref name="size"/>), the can is empty after. TrashEmpty.</summary>
        public CookOutcome TakeBag(bool holding, out int size)
        {
            size = 0;
            CookOutcome can = CleaningRules.CanTakeBag(holding, TrashFill);
            if (can != CookOutcome.Ok) return can;
            size = TrashFill;
            TrashFill = 0;
            Version++;
            return CookOutcome.Ok;
        }

        /// <summary>The bag from <see cref="TakeBag"/> is object <paramref name="objectId"/> now.</summary>
        public void ConfirmBag(int objectId, int size)
        {
            _bags[objectId] = size;
            Version++;
        }

        /// <summary>The backstop: a bag taken but never handed over goes back into the can.</summary>
        public void UndoTakeBag(int size)
        {
            TrashFill = System.Math.Min(HygieneRules.TrashCapacity, TrashFill + System.Math.Max(0, size));
            Version++;
        }

        public bool IsBag(int objectId) => _bags.ContainsKey(objectId);

        /// <summary>035 review: a full bag not taken out yet (a half-full bag carried out is no "full trash" for the hygiene).</summary>
        public bool HasFullBag
        {
            get
            {
                foreach (int size in _bags.Values)
                    if (HygieneRules.IsTrashFull(size)) return true;
                return false;
            }
        }

        /// <summary>Bag <paramref name="objectId"/> goes into the bin outside: it is gone (the trash is out). NotABag.</summary>
        public CookOutcome Binned(int objectId)
        {
            if (!_bags.Remove(objectId)) return CookOutcome.NotABag;
            Version++;
            return CookOutcome.Ok;
        }

        /// <summary>A bag was lost (below the world, or its object went): its trash is back in the can (up to full).</summary>
        public bool BagLost(int objectId)
        {
            if (!_bags.TryGetValue(objectId, out int size)) return false;
            _bags.Remove(objectId);
            UndoTakeBag(size);
            return true;
        }

        public void BagIds(List<int> ids)
        {
            if (ids == null) return;
            ids.Clear();
            foreach (int id in _bags.Keys) ids.Add(id);
            ids.Sort();
        }

        // ---- the holds (scrub, wipe) ----

        public static long KeyOf(int station, int kind) => ((long)station << 8) | (uint)(kind & 0xFF);
        public static int StationOfKey(long key) => (int)(key >> 8);
        public static int KindOfKey(long key) => (int)(key & 0xFF);

        public bool IsHeld(int station, int kind, out int owner, out uint startTick)
        {
            owner = -1;
            startTick = 0;
            if (!_holds.TryGetValue(KeyOf(station, kind), out Hold h)) return false;
            owner = h.Owner;
            startTick = h.StartTick;
            return true;
        }

        /// <summary>Whether anyone holds <paramref name="kind"/> at <paramref name="station"/> (the sink's scrubbing bit).</summary>
        public bool IsHeld(int station, int kind) => _holds.ContainsKey(KeyOf(station, kind));

        /// <summary>
        /// The press at <paramref name="tick"/>: the same player's repeated press keeps its first start (Ok, nothing changes); another
        /// player's hold there is DispenserBusy; a player already holding something else is DispenserBusy too. The caller checked the
        /// station (a dish in the basin, the cloth and the spill).
        /// </summary>
        public CookOutcome Press(int station, int kind, int owner, uint tick)
        {
            long key = KeyOf(station, kind);
            if (_holds.TryGetValue(key, out Hold h)) return h.Owner == owner ? CookOutcome.Ok : CookOutcome.DispenserBusy;
            foreach (KeyValuePair<long, Hold> kv in _holds)
                if (kv.Value.Owner == owner) return CookOutcome.StillHolding; // 035 review: your own other hold, not "someone"
            _holds[key] = new Hold { Owner = owner, StartTick = tick };
            Version++;
            return CookOutcome.Ok;
        }

        /// <summary>
        /// The release by <paramref name="owner"/> at <paramref name="tick"/>: Ok and nothing when this player holds nothing there;
        /// else the hold ends, <paramref name="done"/> when it lasted <see cref="CleaningRules.HoldSeconds"/> (the caller then scrubs or
        /// wipes), else it was too short and nothing happens.
        /// </summary>
        public CookOutcome Release(int station, int kind, int owner, uint tick, double tickRate, out bool done)
        {
            done = false;
            long key = KeyOf(station, kind);
            if (!_holds.TryGetValue(key, out Hold h) || h.Owner != owner) return CookOutcome.Ok;
            _holds.Remove(key);
            Version++;
            done = HeldSeconds(h.StartTick, tick, tickRate) >= CleaningRules.HoldSeconds(kind) - 1e-4;
            return CookOutcome.Ok;
        }

        /// <summary>Drops a hold without a result (its player left, or the station changed under it).</summary>
        public void Cancel(int station, int kind)
        {
            if (_holds.Remove(KeyOf(station, kind))) Version++;
        }

        /// <summary>The seconds the server measured between two ticks at <paramref name="tickRate"/> (0 for a bad rate).</summary>
        public static double HeldSeconds(uint start, uint now, double tickRate) =>
            tickRate > 0d ? CookingTime.ElapsedTicks(start, now) / tickRate : 0d;

        /// <summary>The holds that reached their time by <paramref name="tick"/> (the auto-stop), into <paramref name="due"/> (cleared first, keys in order).</summary>
        public void DueHolds(uint tick, double tickRate, List<long> due)
        {
            due.Clear();
            foreach (KeyValuePair<long, Hold> kv in _holds)
                if (HeldSeconds(kv.Value.StartTick, tick, tickRate) >= CleaningRules.HoldSeconds(KindOfKey(kv.Key)) - 1e-4) due.Add(kv.Key);
            due.Sort();
        }

        /// <summary>The owner of hold <paramref name="key"/>, or -1.</summary>
        public int OwnerOf(long key) => _holds.TryGetValue(key, out Hold h) ? h.Owner : -1;

        /// <summary>Ends hold <paramref name="key"/> by the server (the auto-stop): true when there was one (the caller scrubs or wipes).</summary>
        public bool Complete(long key)
        {
            if (!_holds.Remove(key)) return false;
            Version++;
            return true;
        }

        /// <summary>Pure: every hold whose player is not <paramref name="present"/> any more is dropped without a result; their keys into <paramref name="dropped"/>.</summary>
        public void DropLeft(System.Func<int, bool> present, List<long> dropped)
        {
            dropped.Clear();
            if (present == null) return;
            foreach (KeyValuePair<long, Hold> kv in _holds)
                if (!present(kv.Value.Owner)) dropped.Add(kv.Key);
            foreach (long key in dropped) _holds.Remove(key);
            if (dropped.Count > 0) Version++;
        }

        // ---- the hygiene ----

        /// <summary>What is dirty now: the dishes (and how many are overdue at <paramref name="minute"/>), the trash, the spills and the smell.</summary>
        public HygieneSources Sources(long minute, int spills, bool burning)
        {
            IReadOnlyList<long> since = Dishes.DirtySince();
            int overdue = HygieneRules.OverdueDishes(since, minute);
            return new HygieneSources(since.Count, overdue, TrashIsFull || HasFullBag, spills, burning);
        }

        /// <summary>The meter moves to <paramref name="minute"/> with what is dirty now; true when the score changed.</summary>
        public bool Tick(long minute, int spills, bool burning)
        {
            int before = Meter.Percent;
            Meter = Meter.Advance(minute, Sources(minute, spills, burning));
            if (Meter.Percent == before) return false;
            Version++;
            return true;
        }
    }
}
