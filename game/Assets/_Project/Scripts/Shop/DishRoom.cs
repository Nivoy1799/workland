using System.Collections.Generic;
using Worktown.Game.Cooking;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 035, server, pure: where the shop's <see cref="CleaningRules.TotalPlates"/> plates are. Clean on the plate shelf
    /// (<see cref="Clean"/>), in use as cookware (KitchenService counts those), with an eat-in guest (<see cref="Eating"/>, back dirty
    /// <see cref="CleaningRules.EatMinutes"/> later), dirty at the dish return, dirty in someone's hands or lying about (by object id),
    /// or dirty in the sink's basin. Every dirty dish keeps the game minute it got dirty (the hygiene grace). Every step checks first and
    /// changes nothing on a refusal; the hand-over steps have an undo (the refund backstop). Nothing here creates or destroys a plate:
    /// <c>Clean + in use + Eating + AtReturn + Carried + InBasin == TotalPlates</c> (tested).
    /// </summary>
    public sealed class DishRoom
    {
        private readonly List<long> _eatingDue = new List<long>();
        private readonly List<long> _atReturn = new List<long>();
        private readonly Dictionary<int, long> _carried = new Dictionary<int, long>();
        private readonly List<long> _basin = new List<long>();
        private readonly List<long> _dirtySince = new List<long>();

        public DishRoom(int clean = CleaningRules.TotalPlates - CleaningRules.ScenePlates)
        {
            Clean = clean < 0 ? 0 : clean;
        }

        public int Clean { get; private set; }
        public int Eating => _eatingDue.Count;
        public int AtReturn => _atReturn.Count;
        public int Carried => _carried.Count;
        public int InBasin => _basin.Count;
        /// <summary>Every dirty dish (at the return, carried or lying about, in the basin).</summary>
        public int Dirty => _atReturn.Count + _carried.Count + _basin.Count;
        /// <summary>Counts every change (the server republishes the bytes).</summary>
        public int Version { get; private set; }

        /// <summary>A new game: <paramref name="clean"/> on the shelf, nothing dirty or eaten.</summary>
        public void Reset(int clean = CleaningRules.TotalPlates - CleaningRules.ScenePlates)
        {
            Clean = clean < 0 ? 0 : clean;
            _eatingDue.Clear();
            _atReturn.Clear();
            _carried.Clear();
            _basin.Clear();
            Version++;
        }

        /// <summary>
        /// 035 review: the server restarted. FishNet despawned every plate it spawned (those from the shelf, for good) and set the scene's
        /// <see cref="CleaningRules.ScenePlates"/> plates up again on the plating worktop, whatever happened to them. So after the restart
        /// exactly those are in use; the carried dirty dishes are back at the return already (<see cref="CarriedLost"/>). The shelf gets
        /// the rest: <c>Clean = TotalPlates - ScenePlates - Eating - AtReturn - InBasin</c>. Should more be dirty or eaten than that leaves
        /// room for, the newest go (eating first, then the return, then the basin), so the shop still owns exactly
        /// <see cref="CleaningRules.TotalPlates"/>.
        /// </summary>
        public void Restarted()
        {
            int room = CleaningRules.TotalPlates - CleaningRules.ScenePlates;
            int excess = _eatingDue.Count + _atReturn.Count + _basin.Count - room;
            excess = DropNewest(_eatingDue, excess);
            excess = DropNewest(_atReturn, excess);
            DropNewest(_basin, excess);
            Clean = room - _eatingDue.Count - _atReturn.Count - _basin.Count;
            Version++;
        }

        private static int DropNewest(List<long> list, int excess)
        {
            while (excess > 0 && list.Count > 0)
            {
                list.RemoveAt(list.Count - 1);
                excess--;
            }
            return excess;
        }

        // ---- clean plates ----

        /// <summary>A clean plate leaves the shelf (into the hands): NoCleanPlates when none is left.</summary>
        public CookOutcome TakeClean()
        {
            if (Clean <= 0) return CookOutcome.NoCleanPlates;
            Clean--;
            Version++;
            return CookOutcome.Ok;
        }

        /// <summary>The backstop: a plate taken but never handed over goes back on the shelf.</summary>
        public void UndoTakeClean()
        {
            Clean++;
            Version++;
        }

        /// <summary>
        /// A plate left the kitchen with its meal at game minute <paramref name="minute"/>: with an eat-in guest it comes back dirty after
        /// the meal; otherwise (take-away: the meal went into its parcel; a sale to the wholesaler) it is clean again at once.
        /// </summary>
        public void PlateServed(bool eatIn, long minute)
        {
            if (eatIn) _eatingDue.Add(minute + CleaningRules.EatMinutes);
            else Clean++;
            Version++;
        }

        /// <summary>A clean plate fell out of the world: it turns up on the shelf again (never lost, never doubled).</summary>
        public void PlateLost()
        {
            Clean++;
            Version++;
        }

        /// <summary>The eat-in plates whose meal is over by <paramref name="minute"/> come back to the dish return, dirty since then. Returns how many.</summary>
        public int ReturnDue(long minute)
        {
            int n = 0;
            for (int i = 0; i < _eatingDue.Count;)
            {
                if (_eatingDue[i] > minute)
                {
                    i++;
                    continue;
                }
                _atReturn.Add(_eatingDue[i]);
                _eatingDue.RemoveAt(i);
                n++;
            }
            if (n > 0) Version++;
            return n;
        }

        // ---- dirty dishes ----

        /// <summary>The oldest dirty dish leaves the dish return (into the hands): NoDirtyDishes when none waits.</summary>
        public CookOutcome TakeDirty(out long dirtySince)
        {
            dirtySince = 0;
            if (_atReturn.Count == 0) return CookOutcome.NoDirtyDishes;
            dirtySince = _atReturn[0];
            _atReturn.RemoveAt(0);
            Version++;
            return CookOutcome.Ok;
        }

        /// <summary>The dish from <see cref="TakeDirty"/> is object <paramref name="objectId"/> now (in the hands).</summary>
        public void ConfirmCarried(int objectId, long dirtySince)
        {
            _carried[objectId] = dirtySince;
            Version++;
        }

        /// <summary>The backstop: a dirty dish taken but never handed over goes back to the return (first in line).</summary>
        public void UndoTakeDirty(long dirtySince)
        {
            _atReturn.Insert(0, dirtySince);
            Version++;
        }

        public bool IsCarried(int objectId) => _carried.ContainsKey(objectId);

        /// <summary>The carried dirty dish <paramref name="objectId"/> goes into the basin: NotDirty for no such dish, SinkFull.</summary>
        public CookOutcome PutInSink(int objectId)
        {
            if (!_carried.TryGetValue(objectId, out long since)) return CookOutcome.NotDirty;
            if (_basin.Count >= CleaningRules.BasinCapacity) return CookOutcome.SinkFull;
            _carried.Remove(objectId);
            _basin.Add(since);
            Version++;
            return CookOutcome.Ok;
        }

        /// <summary>A carried dirty dish was lost (below the world, or its object went): it waits at the return again, as dirty as before.</summary>
        public bool CarriedLost(int objectId)
        {
            if (!_carried.TryGetValue(objectId, out long since)) return false;
            _carried.Remove(objectId);
            _atReturn.Insert(0, since);
            Version++;
            return true;
        }

        /// <summary>The carried dirty dishes' object ids, into <paramref name="ids"/> (cleared first, sorted).</summary>
        public void CarriedIds(List<int> ids)
        {
            if (ids == null) return;
            ids.Clear();
            foreach (int id in _carried.Keys) ids.Add(id);
            ids.Sort();
        }

        /// <summary>A scrub finished: the oldest dish in the basin is clean and goes on the shelf. SinkEmpty when there is none.</summary>
        public CookOutcome Scrubbed()
        {
            if (_basin.Count == 0) return CookOutcome.SinkEmpty;
            _basin.RemoveAt(0);
            Clean++;
            Version++;
            return CookOutcome.Ok;
        }

        /// <summary>The game minutes every dirty dish got dirty (for the hygiene grace); the list is reused.</summary>
        public IReadOnlyList<long> DirtySince()
        {
            _dirtySince.Clear();
            _dirtySince.AddRange(_atReturn);
            foreach (long since in _carried.Values) _dirtySince.Add(since);
            _dirtySince.AddRange(_basin);
            return _dirtySince;
        }
    }
}
