using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Net;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 035, server: the shop's cleaning. Owns the <see cref="CleaningRoom"/> (the plates, the trash, the timed holds, the hygiene
    /// meter) and answers the cleaning stations (KitchenService routes their RequestStation options here; no new RPC):
    /// - the dish return gives empty hands the oldest dirty dish (a DirtyDish pickup);
    /// - the sink takes the dirty dish in the hands; holding scrubs the oldest one in the basin (server-timed,
    ///   <see cref="CleaningRules.ScrubSeconds"/>; the re-press keeps the start, the server stops it at its time, a player who left is
    ///   dropped), and it goes back on the plate shelf clean;
    /// - the trash can takes what the hands hold (the kitchen removes it; no money or stock moves) or gives empty hands the bag; the bin
    ///   outside takes the bag;
    /// - the cloth held on a drink dispenser's spill wipes it (the same timed hold, <see cref="CleaningRules.WipeSeconds"/>).
    /// Every frame: the eat-in plates whose meal is over come back dirty, the meter moves with the economy's game minute (the injected
    /// clock), lost dishes and bags come back (dishes to the return, bags into the can), and the bytes go out (only changes). A new
    /// economy is a new game; a server stop keeps the game (carried dishes and bags come back). Lives next to the NetworkManager; does
    /// nothing on pure clients.
    /// </summary>
    public sealed class CleaningService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private PickupService _pickups;
        [SerializeField] private EconomyService _economy;
        [SerializeField] private ServeFeed _feed;
        [Tooltip("The shop's synced hygiene score (a global scene object).")]
        [SerializeField] private ShopHygiene _hygiene;
        [Tooltip("A dirty plate (a pickup with a DirtyDish); registered in DefaultPrefabObjects.")]
        [SerializeField] private NetworkObject _dirtyDishPrefab;
        [Tooltip("A full trash bag (a pickup with a TrashBag); registered in DefaultPrefabObjects.")]
        [SerializeField] private NetworkObject _trashBagPrefab;
        [Tooltip("The stations whose byte this service publishes.")]
        [SerializeField] private Station _plateShelf;
        [SerializeField] private Station _dishReturn;
        [SerializeField] private Station _sink;
        [SerializeField] private Station _trashCan;

        private const float LostCheckInterval = 0.5f;

        private readonly CleaningRoom _room = new CleaningRoom();
        private readonly Dictionary<long, PickupInteractor> _holders = new Dictionary<long, PickupInteractor>();
        private readonly Dictionary<int, NetworkObject> _objects = new Dictionary<int, NetworkObject>();
        private readonly List<long> _due = new List<long>();
        private readonly List<long> _dropped = new List<long>();
        private readonly List<int> _ids = new List<int>();
        private ICleaningKitchen _kitchen;
        private object _game;
        private float _nextLostCheck;
        private System.Func<int, bool> _present;
        private bool _tourTrash;

        /// <summary>Dev builds, the cook tour's host (-tour-orders): the trash starts one item short of full (one discard fills it).</summary>
        public const int TourTrashStart = Worktown.Sim.Economy.HygieneRules.TrashCapacity - 1;

        /// <summary>Server: the room (tests and the tour log).</summary>
        public CleaningRoom Room => _room;

        /// <summary>Server: the shop's hygiene score now (guests' patience and reviews).</summary>
        public int HygienePercent => _room.Hygiene;

        /// <summary>The kitchen binds itself (spills, the smell, discards, the cloth).</summary>
        public void Bind(ICleaningKitchen kitchen) => _kitchen = kitchen;

        private void Start()
        {
            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            _tourTrash = Worktown.Game.Npc.TourOrders.Requested(System.Environment.GetCommandLineArgs());
#endif
        }

        private void OnDestroy()
        {
            if (_networkManager != null && _networkManager.ServerManager != null)
                _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped || _networkManager.ServerManager.IsAnyServerStarted()) return;
            // The game goes on: FishNet despawned the dishes and bags; they come back (to the return, into the can).
            _room.EndSession(_ids);
            _holders.Clear();
            _objects.Clear();
        }

        private uint Tick => _networkManager.TimeManager.Tick;
        private double TickRate => _networkManager.TimeManager.TickRate;
        private long Minute => _economy != null ? _economy.GameMinute : (long)(Time.time * 60f / EconomyRules.SecondsPerHour);

        private void Update()
        {
            if (!_networkManager.IsServerStarted) return;
            long minute = Minute;
            object game = _economy != null ? _economy.Game : this;
            if (!ReferenceEquals(game, _game))
            {
                _game = game;
                _room.Reset(minute); // a new game: clean plates, an empty trash, 100 %
                if (_tourTrash)
                {
                    _room.StartTrashAt(TourTrashStart);
                    Debug.Log("[Cleaning] tour trash (-tour-orders): the trash starts at " + TourTrashStart);
                }
            }
            int back = _room.Dishes.ReturnDue(minute);
            if (back > 0) Debug.Log("[Cleaning] " + back + " dirty plate(s) came back to the dish return (" + _room.Dishes.AtReturn + " waiting)");
            StopDueHolds();
            DropLeftHolds();
            if (Time.unscaledTime >= _nextLostCheck)
            {
                _nextLostCheck = Time.unscaledTime + LostCheckInterval;
                WatchLost();
            }
            int spills = _kitchen != null ? _kitchen.SpillCount : 0;
            bool burning = _kitchen != null && _kitchen.Burning;
            if (_room.Tick(minute, spills, burning) && _room.Hygiene % 10 == 0) // the log every ten points, not every game minute
                Debug.Log("[Cleaning] hygiene " + _room.Hygiene + " % (" + _room.Sources(minute, spills, burning) + ")");
            Publish();
        }

        /// <summary>Sends the bytes (each only sends a change).</summary>
        private void Publish()
        {
            SetByte(_plateShelf, CleaningRules.CountByte(_room.Dishes.Clean));
            SetByte(_dishReturn, CleaningRules.CountByte(_room.Dishes.AtReturn));
            SetByte(_sink, CleaningRules.SinkByte(_room.Dishes.InBasin, _sink != null && _room.IsHeld(_sink.Index, CleaningRules.HoldScrub)));
            SetByte(_trashCan, CleaningRules.CountByte(_room.TrashFill));
            if (_hygiene != null) _hygiene.ServerSet(_room.Hygiene);
        }

        private static void SetByte(Station station, byte value)
        {
            if (station == null || !station.TryGetComponent(out StationContents contents) || !contents.IsServerInitialized) return;
            contents.ServerSetStoveFlags(value);
        }

        // ---- plates (KitchenService) ----

        /// <summary>A clean plate leaves the shelf for the hands: NoCleanPlates when every plate is in use or dirty.</summary>
        public CookOutcome TakeCleanPlate()
        {
            CookOutcome took = _room.Dishes.TakeClean();
            Publish();
            return took;
        }

        /// <summary>The backstop: the plate never reached the hands.</summary>
        public void UndoTakeCleanPlate()
        {
            _room.Dishes.UndoTakeClean();
            Publish();
        }

        /// <summary>A plate left with its meal: an eat-in guest brings it back dirty after eating; anything else is clean again at once.</summary>
        public void PlateServed(bool eatIn)
        {
            _room.Dishes.PlateServed(eatIn, Minute);
            Publish();
        }

        /// <summary>A clean plate fell out of the world: it turns up on the shelf again.</summary>
        public void PlateLost()
        {
            _room.Dishes.PlateLost();
            Publish();
        }

        /// <summary>Whether <paramref name="nob"/> is a dirty dish (never food, never cookware).</summary>
        public static bool IsDirtyDish(NetworkObject nob) => nob != null && nob.TryGetComponent(out DirtyDish _);

        public static bool IsTrashBag(NetworkObject nob) => nob != null && nob.TryGetComponent(out TrashBag _);

        // ---- the stations ----

        /// <summary>
        /// Spec 035: one request at a cleaning station (reach was checked by the kitchen, but for a scrub's release): the dish return,
        /// the sink (put a dish in, the scrub's press and release), the trash can, the bin.
        /// </summary>
        public CookOutcome Use(PickupInteractor player, Station station, int option)
        {
            if (player == null || station == null) return CookOutcome.UnknownStation;
            CookOutcome outcome;
            switch (station.Kind)
            {
                case StationKind.DishReturn:
                    outcome = option == CleaningRules.TakeDirtyOption ? TakeDirty(player) : CookOutcome.InvalidOption;
                    break;
                case StationKind.Sink:
                    outcome = option == CleaningRules.SinkPutOption ? PutInSink(player)
                        : option == CleaningRules.ScrubStartOption ? StartScrub(player, station)
                        : option == CleaningRules.ScrubStopOption ? StopScrub(player, station)
                        : CookOutcome.InvalidOption;
                    break;
                case StationKind.TrashCan:
                    outcome = option == CleaningRules.TrashOption ? UseTrash(player) : CookOutcome.InvalidOption;
                    break;
                case StationKind.Bin:
                    outcome = option == CleaningRules.BinOption ? Bin(player) : CookOutcome.InvalidOption;
                    break;
                default:
                    outcome = CookOutcome.UnknownStation;
                    break;
            }
            Publish();
            return outcome;
        }

        private CookOutcome TakeDirty(PickupInteractor player)
        {
            bool holding = _pickups.TryGetHeld(player, out _);
            CookOutcome can = CleaningRules.CanTakeDirty(holding, _room.Dishes.AtReturn);
            if (can != CookOutcome.Ok) return can;
            if (_dirtyDishPrefab == null || !_pickups.CanHandTo(player, _dirtyDishPrefab)) return CookOutcome.UnknownStation;
            CookOutcome took = _room.Dishes.TakeDirty(out long since);
            if (took != CookOutcome.Ok) return took;
            NetworkObject dish = SpawnInHands(player, _dirtyDishPrefab);
            if (dish == null)
            {
                _room.Dishes.UndoTakeDirty(since); // the backstop: the dish stays at the return
                return CookOutcome.SimRefused;
            }
            _room.Dishes.ConfirmCarried(dish.ObjectId, since);
            Debug.Log("[Cleaning] player " + player.OwnerId + " took a dirty plate from the dish return (" + _room.Dishes.AtReturn + " left)");
            return CookOutcome.Ok;
        }

        private CookOutcome PutInSink(PickupInteractor player)
        {
            if (!_pickups.TryGetHeld(player, out NetworkObject held) || !IsDirtyDish(held)) return CookOutcome.NotDirty;
            CookOutcome put = _room.Dishes.PutInSink(held.ObjectId);
            if (put != CookOutcome.Ok) return put;
            _objects.Remove(held.ObjectId);
            _pickups.TakeAway(player, out _);
            _networkManager.ServerManager.Despawn(held);
            Debug.Log("[Cleaning] player " + player.OwnerId + " put a dirty plate in the sink (" + _room.Dishes.InBasin + " in it)");
            return CookOutcome.Ok;
        }

        private CookOutcome StartScrub(PickupInteractor player, Station sink)
        {
            bool busy = _room.IsHeld(sink.Index, CleaningRules.HoldScrub, out int owner, out _) && owner != player.OwnerId;
            bool mine = _room.IsHeld(sink.Index, CleaningRules.HoldScrub, out owner, out _) && owner == player.OwnerId;
            if (!mine)
            {
                CookOutcome can = CleaningRules.CanScrub(_pickups.TryGetHeld(player, out _), _room.Dishes.InBasin, busy);
                if (can != CookOutcome.Ok) return can;
            }
            CookOutcome pressed = _room.Press(sink.Index, CleaningRules.HoldScrub, player.OwnerId, Tick); // 031b: a re-press keeps its start
            if (pressed == CookOutcome.Ok) _holders[CleaningRoom.KeyOf(sink.Index, CleaningRules.HoldScrub)] = player;
            return pressed;
        }

        private CookOutcome StopScrub(PickupInteractor player, Station sink)
        {
            long key = CleaningRoom.KeyOf(sink.Index, CleaningRules.HoldScrub);
            CookOutcome released = _room.Release(sink.Index, CleaningRules.HoldScrub, player.OwnerId, Tick, TickRate, out bool done);
            if (_room.OwnerOf(key) < 0) _holders.Remove(key);
            if (released == CookOutcome.Ok && done) FinishScrub(player);
            return released;
        }

        /// <summary>
        /// 035 review: a scrub counts only for a scrubber still there with empty hands (like the wipe's cloth check): a player who left or
        /// picked something up meanwhile scrubs nothing.
        /// </summary>
        public static bool ScrubCounts(bool present, bool holding) => present && !holding;

        /// <summary>A wipe counts only for a wiper still there with the cloth in the hands (put down meanwhile: no wipe).</summary>
        public static bool WipeCounts(bool present, bool holdsCloth) => present && holdsCloth;

        private void FinishScrub(PickupInteractor player)
        {
            bool present = player != null && player.IsSpawned;
            if (!ScrubCounts(present, present && _pickups.TryGetHeld(player, out _))) return;
            int owner = player.OwnerId;
            if (_room.Dishes.Scrubbed() != CookOutcome.Ok) return;
            Debug.Log("[Cleaning] player " + owner + " scrubbed a plate clean (" + _room.Dishes.Clean + " clean on the shelf, "
                + _room.Dishes.InBasin + " left in the sink)");
        }

        private CookOutcome UseTrash(PickupInteractor player)
        {
            if (!_pickups.TryGetHeld(player, out NetworkObject held))
            {
                CookOutcome empty = CleaningRules.CanTakeBag(false, _room.TrashFill);
                if (empty != CookOutcome.Ok) return empty;
                if (_trashBagPrefab == null || !_pickups.CanHandTo(player, _trashBagPrefab)) return CookOutcome.UnknownStation;
                CookOutcome took = _room.TakeBag(false, out int size);
                if (took != CookOutcome.Ok) return took;
                NetworkObject bag = SpawnInHands(player, _trashBagPrefab);
                if (bag == null)
                {
                    _room.UndoTakeBag(size);
                    return CookOutcome.SimRefused;
                }
                _room.ConfirmBag(bag.ObjectId, size);
                Debug.Log("[Cleaning] player " + player.OwnerId + " took the trash bag out of the can (" + size + " items)");
                return CookOutcome.Ok;
            }
            if (IsDirtyDish(held) || IsTrashBag(held)) return CookOutcome.CannotDiscard; // wash the dish; the bag goes to the bin outside
            CookOutcome can = _room.CanDiscard(_kitchen != null && _kitchen.CanDiscardHeld(player));
            if (can != CookOutcome.Ok) return can;
            CookOutcome gone = _kitchen.DiscardHeld(player, out string what);
            if (gone != CookOutcome.Ok) return gone;
            _room.Discarded();
            Debug.Log("[Cleaning] player " + player.OwnerId + " threw " + what + " away (trash " + _room.TrashFill + "/"
                + Worktown.Sim.Economy.HygieneRules.TrashCapacity + ")");
            return CookOutcome.Ok;
        }

        private CookOutcome Bin(PickupInteractor player)
        {
            if (!_pickups.TryGetHeld(player, out NetworkObject held) || !IsTrashBag(held)) return CookOutcome.NotABag;
            CookOutcome binned = _room.Binned(held.ObjectId);
            if (binned != CookOutcome.Ok) return binned;
            _objects.Remove(held.ObjectId);
            _pickups.TakeAway(player, out _);
            _networkManager.ServerManager.Despawn(held);
            Debug.Log("[Cleaning] player " + player.OwnerId + " took the trash out");
            if (_feed != null) _feed.ServerAdd("Player " + player.OwnerId + " took the trash out");
            return CookOutcome.Ok;
        }

        // ---- the wipe (KitchenService routes the drink dispenser's wipe options) ----

        /// <summary>Whether someone wipes dispenser <paramref name="station"/> (the dispenser byte's wiping bit, for onlookers).</summary>
        public bool IsWiping(int station) => _room.IsHeld(station, CleaningRules.HoldWipe);

        /// <summary>
        /// Spec 035: the cloth held on the spill of drink dispenser <paramref name="dispenser"/>: the press (the cloth in the hands, a spill
        /// there; reach was checked) and the release (anywhere). A wipe held <see cref="CleaningRules.WipeSeconds"/> clears the spill.
        /// </summary>
        public CookOutcome Wipe(PickupInteractor player, Station dispenser, int option)
        {
            if (player == null || dispenser == null || _kitchen == null) return CookOutcome.UnknownStation;
            long key = CleaningRoom.KeyOf(dispenser.Index, CleaningRules.HoldWipe);
            CookOutcome outcome;
            if (option == CleaningRules.WipeStartOption)
            {
                bool mine = _room.OwnerOf(key) == player.OwnerId;
                outcome = mine ? CookOutcome.Ok
                    : CleaningRules.CanWipe(_kitchen.HoldsCloth(player), _kitchen.HasSpill(dispenser.Index), _room.OwnerOf(key) >= 0);
                if (outcome == CookOutcome.Ok) outcome = _room.Press(dispenser.Index, CleaningRules.HoldWipe, player.OwnerId, Tick);
                if (outcome == CookOutcome.Ok) _holders[key] = player;
            }
            else if (option == CleaningRules.WipeStopOption)
            {
                outcome = _room.Release(dispenser.Index, CleaningRules.HoldWipe, player.OwnerId, Tick, TickRate, out bool done);
                if (_room.OwnerOf(key) < 0) _holders.Remove(key);
                if (outcome == CookOutcome.Ok && done) FinishWipe(player, dispenser.Index);
            }
            else outcome = CookOutcome.InvalidOption;
            _kitchen.RefreshDispenser(dispenser.Index);
            return outcome;
        }

        private void FinishWipe(PickupInteractor player, int station)
        {
            if (!WipeCounts(player != null && player.IsSpawned, player != null && _kitchen.HoldsCloth(player))) return; // the cloth was put down: no wipe
            if (_kitchen.ClearSpill(station)) Debug.Log("[Cleaning] player " + player.OwnerId + " wiped the spill at the drink dispenser");
        }

        // ---- the server's timers ----

        /// <summary>The auto-stop: a scrub or wipe held its time ends by itself (the dish is clean, the spill gone).</summary>
        private void StopDueHolds()
        {
            _room.DueHolds(Tick, TickRate, _due);
            foreach (long key in _due)
            {
                _holders.TryGetValue(key, out PickupInteractor player);
                if (!_room.Complete(key)) continue;
                _holders.Remove(key);
                int station = CleaningRoom.StationOfKey(key);
                if (CleaningRoom.KindOfKey(key) == CleaningRules.HoldScrub) FinishScrub(player); // 035 review: re-checks the scrubber
                else
                {
                    FinishWipe(player, station);
                    _kitchen?.RefreshDispenser(station);
                }
            }
        }

        /// <summary>A player who left (despawned, destroyed) is freed: its hold is dropped without a result.</summary>
        private void DropLeftHolds()
        {
            if (_holders.Count == 0) return;
            _present ??= IsPresent;
            _room.DropLeft(_present, _dropped);
            foreach (long key in _dropped)
            {
                _holders.Remove(key);
                if (CleaningRoom.KindOfKey(key) == CleaningRules.HoldWipe) _kitchen?.RefreshDispenser(CleaningRoom.StationOfKey(key));
                Debug.Log("[Cleaning] a hold at station " + CleaningRoom.StationOfKey(key) + " was dropped: its player left");
            }
        }

        private bool IsPresent(int owner)
        {
            foreach (KeyValuePair<long, PickupInteractor> kv in _holders)
                if (kv.Value != null && kv.Value.IsSpawned && kv.Value.OwnerId == owner) return true;
            return false;
        }

        /// <summary>A dirty dish or a bag below the world or gone without its step: the dish back to the return, the bag into the can.</summary>
        private void WatchLost()
        {
            _room.Dishes.CarriedIds(_ids);
            foreach (int id in _ids)
                if (IsLost(id))
                {
                    _room.Dishes.CarriedLost(id);
                    Debug.Log("[Cleaning] a dirty plate was lost; it waits at the dish return again");
                }
            _room.BagIds(_ids);
            foreach (int id in _ids)
                if (IsLost(id))
                {
                    _room.BagLost(id);
                    Debug.Log("[Cleaning] a trash bag was lost; its trash is back in the can");
                }
        }

        private bool IsLost(int id)
        {
            _objects.TryGetValue(id, out NetworkObject nob);
            bool exists = nob != null && nob.IsSpawned && nob.ObjectId == id;
            if (!VesselRules.IsLost(exists, exists ? nob.transform.position.y : 0f)) return false;
            _objects.Remove(id);
            if (exists) _networkManager.ServerManager.Despawn(nob);
            return true;
        }

        private NetworkObject SpawnInHands(PickupInteractor player, NetworkObject prefab)
        {
            NetworkObject nob = _networkManager.GetPooledInstantiated(prefab, true);
            nob.transform.SetPositionAndRotation(player.HoldPoint.position, player.HoldPoint.rotation);
            _networkManager.ServerManager.Spawn(nob);
            if (_pickups.HandTo(player, nob))
            {
                _objects[nob.ObjectId] = nob;
                return nob;
            }
            Debug.LogError("[Cleaning] could not hand " + prefab.name + " to player " + player.OwnerId);
            _networkManager.ServerManager.Despawn(nob);
            return null;
        }
    }
}
