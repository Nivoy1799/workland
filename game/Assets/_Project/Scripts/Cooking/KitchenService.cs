using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;
using Worktown.Game.Economy;
using Worktown.Game.Net;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Server-side authority for the kitchen (spec 007). Validates the target, the distance and what the player holds,
    /// applies the sim operations through KitchenState, and publishes the results on the Food and Vessel objects.
    /// Spec 021: the board, pan, pot, plates and bowl are carried cookware (<see cref="Vessel"/>); clients send "use
    /// what I hold on that vessel" (RequestUse) and the server decides: put food in, pour, take out, cut. Pans and pots
    /// cook only while they stand on a burner that is on (PickupService reports every move). Lost cookware comes back
    /// at its home spot like the knife; plates come from the shelf (at most <see cref="VesselRules.MaxPlatesInWorld"/>).
    /// Lives next to the NetworkManager; does nothing on pure clients.
    /// </summary>
    public sealed class KitchenService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private PickupService _pickups;
        [SerializeField] private NetworkObject _foodPrefab;
        [SerializeField] private ServeFeed _feed;
        [Tooltip("Spec 009. Without it, crates are free and the counter only serves.")]
        [SerializeField] private EconomyService _economy;
        [Tooltip("Spec 014. Optional: visible customers react to sales.")]
        [SerializeField] private Worktown.Game.Npc.CustomerService _customers;
        [Tooltip("Index in this array = Station.Index.")]
        [SerializeField] private Station[] _stations = new Station[0];
        [Tooltip("Spec 019: knife prefab for replacing a lost knife (the scene knife is an instance of it).")]
        [SerializeField] private NetworkObject _knifePrefab;
        [Tooltip("Spec 021: one prefab per kind of cookware (board, pan, pot, plate, bowl), for respawns and the plate shelf.")]
        [SerializeField] private NetworkObject[] _vesselPrefabs = new NetworkObject[0];
        [Tooltip("Spec 030: spatula prefab for replacing a lost spatula (the scene spatula is an instance of it).")]
        [SerializeField] private NetworkObject _spatulaPrefab;

        private const float HomeCheckInterval = 0.5f;
        private const int MaxInitAttempts = 300;

        private readonly KitchenState _kitchen = KitchenState.CreateDemo();
        // Server only, built on the first server frame: every spawned piece of cookware, the knife and the cookware
        // with a home spot, and which place surface is burner b of stove s.
        private List<Vessel> _vessels;
        private List<HomeSlot> _homes;
        private readonly Dictionary<long, int> _burnerSurface = new Dictionary<long, int>();
        private float _nextHomeCheck;
        private bool _initialized;
        private int _initAttempts;
        private bool _labelsDirty = true;
        private uint _lastLabelTick;
        // Spec 027c: the heat bytes' rate gate per pan and pot, and the tick they were last looked at.
        private readonly Dictionary<Vessel, HeatSync> _heatSync = new Dictionary<Vessel, HeatSync>();
        // Spec 029: the fryer baskets (key: fryer station index and basket), found on the first server frame.
        private readonly Dictionary<long, Vessel> _baskets = new Dictionary<long, Vessel>();
        private readonly List<Vessel> _basketList = new List<Vessel>();
        // Spec 030: the griddle spots (key: griddle station index and spot), found on the first server frame.
        private readonly Dictionary<long, Vessel> _griddleSpots = new Dictionary<long, Vessel>();
        // Spec 031: the build spot of each assembly board (key: the board's station index), and who squeezes which dispenser.
        private readonly Dictionary<int, Vessel> _assemblySpots = new Dictionary<int, Vessel>();
        private readonly Dictionary<long, PickupInteractor> _squeezers = new Dictionary<long, PickupInteractor>();
        private readonly List<long> _leftSqueezes = new List<long>(); // 031b
        private AssemblyDesk _deskField; // 031b: the board's charging steps (pure, tested)
        private AssemblyDesk Desk => _deskField ??= new AssemblyDesk(_kitchen);
        // Spec 033: who pours which tap of which drink dispenser (key: DrinkDesk.KeyOf), and the dispenser's steps (pure, tested).
        private readonly Dictionary<long, PickupInteractor> _pourers = new Dictionary<long, PickupInteractor>();
        private readonly List<long> _leftPours = new List<long>();
        private DrinkDesk _drinkDeskField;
        private DrinkDesk DrinkSteps => _drinkDeskField ??= new DrinkDesk(_kitchen);
        private uint _lastHeatTick;
        // 030 review: the cooked-ticks stamp of each heat vessel at its last periodic label refresh.
        private readonly Dictionary<Vessel, long> _labelStamps = new Dictionary<Vessel, long>();
        private bool _heatChecked;

        private sealed class HomeSlot
        {
            public NetworkObject Prefab;
            public Vector3 Position;
            public Quaternion Rotation;
            public NetworkObject Item;
            public float LostSince = -1f;
        }

        // Start, not OnEnable: NetworkManager creates its ServerManager in its own Awake.
        private void Start()
        {
            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            if (_pickups != null) _pickups.ItemMoved += OnItemMoved;
        }

        private void OnDestroy()
        {
            if (_pickups != null) _pickups.ItemMoved -= OnItemMoved;
            if (_networkManager != null && _networkManager.ServerManager != null)
                _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        }

        private void OnServerConnectionState(FishNet.Transporting.ServerConnectionStateArgs args)
        {
            // FishNet despawns everything and reuses object ids on a restart in the same process;
            // old entries must not attach to new objects (review finding, spec 007).
            if (args.ConnectionState == FishNet.Transporting.LocalConnectionState.Stopped
                && !_networkManager.ServerManager.IsAnyServerStarted())
            {
                _kitchen.Clear();
                _labelsDirty = true;
                _vessels = null; // spec 021: rebuilt from the scene on the next start
                _homes = null;
                _initialized = false;
                _initAttempts = 0;
                _burnerSurface.Clear();
                _heatSync.Clear();
                _labelStamps.Clear();
                _heatChecked = false;
                _baskets.Clear();
                _basketList.Clear();
                _griddleSpots.Clear();
                _assemblySpots.Clear(); // spec 031
                _squeezers.Clear();
                _pourers.Clear(); // spec 033
            }
        }

        private uint Tick => _networkManager.TimeManager.Tick;
        private double TickRate => _networkManager.TimeManager.TickRate;

        // ---- stations: stock (fridge, shelf, crates), counter, stoves, plate shelf (RequestStation) ----

        public CookOutcome TryUseStation(PickupInteractor player, int stationIndex, int option)
        {
            if (stationIndex < 0 || stationIndex >= _stations.Length || _stations[stationIndex] == null)
                return CookOutcome.UnknownStation;
            Station station = _stations[stationIndex];
            // Spec 028b: stock checks its option (the slot) and the reach to that slot's anchor itself (StockRules).
            if (station.Kind == StationKind.Crate || StockLayout.IsStock(station.Kind)) return TakeIngredient(player, station, option);
            // Spec 031: a dispenser's release ends the squeeze wherever the player is now (the press was in reach).
            if (!AssemblyDesk.NeedsReach(station.Kind, option))
            {
                EnsureInitialized();
                return UseAssembly(player, station, option);
            }
            // Spec 033: a tap's release ends the pour wherever the player is now (the press was in reach).
            if (!DrinkDesk.NeedsReach(station.Kind, option))
            {
                EnsureInitialized();
                return UseDrinks(player, station, option);
            }
            if (!StationRules.IsInReach(player.transform.position, station.transform.position))
                return CookOutcome.TooFar;
            EnsureInitialized();

            switch (station.Kind)
            {
                case StationKind.Counter: return Serve(player, station);
                case StationKind.Stove: return UseBurner(station, option);
                case StationKind.Fryer: return UseFryer(station, option);
                case StationKind.Griddle: return UseGriddle(station, option); // spec 030
                case StationKind.Assembly: return UseAssembly(player, station, option); // spec 031
                case StationKind.DrinkDispenser: return UseDrinks(player, station, option); // spec 033
                case StationKind.PlateShelf: return TakePlate(player);
                // Spec 032: the register's slots (the change tray, take back, hand over, the card terminal); the guests' service decides.
                case StationKind.Register: return _customers != null ? _customers.UseRegister(player, option) : CookOutcome.NoPayment;
                default: return CookOutcome.UnknownStation;
            }
        }

        /// <summary>Spec 021: a burner knob (on/off) or button (fry/saute) on the stove's front.</summary>
        private CookOutcome UseBurner(Station stove, int option)
        {
            if (!StationRules.TryParseStoveOption(option, out int burner, out int button)) return CookOutcome.InvalidOption;
            if (!_burnerSurface.TryGetValue(BurnerKey(stove.Index, burner), out int burnerId)) return CookOutcome.InvalidOption;
            CookOutcome changed = _kitchen.TryUseBurner(burnerId, button, Tick);
            if (changed == CookOutcome.Ok)
            {
                RefreshStove(stove);
                foreach (Vessel vessel in _vessels)
                    if (vessel != null && _kitchen.BurnerUnder(vessel.ObjectId) == burnerId) RefreshVessel(vessel);
                Debug.Log("[Kitchen] " + stove.DisplayName + " burner " + burner + ": " + _kitchen.BurnerOf(burnerId).Describe());
            }
            return changed;
        }

        /// <summary>
        /// Spec 029: the fryer's front: the power knob (FryerRules.PowerOption) or a basket's button, sent as the intent
        /// FryerRules.LowerOption / RaiseOption (spec 029b). Lowering needs food in the basket and the fryer on; raising works
        /// for any basket in the oil.
        /// </summary>
        private CookOutcome UseFryer(Station fryer, int option)
        {
            uint tick = Tick;
            if (option == FryerRules.PowerOption)
            {
                _kitchen.TryToggleFryer(fryer.Index, tick);
                RefreshFryer(fryer);
                for (int b = 0; b < FryerRules.BasketsPerFryer; b++)
                    if (_baskets.TryGetValue(BurnerKey(fryer.Index, b), out Vessel v)) RefreshVessel(v);
                Debug.Log("[Kitchen] " + fryer.DisplayName + " " + (_kitchen.FryerIsOn(fryer.Index) ? "on" : "off"));
                return CookOutcome.Ok;
            }
            if (!FryerRules.TryParseBasketOption(option, out int basket, out bool lower)
                || !_baskets.TryGetValue(BurnerKey(fryer.Index, basket), out Vessel vessel) || vessel == null)
                return CookOutcome.InvalidOption;
            // Spec 029b: the option says lower or raise; a request for where the basket already is is refused.
            CookOutcome moved = lower ? _kitchen.TryLowerBasket(vessel.ObjectId, tick) : _kitchen.TryRaiseBasket(vessel.ObjectId, tick, TickRate);
            if (moved != CookOutcome.Ok) return moved;
            RefreshVessel(vessel);
            RefreshFryer(fryer);
            Debug.Log("[Kitchen] " + fryer.DisplayName + " basket " + basket + ": " + _kitchen.BasketStateOf(vessel.ObjectId, tick));
            return CookOutcome.Ok;
        }

        /// <summary>Spec 029: the fryer's light byte (on, lowered baskets).</summary>
        private void RefreshFryer(Station fryer)
        {
            if (fryer == null || !fryer.TryGetComponent(out StationContents contents) || !contents.IsServerInitialized) return;
            contents.ServerSetStoveFlags(_kitchen.FryerFlags(fryer.Index, Tick));
        }

        /// <summary>Spec 029: a shake of a lowered basket, with empty hands.</summary>
        private CookOutcome Shake(PickupInteractor player, Vessel basket)
        {
            CookOutcome can = VesselRules.CheckShake(basket.Kind, _pickups.TryGetHeld(player, out _));
            if (can != CookOutcome.Ok) return can;
            CookOutcome shaken = _kitchen.TryShake(basket.ObjectId, Tick, TickRate);
            if (shaken != CookOutcome.Ok) return shaken;
            RefreshVessel(basket);
            Debug.Log("[Kitchen] player " + player.OwnerId + " shook the basket" + (_kitchen.BasketShaken(basket.ObjectId) ? " (even fry)" : string.Empty));
            return CookOutcome.Ok;
        }

        /// <summary>Spec 030: the griddle's power knob (GriddleRules.PowerOption); the spots' down sides cook while it is on.</summary>
        private CookOutcome UseGriddle(Station griddle, int option)
        {
            if (option != GriddleRules.PowerOption) return CookOutcome.InvalidOption;
            _kitchen.TryToggleGriddle(griddle.Index, Tick);
            RefreshGriddle(griddle);
            for (int s = 0; s < GriddleRules.SpotsPerGriddle; s++)
                if (_griddleSpots.TryGetValue(BurnerKey(griddle.Index, s), out Vessel spot)) RefreshVessel(spot);
            Debug.Log("[Kitchen] " + griddle.DisplayName + " " + (_kitchen.GriddleIsOn(griddle.Index) ? "on" : "off"));
            return CookOutcome.Ok;
        }

        /// <summary>Spec 030: the griddle's light byte (on).</summary>
        private void RefreshGriddle(Station griddle)
        {
            if (griddle == null || !griddle.TryGetComponent(out StationContents contents) || !contents.IsServerInitialized) return;
            contents.ServerSetStoveFlags(_kitchen.GriddleFlags(griddle.Index));
        }

        /// <summary>
        /// Spec 030: a flip of the item on a griddle spot: the spatula must be in the hands (NoSpatula, "Use the spatula"; reach
        /// was checked by the caller), food on the spot (StationEmpty). The new down side's heat bytes go out at once with the
        /// mode byte, so the two ring halves never swap for a moment.
        /// </summary>
        private CookOutcome Flip(PickupInteractor player, Vessel spot)
        {
            bool spatula = _pickups.TryGetHeld(player, out NetworkObject tool) && tool.TryGetComponent(out Spatula _);
            CookOutcome can = VesselRules.CheckFlip(spot.Kind, spatula);
            if (can != CookOutcome.Ok) return can;
            CookOutcome flipped = _kitchen.TryFlip(spot.ObjectId, Tick);
            if (flipped != CookOutcome.Ok) return flipped;
            RefreshVessel(spot);
            _heatSync.Remove(spot);
            PublishHeatOf(spot, Tick, TickRate);
            GriddleSides sides = _kitchen.GriddleSidesOf(spot.ObjectId, Tick, TickRate);
            Debug.Log("[Kitchen] player " + player.OwnerId + " flipped the food on the griddle: " + GriddleRules.SideName(sides.DownSide)
                + " down (side 1 " + (sides.SideA / CookingTime.TimeScale).ToString("0.0") + " s, side 2 "
                + (sides.SideB / CookingTime.TimeScale).ToString("0.0") + " s)");
            return CookOutcome.Ok;
        }

        /// <summary>Spec 030: the griddle spot's item slides onto the plate (or into the bowl) the player holds.</summary>
        private CookOutcome TipGriddle(PickupInteractor player, Vessel spot, Vessel plate)
        {
            CookOutcome tipped = _kitchen.TryPour(spot.ObjectId, VesselKind.Griddle, plate.ObjectId, plate.Kind, Tick, TickRate, out Item result);
            if (tipped != CookOutcome.Ok && tipped != CookOutcome.TooShort) return tipped;
            RefreshVessel(spot);
            RefreshVessel(plate);
            Debug.Log("[Kitchen] player " + player.OwnerId + " slid " + KitchenState.NameOf(result) + " from the griddle onto the "
                + VesselRules.Name(plate.Kind) + " q=" + KitchenState.QualityOf(result).ToString("0.00"));
            return tipped;
        }

        // ---- Spec 031: the assembly board ----

        /// <summary>
        /// Spec 031: the board's tools (RequestStation; reach to the board was checked): a dispenser's press and release
        /// (<see cref="BuildRules.SqueezeStartOption"/> / <see cref="BuildRules.SqueezeStopOption"/>), the server measuring the time
        /// between their arrivals in its own ticks; a packaging (<see cref="BuildRules.PackagingOption"/>).
        /// </summary>
        private CookOutcome UseAssembly(PickupInteractor player, Station board, int option)
        {
            if (!_assemblySpots.TryGetValue(board.Index, out Vessel spot) || spot == null) return CookOutcome.UnknownStation;
            if (BuildRules.TryParseSqueeze(option, out int d, out bool start))
                return start ? StartSqueeze(player, board, d) : StopSqueeze(player, board, d);
            if (BuildRules.TryParsePackaging(option, out int p)) return Package(player, spot, p);
            return CookOutcome.InvalidOption;
        }

        private CookOutcome StartSqueeze(PickupInteractor player, Station board, int d)
        {
            CookOutcome started = Desk.Press(board.Index, d, player.OwnerId, Tick, WalletOf(player));
            if (started != CookOutcome.Ok) return started;
            _squeezers[AssemblyDesk.KeyOf(board.Index, d)] = player; // 032 review: the desk's key
            RefreshAssembly(board);
            return CookOutcome.Ok;
        }

        /// <summary>
        /// The release: the sauce for the time held goes on the build (or on the same sauce on top), its portion is charged
        /// first (spec 031b: AssemblyDesk); a refused step changes nothing and charges nothing. A release with no squeeze of this
        /// player (refused at the press, or already stopped by the server) is harmless: Ok, nothing happens.
        /// </summary>
        private CookOutcome StopSqueeze(PickupInteractor player, Station board, int d)
        {
            if (!_kitchen.IsSqueezing(board.Index, d, out int owner, out _) || owner != player.OwnerId) return CookOutcome.Ok;
            return EndSqueeze(player, board, d);
        }

        private CookOutcome EndSqueeze(PickupInteractor player, Station board, int d)
        {
            _squeezers.Remove(AssemblyDesk.KeyOf(board.Index, d));
            CookOutcome made = Desk.End(board.Index, d, player.OwnerId, Tick, TickRate, WalletOf(player), out AssemblyDesk.SqueezeResult r);
            RefreshAssembly(board);
            if (made != CookOutcome.Ok) return made;
            if (_assemblySpots.TryGetValue(board.Index, out Vessel spot)) RefreshVessel(spot);
            Debug.Log("[Kitchen] player " + player.OwnerId + " squeezed " + BuildRules.SauceOf(d) + " for " + r.Seconds.ToString("0.00")
                + " s (" + EconomyRules.Money(r.Cost) + "), top " + r.TopSeconds.ToString("0.00") + " s: " + KitchenState.NameOf(r.Build)
                + " q=" + KitchenState.QualityOf(r.Build).ToString("0.00"));
            return CookOutcome.Ok;
        }

        /// <summary>Spec 031: wrap paper, a box or a bag from the rack onto the build: checked in the sim, paid, then applied.</summary>
        private CookOutcome Package(PickupInteractor player, Vessel spot, int p)
        {
            CookOutcome done = Desk.Package(spot.ObjectId, p, player.OwnerId, WalletOf(player), out Item packed, out long cost);
            if (done != CookOutcome.Ok) return done;
            RefreshVessel(spot);
            Debug.Log("[Kitchen] player " + player.OwnerId + " used the " + BuildRules.PackagingName(p) + " (" + EconomyRules.Money(cost)
                + "): " + KitchenState.NameOf(packed) + " q=" + KitchenState.QualityOf(packed).ToString("0.00"));
            return CookOutcome.Ok;
        }

        /// <summary>Spec 031: empty hands (or anything) fold the open build on bread (RequestUse action ActionFold).</summary>
        private CookOutcome Fold(PickupInteractor player, Vessel spot)
        {
            if (spot.Kind != VesselKind.Assembly) return CookOutcome.InvalidOption;
            CookOutcome folded = _kitchen.TryFold(spot.ObjectId);
            if (folded != CookOutcome.Ok) return folded;
            RefreshVessel(spot);
            Debug.Log("[Kitchen] player " + player.OwnerId + " folded " + _kitchen.ResultName(spot.ObjectId, VesselKind.Assembly, Tick, TickRate));
            return CookOutcome.Ok;
        }

        /// <summary>Spec 031b: the player's wallet for the board's charges (free without an economy).</summary>
        private IAssemblyWallet WalletOf(PickupInteractor player) => _economy != null ? new PlayerWallet(_economy, player) : null;

        /// <summary>Spec 031b: <see cref="IAssemblyWallet"/> through the economy service: a portion or the full grocer price.</summary>
        private sealed class PlayerWallet : IAssemblyWallet
        {
            private readonly Worktown.Game.Economy.EconomyService _economy;
            private readonly PickupInteractor _player;

            public PlayerWallet(Worktown.Game.Economy.EconomyService economy, PickupInteractor player)
            {
                _economy = economy;
                _player = player;
            }

            public CookOutcome Charge(int owner, string grocerId, int percent, out long cost)
            {
                cost = 0;
                if (_player == null || _player.OwnerId != owner) return CookOutcome.SimRefused;
                Worktown.Sim.Economy.EconomyError paid = percent >= 100
                    ? _economy.TryBuy(_player.NetworkObject, grocerId, out cost)
                    : _economy.TryBuyPortion(_player.NetworkObject, grocerId, percent, out cost);
                return StockRules.PaidOutcome(paid);
            }

            public bool CanAfford(int owner, string grocerId, int percent) =>
                _player != null && _player.OwnerId == owner && _economy.CanAffordPortion(_player.NetworkObject, grocerId, percent);
        }

        private static readonly System.Func<PickupInteractor, bool> IsPresent = p => p != null && p.IsSpawned;

        /// <summary>Spec 031: the board's light byte (who squeezes).</summary>
        private void RefreshAssembly(Station board)
        {
            if (board == null || !board.TryGetComponent(out StationContents contents) || !contents.IsServerInitialized) return;
            contents.ServerSetStoveFlags(_kitchen.AssemblyFlags(board.Index));
        }

        /// <summary>
        /// Spec 031: a squeeze held past <see cref="BuildRules.AutoStopSeconds"/> ends as if released. 031b: a squeeze whose player
        /// left (despawned) is dropped at once, without a charge, so the dispenser is free for everyone.
        /// </summary>
        private void StopOverdueSqueezes(uint tick, double tickRate)
        {
            if (_squeezers.Count > 0)
            {
                // 031b review: the selection and the cancel are the desk's pure helper (tested); here only the refresh and the log.
                Desk.DropLeftSqueezes(_squeezers, IsPresent, _leftSqueezes);
                foreach (long key in _leftSqueezes)
                {
                    int board = AssemblyDesk.BoardOfKey(key), d = AssemblyDesk.DispenserOfKey(key);
                    RefreshAssembly(board >= 0 && board < _stations.Length ? _stations[board] : null);
                    Debug.Log("[Kitchen] the squeeze of a player who left was dropped (board " + board + ", dispenser " + d + ")");
                }
            }
            while (_kitchen.TryFindOverdueSqueeze(tick, tickRate, out int board, out int d, out int owner))
            {
                Station station = board >= 0 && board < _stations.Length ? _stations[board] : null;
                long key = AssemblyDesk.KeyOf(board, d);
                if (_squeezers.TryGetValue(key, out PickupInteractor player) && player != null && player.OwnerId == owner && station != null)
                {
                    CookOutcome ended = EndSqueeze(player, station, d);
                    if (ended != CookOutcome.Ok) Debug.Log("[Kitchen] the held squeeze ended: " + ended);
                    continue;
                }
                _squeezers.Remove(key);
                _kitchen.CancelSqueeze(board, d);
                RefreshAssembly(station);
            }
        }

        // ---- Spec 033: the drink dispenser ----

        /// <summary>
        /// Spec 033: the dispenser's tools (RequestStation; reach was checked but for a release): a tap's press and release
        /// (<see cref="DrinkRules.PourStartOption"/> / <see cref="DrinkRules.PourStopOption"/>), the server measuring the time between
        /// their arrivals in its own ticks and filling the cup in the hands; the cup stack (<see cref="DrinkRules.CupOption"/>) and the lid
        /// stack (<see cref="DrinkRules.LidOption"/>).
        /// </summary>
        private CookOutcome UseDrinks(PickupInteractor player, Station dispenser, int option)
        {
            if (dispenser.Kind != StationKind.DrinkDispenser) return CookOutcome.UnknownStation;
            if (DrinkRules.TryParsePour(option, out int tap, out bool start))
                return start ? StartPour(player, dispenser, tap) : StopPour(player, dispenser, tap);
            if (option == DrinkRules.CupOption) return TakeCup(player);
            if (option == DrinkRules.LidOption) return PutLid(player);
            return CookOutcome.InvalidOption;
        }

        /// <summary>The object id of the food the player holds, or -1.</summary>
        private int HeldFoodId(PickupInteractor player, out Food food)
        {
            food = null;
            if (player == null || !_pickups.TryGetHeld(player, out NetworkObject held) || held == null || !held.TryGetComponent(out food)) return -1;
            return held.ObjectId;
        }

        private CookOutcome StartPour(PickupInteractor player, Station dispenser, int tap)
        {
            int cup = HeldFoodId(player, out _);
            CookOutcome started = DrinkSteps.Press(dispenser.Index, tap, player.OwnerId, cup, Tick, DrinkWalletOf(player));
            if (started != CookOutcome.Ok) return started;
            _pourers[DrinkDesk.KeyOf(dispenser.Index, tap)] = player;
            RefreshDrinks(dispenser);
            return CookOutcome.Ok;
        }

        /// <summary>
        /// The release: the cup in the hands gets the drink for the time held (or its top-up), the pour is charged once first (the cup when
        /// it was empty plus the drink's share; DrinkDesk); a refused step changes nothing and charges nothing. A release with no pour of
        /// this player (refused at the press, or already stopped by the server) is harmless: Ok, nothing happens.
        /// </summary>
        private CookOutcome StopPour(PickupInteractor player, Station dispenser, int tap)
        {
            if (!_kitchen.IsPouring(dispenser.Index, tap, out int owner, out _, out _) || owner != player.OwnerId) return CookOutcome.Ok;
            return EndPour(player, dispenser, tap);
        }

        private CookOutcome EndPour(PickupInteractor player, Station dispenser, int tap)
        {
            _pourers.Remove(DrinkDesk.KeyOf(dispenser.Index, tap));
            int cup = HeldFoodId(player, out Food food);
            CookOutcome made = DrinkSteps.End(dispenser.Index, tap, player.OwnerId, Tick, TickRate, cup, DrinkWalletOf(player), out DrinkDesk.PourResult r);
            RefreshDrinks(dispenser);
            if (made != CookOutcome.Ok) return made;
            if (food != null) Publish(food, r.Cup);
            Debug.Log("[Kitchen] player " + player.OwnerId + " poured " + DrinkRules.DrinkOf(tap) + ": " + r.Poured.ToString("0") + " ml ("
                + EconomyRules.Money(r.Cost) + "), the cup holds " + r.Total.ToString("0") + " ml: " + KitchenState.NameOf(r.Cup) + " q="
                + KitchenState.QualityOf(r.Cup).ToString("0.00") + (r.Spilled ? " (spilled on the counter)" : string.Empty));
            return CookOutcome.Ok;
        }

        /// <summary>Spec 033: empty hands at the cup stack take an empty cup (free: its price comes with its first pour).</summary>
        private CookOutcome TakeCup(PickupInteractor player)
        {
            CookOutcome can = DrinkSteps.CanTakeCup(_pickups.TryGetHeld(player, out _));
            if (can != CookOutcome.Ok) return can;
            if (_foodPrefab == null || !_pickups.CanHandTo(player, _foodPrefab)) return CookOutcome.HandsFull;
            if (_kitchen.TryCreateCup(out Item cup) != CookOutcome.Ok) return CookOutcome.SimRefused;
            if (!SpawnFoodInHands(player, cup)) return CookOutcome.SimRefused;
            Debug.Log("[Kitchen] player " + player.OwnerId + " took an empty cup");
            return CookOutcome.Ok;
        }

        /// <summary>Spec 033: the lid stack puts a lid on the filled cup in the hands (free; a lid only on a filled cup, once).</summary>
        private CookOutcome PutLid(PickupInteractor player)
        {
            int cup = HeldFoodId(player, out Food food);
            CookOutcome lidded = DrinkSteps.Lid(cup, out Item item);
            if (lidded != CookOutcome.Ok) return lidded;
            if (food != null) Publish(food, item);
            Debug.Log("[Kitchen] player " + player.OwnerId + " put a lid on " + KitchenState.NameOf(item) + " q=" + KitchenState.QualityOf(item).ToString("0.00"));
            return CookOutcome.Ok;
        }

        /// <summary>Spec 033: the player's wallet for a pour (free without an economy).</summary>
        private IDrinkWallet DrinkWalletOf(PickupInteractor player) => _economy != null ? new PlayerDrinkWallet(_economy, player) : null;

        /// <summary>Spec 033: <see cref="IDrinkWallet"/> through the economy service: one debit per pour.</summary>
        private sealed class PlayerDrinkWallet : IDrinkWallet
        {
            private readonly EconomyService _economy;
            private readonly PickupInteractor _player;

            public PlayerDrinkWallet(EconomyService economy, PickupInteractor player)
            {
                _economy = economy;
                _player = player;
            }

            public CookOutcome Charge(int owner, string drinkId, int percent, bool withCup, out long cost)
            {
                cost = 0;
                if (_player == null || _player.OwnerId != owner) return CookOutcome.SimRefused;
                return StockRules.PaidOutcome(_economy.TryBuyFill(_player.NetworkObject, drinkId, percent, withCup, out cost));
            }

            public bool CanAfford(int owner, string drinkId, int percent, bool withCup) =>
                _player != null && _player.OwnerId == owner && _economy.CanAffordFill(_player.NetworkObject, drinkId, percent, withCup);
        }

        /// <summary>Spec 033: the dispenser's byte (who pours, the spill).</summary>
        private void RefreshDrinks(Station dispenser)
        {
            if (dispenser == null || !dispenser.TryGetComponent(out StationContents contents) || !contents.IsServerInitialized) return;
            contents.ServerSetStoveFlags(_kitchen.DrinkFlags(dispenser.Index));
        }

        /// <summary>
        /// Spec 033: a pour that reached <see cref="Worktown.Sim.Cooking.Drinks.MaxMl"/> ends as if released (a spill, the cup full). A
        /// pour whose player left (despawned) is dropped at once, without a charge, so the tap is free for everyone.
        /// </summary>
        private void StopOverduePours(uint tick, double tickRate)
        {
            if (_pourers.Count > 0)
            {
                DrinkSteps.DropLeftPours(_pourers, IsPresent, _leftPours);
                foreach (long key in _leftPours)
                {
                    int station = DrinkDesk.StationOfKey(key), tap = DrinkDesk.TapOfKey(key);
                    RefreshDrinks(station >= 0 && station < _stations.Length ? _stations[station] : null);
                    Debug.Log("[Kitchen] the pour of a player who left was dropped (dispenser " + station + ", tap " + tap + ")");
                }
            }
            while (_kitchen.TryFindOverduePour(tick, tickRate, out int station, out int tap, out int owner))
            {
                Station dispenser = station >= 0 && station < _stations.Length ? _stations[station] : null;
                long key = DrinkDesk.KeyOf(station, tap);
                if (_pourers.TryGetValue(key, out PickupInteractor player) && player != null && player.OwnerId == owner && dispenser != null)
                {
                    CookOutcome ended = EndPour(player, dispenser, tap);
                    if (ended != CookOutcome.Ok) Debug.Log("[Kitchen] the held pour ended: " + ended);
                    continue;
                }
                _pourers.Remove(key);
                _kitchen.CancelPour(station, tap);
                RefreshDrinks(dispenser);
            }
        }

        // ---- cookware (RequestUse) ----

        /// <summary>
        /// Spec 021: use what the player holds on the cookware <paramref name="target"/>. Action 0: food goes in, a pan,
        /// pot or bowl is poured into it, or empty hands take the food out; 1 one knife stroke, 2 turn (knife in the
        /// hands, a board). Checked before anything changes: the target is spawned cookware nobody holds, within reach
        /// (2.5 m), the hands, capacity and the sim. Hand over first, clear only on success.
        /// </summary>
        public CookOutcome TryUseVessel(PickupInteractor player, NetworkObject target, int action)
        {
            if (target == null || !target.IsSpawned || !target.TryGetComponent(out Vessel vessel)) return CookOutcome.NotAVessel;
            // Spec 029: a fryer basket has no Pickup (it never leaves its fryer); all other cookware must lie free.
            bool hasPickup = target.TryGetComponent(out Pickup pickup);
            CookOutcome reach = VesselRules.CheckUseTarget(vessel.Kind, hasPickup, hasPickup && pickup.IsHeld,
                player.transform.position, target.transform.position);
            if (reach != CookOutcome.Ok) return reach;
            EnsureInitialized();

            if (action == VesselRules.ActionStroke || action == VesselRules.ActionTurn) return Cut(player, vessel, action);
            if (action == VesselRules.ActionShake) return Shake(player, vessel);
            if (action == VesselRules.ActionFlip) return Flip(player, vessel); // spec 030
            if (action == VesselRules.ActionFold) return Fold(player, vessel); // spec 031
            if (action != VesselRules.ActionUse) return CookOutcome.InvalidOption;

            if (!_pickups.TryGetHeld(player, out NetworkObject held)) return TakeOut(player, vessel);
            if (held.TryGetComponent(out Vessel source))
            {
                // Spec 029b: holding a plate or bowl, a click on a raised basket tips its food onto it (no new RPC).
                if (vessel.Kind == VesselKind.Basket && VesselRules.TipsFromBasket(source.Kind)) return TipBasket(player, vessel, source);
                // Spec 030: holding a plate or bowl, a click on a griddle spot slides its item onto it.
                if (vessel.Kind == VesselKind.Griddle && VesselRules.TipsFromFixed(vessel.Kind, source.Kind)) return TipGriddle(player, vessel, source);
                return Pour(player, source, vessel);
            }
            if (!held.TryGetComponent(out Food food)) return CookOutcome.NotFood;

            CookOutcome put = _kitchen.TryPutInto(held.ObjectId, vessel.ObjectId, vessel.Kind, Tick);
            if (put != CookOutcome.Ok) return put;
            string name = food.DisplayName.Value; // read before despawn
            _pickups.TakeAway(player, out _);
            _networkManager.ServerManager.Despawn(held);
            RefreshVessel(vessel);
            Debug.Log("[Kitchen] player " + player.OwnerId + " put " + name + " into the " + VesselRules.Name(vessel.Kind));
            return CookOutcome.Ok;
        }

        /// <summary>Spec 019: a knife stroke or turn on a board; the knife must be in the hands.</summary>
        private CookOutcome Cut(PickupInteractor player, Vessel board, int action)
        {
            if (!_pickups.TryGetHeld(player, out NetworkObject tool) || !tool.TryGetComponent(out Knife _))
                return CookOutcome.NoKnife;
            if (board.Kind != VesselKind.Board) return CookOutcome.InvalidOption;
            CookOutcome cut = action == VesselRules.ActionStroke
                ? _kitchen.TryBoardStroke(board.ObjectId)
                : _kitchen.TryBoardTurn(board.ObjectId);
            if (cut == CookOutcome.Ok) RefreshVessel(board);
            return cut;
        }

        /// <summary>Empty hands: the board's food (cut so far), the pan's food (cooked so far) or the bowl's mix.</summary>
        private CookOutcome TakeOut(PickupInteractor player, Vessel vessel)
        {
            CookOutcome taken = _kitchen.TryPreviewTakeOut(vessel.ObjectId, vessel.Kind, Tick, TickRate, out Item result);
            if (taken != CookOutcome.Ok && taken != CookOutcome.TooShort) return taken;
            if (!SpawnFoodInHands(player, result)) return CookOutcome.SimRefused;
            _kitchen.ClearContents(vessel.ObjectId, vessel.Kind);
            RefreshVessel(vessel);
            Debug.Log("[Kitchen] player " + player.OwnerId + " took " + KitchenState.NameOf(result) + " from the "
                + VesselRules.Name(vessel.Kind) + (taken == CookOutcome.TooShort ? " (too short, unchanged)" : " q=" + KitchenState.QualityOf(result).ToString("0.00")));
            return taken;
        }

        /// <summary>Spec 029b: the raised basket's food goes onto the plate (or into the bowl) the player holds.</summary>
        private CookOutcome TipBasket(PickupInteractor player, Vessel basket, Vessel plate)
        {
            CookOutcome tipped = _kitchen.TryTipBasket(basket.ObjectId, plate.ObjectId, plate.Kind, Tick, TickRate, out Item result);
            if (tipped != CookOutcome.Ok && tipped != CookOutcome.TooShort) return tipped;
            RefreshVessel(basket);
            RefreshVessel(plate);
            Debug.Log("[Kitchen] player " + player.OwnerId + " tipped " + KitchenState.NameOf(result) + " from the basket onto the "
                + VesselRules.Name(plate.Kind) + " q=" + KitchenState.QualityOf(result).ToString("0.00"));
            return tipped;
        }

        /// <summary>Holding a pan, pot or bowl: pour its finished food onto the plate or into the bowl.</summary>
        private CookOutcome Pour(PickupInteractor player, Vessel source, Vessel target)
        {
            CookOutcome poured = _kitchen.TryPour(source.ObjectId, source.Kind, target.ObjectId, target.Kind, Tick, TickRate, out Item result);
            if (poured != CookOutcome.Ok && poured != CookOutcome.TooShort) return poured;
            RefreshVessel(source);
            RefreshVessel(target);
            Debug.Log("[Kitchen] player " + player.OwnerId + " poured " + KitchenState.NameOf(result) + " from the "
                + VesselRules.Name(source.Kind) + " into the " + VesselRules.Name(target.Kind));
            return poured;
        }

        /// <summary>Spec 021: empty hands at the shelf get a clean plate, while the world has fewer than the maximum.</summary>
        private CookOutcome TakePlate(PickupInteractor player)
        {
            if (_pickups.TryGetHeld(player, out _)) return CookOutcome.HandsFull;
            int plates = 0;
            foreach (Vessel vessel in _vessels)
                if (vessel != null && vessel.IsSpawned && vessel.Kind == VesselKind.Plate) plates++;
            if (plates >= VesselRules.MaxPlatesInWorld) return CookOutcome.NoPlatesLeft;
            NetworkObject prefab = VesselPrefab(VesselKind.Plate);
            if (prefab == null) return CookOutcome.UnknownStation;

            NetworkObject plate = _networkManager.GetPooledInstantiated(prefab, true);
            plate.transform.SetPositionAndRotation(player.HoldPoint.position, player.HoldPoint.rotation);
            _networkManager.ServerManager.Spawn(plate);
            Vessel fresh = plate.GetComponent<Vessel>();
            _kitchen.RemoveVessel(plate.ObjectId); // a reused object id starts empty
            _vessels.Add(fresh);
            RefreshVessel(fresh);
            if (_pickups.HandTo(player, plate)) return CookOutcome.Ok;
            Debug.LogError("[Kitchen] could not hand a plate to player " + player.OwnerId);
            DespawnVessel(fresh);
            return CookOutcome.SimRefused;
        }

        // ---- spec 021: cookware moving on and off burners ----

        private void OnItemMoved(NetworkObject item, PlaceSurface surface)
        {
            if (item == null || !_networkManager.IsServerStarted || !item.TryGetComponent(out Vessel vessel)) return;
            if (!VesselRules.StandsOnBurner(vessel.Kind)) return;
            int burner = surface != null && surface.IsBurner ? surface.Index : KitchenState.NoBurner;
            _kitchen.SetVesselBurner(vessel.ObjectId, burner, Tick);
            RefreshVessel(vessel);
        }

        /// <summary>Server: the vessel's synced contents, the name of what it gives, the pan mode.</summary>
        private void RefreshVessel(Vessel vessel)
        {
            if (vessel == null || !vessel.IsServerInitialized) return;
            uint tick = Tick;
            double rate = TickRate;
            int id = vessel.ObjectId;
            byte mode = 0;
            if (vessel.Kind == VesselKind.Pan) mode = _kitchen.PanIsSaute(id) ? Vessel.ModeSaute : Vessel.ModeFry;
            else if (vessel.Kind == VesselKind.Basket) mode = _kitchen.BasketMode(id, tick); // spec 029
            else if (vessel.Kind == VesselKind.Griddle) mode = _kitchen.GriddleMode(id, tick, rate); // spec 030
            else if (vessel.Kind == VesselKind.Assembly) mode = _kitchen.BuildMode(id); // spec 031
            // 030 review: one preview per food for the label and the title.
            IReadOnlyList<string> names = _kitchen.ContentNamesAndTitle(id, vessel.Kind, tick, rate, out string title);
            vessel.ServerSet(names, _kitchen.ContentLooks(id, vessel.Kind, tick, rate), title, mode);
        }

        /// <summary>Server: the stove's knob and button lights (two bits per burner).</summary>
        private void RefreshStove(Station stove)
        {
            if (!stove.TryGetComponent(out StationContents contents) || !contents.IsServerInitialized) return;
            byte flags = 0;
            for (int b = 0; b < StationRules.BurnersPerStove; b++)
            {
                if (!_burnerSurface.TryGetValue(BurnerKey(stove.Index, b), out int burnerId)) continue;
                StoveSettings settings = _kitchen.BurnerOf(burnerId);
                flags |= StationRules.BurnerFlags(b, settings.IsOn, settings.Saute);
            }
            contents.ServerSetStoveFlags(flags);
        }

        private static long BurnerKey(int stoveIndex, int burner) => ((long)stoveIndex << 8) | (uint)burner;

        /// <summary>
        /// First server frame: every spawned cookware, the burners, the home spots of the knife and the cookware
        /// (where they lie at the start), and which pan or pot already stands on a burner.
        /// </summary>
        private void EnsureInitialized()
        {
            if (_vessels != null && _initialized) return;
            // Scene objects should be spawned by the first server frame; if none is yet, look again next frame (for a
            // while), so the cookware is never missed.
            _initAttempts++;
            _vessels = new List<Vessel>();
            _homes = new List<HomeSlot>();
            _burnerSurface.Clear();
            for (int i = 0; i < _pickups.SurfaceCount; i++)
            {
                PlaceSurface surface = _pickups.SurfaceAt(i);
                if (surface != null && surface.IsBurner) _burnerSurface[BurnerKey(surface.StoveIndex, surface.Burner)] = surface.Index;
            }
            foreach (Vessel vessel in FindObjectsByType<Vessel>(FindObjectsSortMode.None))
            {
                if (!vessel.IsSpawned) continue;
                _vessels.Add(vessel);
                if (VesselRules.RespawnsAtHome(vessel.Kind)) _homes.Add(Home(vessel.NetworkObject, VesselPrefab(vessel.Kind)));
                PlaceOnBurnerIfThere(vessel);
                RefreshVessel(vessel);
            }
            foreach (Knife knife in FindObjectsByType<Knife>(FindObjectsSortMode.None))
                if (knife.TryGetComponent(out NetworkObject nob) && nob.IsSpawned) _homes.Add(Home(nob, _knifePrefab));
            // Spec 030: the spatula comes back at its home spot like the knife.
            foreach (Spatula spatula in FindObjectsByType<Spatula>(FindObjectsSortMode.None))
                if (spatula.TryGetComponent(out NetworkObject nob) && nob.IsSpawned) _homes.Add(Home(nob, _spatulaPrefab));
            // Spec 030: the griddle spots (fixed scene objects on their griddle).
            _griddleSpots.Clear();
            foreach (GriddleSpot gs in FindObjectsByType<GriddleSpot>(FindObjectsSortMode.None))
            {
                if (gs.Griddle == null || !gs.TryGetComponent(out Vessel spot) || !spot.IsSpawned) continue;
                _kitchen.RegisterGriddleSpot(spot.ObjectId, gs.Griddle.Index, gs.Spot);
                _griddleSpots[BurnerKey(gs.Griddle.Index, gs.Spot)] = spot;
                RefreshVessel(spot);
            }
            // Spec 031: the build spots of the assembly boards.
            _assemblySpots.Clear();
            foreach (AssemblySpot a in FindObjectsByType<AssemblySpot>(FindObjectsSortMode.None))
            {
                if (a.Board == null || !a.TryGetComponent(out Vessel spot) || !spot.IsSpawned) continue;
                _kitchen.RegisterAssemblySpot(spot.ObjectId, a.Board.Index);
                _assemblySpots[a.Board.Index] = spot;
                RefreshVessel(spot);
            }
            // Spec 029: the fryer baskets (scene objects next to their fryer).
            _baskets.Clear();
            _basketList.Clear();
            foreach (FryerBasket fb in FindObjectsByType<FryerBasket>(FindObjectsSortMode.None))
            {
                if (fb.Fryer == null || !fb.TryGetComponent(out Vessel basket) || !basket.IsSpawned) continue;
                _kitchen.RegisterBasket(basket.ObjectId, fb.Fryer.Index, fb.Basket);
                _baskets[BurnerKey(fb.Fryer.Index, fb.Basket)] = basket;
                _basketList.Add(basket);
                RefreshVessel(basket);
            }
            foreach (Station station in _stations)
            {
                if (station != null && station.Kind == StationKind.Stove) RefreshStove(station);
                else if (station != null && station.Kind == StationKind.Fryer) RefreshFryer(station);
                else if (station != null && station.Kind == StationKind.Griddle) RefreshGriddle(station); // spec 030
                else if (station != null && station.Kind == StationKind.Assembly) RefreshAssembly(station); // spec 031
                else if (station != null && station.Kind == StationKind.DrinkDispenser) RefreshDrinks(station); // spec 033
            }
            _initialized = _vessels.Count > 0 || _initAttempts >= MaxInitAttempts;
        }

        private static HomeSlot Home(NetworkObject item, NetworkObject prefab)
        {
            return new HomeSlot { Prefab = prefab, Position = item.transform.position, Rotation = item.transform.rotation, Item = item };
        }

        private void PlaceOnBurnerIfThere(Vessel vessel)
        {
            if (!VesselRules.StandsOnBurner(vessel.Kind)) return;
            PlaceSurface surface = _pickups.SurfaceUnder(vessel.transform.position);
            _kitchen.SetVesselBurner(vessel.ObjectId, surface != null && surface.IsBurner ? surface.Index : KitchenState.NoBurner, Tick);
        }

        private NetworkObject VesselPrefab(VesselKind kind)
        {
            foreach (NetworkObject prefab in _vesselPrefabs)
                if (prefab != null && prefab.TryGetComponent(out Vessel v) && v.Kind == kind) return prefab;
            return null;
        }

        /// <summary>
        /// Spec 019/021: the knife or a piece of cookware that has been gone, or below the world, for 5 s is replaced by
        /// a new one at its home spot (its contents are lost). Never more than one per home: the lost one is despawned
        /// first. A lost plate is only removed (the shelf gives new ones).
        /// </summary>
        private void WatchHomes()
        {
            float now = Time.unscaledTime;
            if (now < _nextHomeCheck) return;
            _nextHomeCheck = now + HomeCheckInterval;

            for (int i = _vessels.Count - 1; i >= 0; i--)
            {
                Vessel vessel = _vessels[i];
                if (vessel == null || !vessel.IsSpawned)
                {
                    _vessels.RemoveAt(i); // its id may be reused; KitchenState entries were removed when it went
                    if (vessel != null)
                    {
                        _heatSync.Remove(vessel);
                        _labelStamps.Remove(vessel);
                    }
                    continue;
                }
                if (vessel.Kind == VesselKind.Plate && VesselRules.IsLost(true, vessel.transform.position.y))
                {
                    Debug.Log("[Kitchen] a plate fell out of the world");
                    DespawnVessel(vessel);
                }
            }

            foreach (HomeSlot slot in _homes)
            {
                NetworkObject item = slot.Item;
                bool exists = item != null && item.IsSpawned;
                if (!VesselRules.IsLost(exists, exists ? item.transform.position.y : 0f))
                {
                    slot.LostSince = -1f;
                    continue;
                }
                if (slot.LostSince < 0f) slot.LostSince = now;
                if (!VesselRules.ShouldRespawn(slot.LostSince, now) || slot.Prefab == null) continue;

                if (exists)
                {
                    if (item.TryGetComponent(out Vessel lost)) DespawnVessel(lost);
                    else _networkManager.ServerManager.Despawn(item);
                }
                NetworkObject fresh = _networkManager.GetPooledInstantiated(slot.Prefab, true);
                fresh.transform.SetPositionAndRotation(slot.Position, slot.Rotation);
                _networkManager.ServerManager.Spawn(fresh);
                slot.Item = fresh;
                slot.LostSince = -1f;
                if (fresh.TryGetComponent(out Vessel vessel))
                {
                    _kitchen.RemoveVessel(fresh.ObjectId);
                    _vessels.Add(vessel);
                    PlaceOnBurnerIfThere(vessel);
                    RefreshVessel(vessel);
                }
                Debug.Log("[Kitchen] " + fresh.name + " was lost; a new one lies at its home spot");
            }
        }

        private void DespawnVessel(Vessel vessel)
        {
            _kitchen.RemoveVessel(vessel.ObjectId);
            _vessels.Remove(vessel);
            _heatSync.Remove(vessel); // a pooled object that comes back starts fresh
            _labelStamps.Remove(vessel);
            if (vessel.IsSpawned) _networkManager.ServerManager.Despawn(vessel.NetworkObject);
        }

        // Spec 016/021: keep the cooking labels current (doneness bucket). Only changes are sent; the check runs once
        // per second of ticks, not every frame (review nit).
        private void Update()
        {
            if (!_networkManager.IsServerStarted) return;
            EnsureInitialized();
            uint tick = Tick;
            double tickRate = TickRate;
            // Spec 030 (orchestrator decision): the label is the name a take would give now, so it is refreshed 4 times a
            // second (HeatSync's rate; only changes are sent), and the name in the hands never differs from the label for long.
            if (_labelsDirty || CookingTime.ElapsedTicks(_lastLabelTick, tick) >= HeatSync.MinTicks(tickRate) || tick < _lastLabelTick)
            {
                bool all = _labelsDirty;
                _labelsDirty = false;
                _lastLabelTick = tick;
                foreach (Vessel vessel in _vessels)
                {
                    if (vessel == null || !VesselRules.IsHeat(vessel.Kind) || _kitchen.HeatCount(vessel.ObjectId) == 0) continue;
                    // 030 review: only food that cooked since the last refresh (a heat that is off changes no name).
                    long stamp = _kitchen.HeatStamp(vessel.ObjectId, tick);
                    if (!all && _labelStamps.TryGetValue(vessel, out long last) && last == stamp) continue;
                    _labelStamps[vessel] = stamp;
                    RefreshVessel(vessel);
                }
            }
            if (!_heatChecked || tick != _lastHeatTick)
            {
                _heatChecked = true;
                _lastHeatTick = tick;
                // Spec 029: a raised basket that has drained is raised again (the server timer).
                for (int i = 0; i < _basketList.Count; i++)
                {
                    Vessel basket = _basketList[i];
                    if (basket != null && _kitchen.SettleDrain(basket.ObjectId, tick)) RefreshVessel(basket);
                }
                PublishHeat(tick, tickRate);
                StopOverdueSqueezes(tick, tickRate); // spec 031
                StopOverduePours(tick, tickRate); // spec 033
            }
            WatchHomes();
        }

        /// <summary>
        /// Spec 027c: the pan's and pot's heat bytes (zone with the warning bit, progress) from the sim's doneness window
        /// and the time on the heat. Each vessel's <see cref="HeatSync"/> sends only changes, at most 4 per second.
        /// </summary>
        private void PublishHeat(uint tick, double tickRate)
        {
            foreach (Vessel vessel in _vessels)
                if (vessel != null) PublishHeatOf(vessel, tick, tickRate);
        }

        private void PublishHeatOf(Vessel vessel, uint tick, double tickRate)
        {
            if (!vessel.IsServerInitialized || !VesselRules.IsHeat(vessel.Kind)) return;
            if (!_heatSync.TryGetValue(vessel, out HeatSync sync))
            {
                sync = new HeatSync();
                _heatSync[vessel] = sync;
            }
            HeatReading reading = _kitchen.HeatReadingOf(vessel.ObjectId, tick, tickRate);
            byte zone = HeatZoneRules.Encode(reading);
            byte zoneBefore = sync.Zone;
            if (!sync.ShouldSend(tick, tickRate, reading.Progress, zone)) return;
            vessel.ServerSetHeat(reading.Progress, zone);
            // Spec 029b: the label follows the zone change with the ring, not up to a refresh later (spec 030: the name a take
            // would give may change with it; the refresh also republishes a griddle spot's up side).
            if (HeatZoneRules.ZoneOf(zone) != HeatZoneRules.ZoneOf(zoneBefore)) RefreshVessel(vessel);
        }

        // ---- stock and the counter (spec 007/009, spec 028b) ----

        /// <summary>
        /// Spec 028b: one ingredient from a fridge or shelf slot (option = slot) or a crate (option 0), the same way the
        /// crates always worked: StockRules checks the slot, the reach to its anchor, free hands and the ingredient; then the
        /// sim item, the grocer's price (spec 009: no money, no potato) and the food in the hands.
        /// </summary>
        private CookOutcome TakeIngredient(PickupInteractor player, Station stock, int option)
        {
            var take = new StockTake(this, player, stock, option);
            CookOutcome outcome = StockTakeSequence.Run(take); // spec 029b: never paid without the item
            if (outcome == CookOutcome.Ok)
                Debug.Log("[Kitchen] player " + player.OwnerId + " bought " + KitchenState.NameOf(take.Item) + " from the "
                    + stock.DisplayName + " (option " + option + ") for " + EconomyRules.Money(take.Cost));
            else if (take.Paid)
                Debug.LogError("[Kitchen] paid but could not hand " + KitchenState.NameOf(take.Item) + " to player " + player.OwnerId);
            return outcome;
        }

        /// <summary>Spec 029b: the server's steps of one stock take (<see cref="StockTakeSequence"/>).</summary>
        private sealed class StockTake : IStockTakeSteps
        {
            private readonly KitchenService _service;
            private readonly PickupInteractor _player;
            private readonly Station _stock;
            private readonly int _option;
            private string _baseId;

            public StockTake(KitchenService service, PickupInteractor player, Station stock, int option)
            {
                _service = service;
                _player = player;
                _stock = stock;
                _option = option;
            }

            public Item Item { get; private set; }
            public long Cost { get; private set; }
            public bool Paid { get; private set; }

            public CookOutcome Check()
            {
                CookOutcome check = StockRules.CheckTake(_stock, _option, _player.transform.position,
                    _service._pickups.TryGetHeld(_player, out _), out _baseId);
                if (check == CookOutcome.Ok) _service.EnsureInitialized();
                return check;
            }

            public CookOutcome Create()
            {
                CookOutcome created = _service._kitchen.TryCreateIngredient(_baseId, out Item item);
                Item = item;
                return created;
            }

            public bool CanHandOver() => _service._foodPrefab != null && _service._pickups.CanHandTo(_player, _service._foodPrefab);

            public CookOutcome Pay()
            {
                if (_service._economy == null) return CookOutcome.Ok;
                CookOutcome paid = StockRules.PaidOutcome(_service._economy.TryBuy(_player.NetworkObject, _baseId, out long cost));
                Cost = cost;
                Paid = paid == CookOutcome.Ok;
                return paid;
            }

            public bool HandOver() => _service.SpawnFoodInHands(_player, Item);
        }

        /// <summary>Spawns a food object for a sim item directly in the player's hands.</summary>
        private bool SpawnFoodInHands(PickupInteractor player, Item item)
        {
            NetworkObject food = _networkManager.GetPooledInstantiated(_foodPrefab, true);
            food.transform.SetPositionAndRotation(player.HoldPoint.position, player.HoldPoint.rotation);
            // Spec 017: set the look before spawning, so it arrives with the spawn and a pooled object never
            // shows its old model or the fallback cube for a tick.
            Publish(food.GetComponent<Food>(), item);
            _networkManager.ServerManager.Spawn(food);
            _kitchen.Register(food.ObjectId, item);
            if (_pickups.HandTo(player, food)) return true;

            // Should not happen (hands were checked); do not leave the item lying around unowned.
            Debug.LogError("[Kitchen] could not hand " + KitchenState.NameOf(item) + " to player " + player.OwnerId);
            _kitchen.TryRemove(food.ObjectId, out _);
            _networkManager.ServerManager.Despawn(food);
            return false;
        }

        /// <summary>Server: name, rating and (spec 017) the coarse look of the item on its food object.</summary>
        private static void Publish(Food food, Item item)
        {
            food.ServerSet(KitchenState.NameOf(item), KitchenState.QualityOf(item),
                FoodLookRules.From(item), FoodLookRules.PartsOf(item));
        }

        /// <summary>
        /// Spec 009: sell what the player holds. Loose food is plated by the counter as before; spec 021: a plate with
        /// food is sold as the plate op on its parts, and the plate goes away with the meal. A refused sale keeps
        /// everything in the hands. Spec 022: a sale to NPC demand goes to a waiting customer (first exact order match,
        /// else the first in line) and pays the match's share of the sim price.
        /// </summary>
        private CookOutcome Serve(PickupInteractor player, Station counter)
        {
            if (!_pickups.TryGetHeld(player, out NetworkObject held)) return CookOutcome.NothingHeld;
            Vessel plate = null;
            Item item;
            if (held.TryGetComponent(out Vessel vessel))
            {
                if (vessel.Kind != VesselKind.Plate) return CookOutcome.NotFood;
                CookOutcome dish = _kitchen.TryPreviewTake(held.ObjectId, VesselKind.Plate, out item);
                if (dish != CookOutcome.Ok) return dish;
                plate = vessel;
            }
            else if (!held.TryGetComponent(out Food _) || !_kitchen.TryGet(held.ObjectId, out item))
            {
                return CookOutcome.NotFood;
            }

            string name = KitchenState.NameOf(item);
            float quality = KitchenState.QualityOf(item);
            int starCount = StarRating.Stars(quality);
            string stars = StarRating.Format(starCount);
            string entry;
            bool served = false;
            Worktown.Game.Npc.GuestPick pick = default;
            long soldPayout = 0;

            if (_economy != null)
            {
                Item plated = item;
                if (plate == null && _kitchen.TryPlate(held.ObjectId, out plated) != CookOutcome.Ok) return CookOutcome.CannotSell;
                // Spec 022: which waiting customer gets it (a pure read; a failure here only means "no customer", the sale then
                // pays the full sim price as before). Spec 032: with its modifiers; the guest pays later at the register, so a sale
                // to it credits nothing now (TrySellForGuest) and the guest's bill comes from the sim's payout.
                bool forCustomer = false;
                if (_customers != null)
                {
                    try { forCustomer = _customers.TryPick(plated, out pick); }
                    catch (System.Exception e) { Debug.LogException(e); forCustomer = false; }
                }
                SaleResult sale = forCustomer ? _economy.TrySellForGuest(player.NetworkObject, plated) : _economy.TrySell(player.NetworkObject, plated);
                if (!sale.Ok)
                {
                    Debug.Log("[Economy] player " + player.OwnerId + " could not sell " + name + ": " + sale.Error);
                    return CookOutcome.CannotSell;
                }
                string where = sale.Channel == SaleChannel.NpcDemand ? sale.MarketId : "wholesaler";
                served = forCustomer && sale.Channel == SaleChannel.NpcDemand;
                soldPayout = sale.Payout;
                if (served)
                    entry = "Player " + player.OwnerId + " handed " + name + " " + stars + " to a guest · "
                        + (pick.IsDrink ? "drink" : Worktown.Game.Npc.Menu.NameOf(pick.Dish)) + " order: " // spec 033: a drink line
                        + Worktown.Game.Npc.OrderRules.Describe(pick.Fit.Match);
                else
                    entry = "Player " + player.OwnerId + " sold " + name + " " + stars + " for "
                        + EconomyRules.Money(sale.Payout) + " (" + where + ")";
                Debug.Log("[Economy] player " + player.OwnerId + (served ? " handed " : " sold ") + name + " (sim payout "
                    + EconomyRules.Money(sale.Payout) + ") to " + where + " q=" + quality.ToString("0.00")
                    + (served ? " (" + Worktown.Game.Npc.OrderRules.Describe(pick.Fit.Match) + ", bill " + pick.BillPermille + " permille)" : string.Empty));
            }
            else
            {
                entry = "Player " + player.OwnerId + " served " + name + " " + stars + " (q " + quality.ToString("0.00") + ")";
                Debug.Log("[Kitchen] player " + player.OwnerId + " served " + name + " q=" + quality.ToString("0.00"));
            }

            _pickups.TakeAway(player, out _);
            if (plate != null) DespawnVessel(plate);
            else
            {
                _kitchen.TryRemove(held.ObjectId, out _);
                _networkManager.ServerManager.Despawn(held);
            }
            if (_feed != null)
            {
                _feed.ServerAdd(entry);
                // Spec 027c: the reward moment over the counter for everyone (stars, coin), through the feed's SyncVar. Spec 032: a
                // guest's reward comes when it pays at the register (its tip is known then); a plain sale has no tip.
                if (!served) _feed.ServerReward(counter.Index, starCount, soldPayout, 0);
            }

            // Spec 014, after the sale is complete: customers must never be able to break the money transaction
            // (review blocker). Spec 032: the guest takes the dish and pays at the register; should that fail, the bill is paid now.
            // 032 review: never for a guest already in the register's line (it pays there, once; GuestFallback).
            if (served)
            {
                bool handed = false;
                try { handed = _customers.OnHandedOver(pick, player, soldPayout); }
                catch (System.Exception e) { Debug.LogException(e); }
                bool waiting = true;
                try { waiting = _customers.HasBill(pick); } // spec 033: in the register's line, or the line is on its tab
                catch (System.Exception e) { Debug.LogException(e); }
                if (Worktown.Game.Shop.RegisterFlow.FallbackPays(handed, waiting))
                    _economy.TryPayGuest(player.OwnerId, player.NetworkObject, EconomyRules.ApplyPermille(soldPayout, pick.BillPermille), out _);
            }
            return CookOutcome.Ok;
        }
    }
}
