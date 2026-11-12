using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Npc;
using Worktown.Sim.Climb;
using Worktown.Sim.Economy;

namespace Worktown.Game.Climb
{
    /// <summary>
    /// Spec 053: the shop's stage of the D34 climb and its goal, as every client sees them (a global scene object, like the stock counts):
    /// <see cref="Stage"/> (0 the open kitchen, 1 the food corner, spec 057: 2 the first restaurant), <see cref="GoalMoney"/> (stage 1: the
    /// rent goal; stage 2: the service licence) and <see cref="GoalReached"/>. No RPC. The server sets them when a game starts (SaveService)
    /// and runs each stage's service moments here: stage 1's drop at a start and its pitch fee and goal at an end; spec 057: stage 2's frozen
    /// menu at a start, its rent and upkeep (<see cref="RentRules"/>) and its star window and gate (<see cref="GateRules"/>) at an end.
    /// Spec 057 (D40): the crew factors are measured at each service start (the players connected), so the goals, the recipes and the rent
    /// are fixed for that service. The goal board's presses (<see cref="ServerPressBoard"/>) rent the restaurant (the move happens at once
    /// between services, else at the service end) and buy and toggle the stage 2 recipes. ClosedTargets.IsClosed is the one test of a
    /// closed station, slot or cookware that the server's refusal and the client's prompt share.
    /// </summary>
    public sealed class ShopStage : NetworkBehaviour
    {
        public readonly SyncVar<byte> Stage = new SyncVar<byte>(ShopStageRules.OpenKitchen);
        public readonly SyncVar<long> GoalMoney = new SyncVar<long>(0L);
        public readonly SyncVar<bool> GoalReached = new SyncVar<bool>(false);
        // Spec 057: the restaurant's state for the board, the HUD and the summary (server writes, no RPC).
        public readonly SyncVar<bool> MovePending = new SyncVar<bool>(false);
        public readonly SyncVar<byte> RecipesOwned = new SyncVar<byte>(0);
        public readonly SyncVar<byte> MenuNext = new SyncVar<byte>(0);
        public readonly SyncVar<byte> MenuNow = new SyncVar<byte>(0);
        public readonly SyncVar<short> WindowAverageTenths = new SyncVar<short>(0);
        public readonly SyncVar<byte> WindowCount = new SyncVar<byte>(0);
        /// <summary>Spec 057 (the summary's pips): bit i, the i-th window service (oldest first) reached the gate's stars.</summary>
        public readonly SyncVar<byte> WindowPips = new SyncVar<byte>(0);
        public readonly SyncVar<long> RentOwed = new SyncVar<long>(0L);
        /// <summary>Spec 057 (D40): the crew size measured at the last service start; the client derives every crew-scaled amount from it.</summary>
        public readonly SyncVar<byte> CrewPlayers = new SyncVar<byte>((byte)CrewScaling.ReferencePlayers);
        /// <summary>057 review: the rent and upkeep the last stage 2 service end paid (the summary's line; the crew of that service).</summary>
        public readonly SyncVar<long> RentLast = new SyncVar<long>(0L);

        [SerializeField] private EconomyService _economy;
        [SerializeField] private Worktown.Game.Shop.SupplyService _supply;
        [SerializeField] private ServeFeed _feed;

        private readonly ClimbCatalog _climb = FoodCornerRules.DefaultClimb;
        private readonly BoardConfirm _confirm = new BoardConfirm();
        private CustomerService _customers;

        // Server: the game's values (kept while the object is not spawned yet; pushed to the synced fields when it is).
        private byte _stage = ShopStageRules.OpenKitchen;
        private long _goal;
        private long _pitchFee;
        private bool _reached;
        private bool _movePending;
        private readonly List<string> _unlocks = new List<string>();
        private byte _owned;
        private byte _menuNext;
        private byte _menuNow;
        private int[] _window = Array.Empty<int>();
        private long _rentOwed;
        // 057 review: _crew is the one-time crew (goals, recipes, the licence: RestaurantRules.OneTimePlayers over _crewStarts and the
        // players connected now, _players); _rentCrew is the crew at this service's start (the rent and upkeep, D40's running costs).
        private int _players = 1;
        private CrewFactors _crew;
        private CrewFactors _rentCrew;
        private int[] _crewStarts = Array.Empty<int>();
        private long _due;
        private long _rentLast;
        private bool _serviceOpen;

        /// <summary>Server: the stage of the running game.</summary>
        public byte ServerStage => _stage;
        public long ServerGoal => _goal;
        public bool ServerReached => _reached;
        public long PitchFee => _pitchFee;
        public bool ServerMovePending => _movePending;
        public long ServerRentOwed => _rentOwed;
        public byte ServerOwned => _owned;
        public byte ServerMenuNext => _menuNext;
        /// <summary>Server: the menu frozen at the service start (stage 2's guests order from it).</summary>
        public byte ServerMenuNow => _menuNow;
        public IReadOnlyList<int> ServerWindow => _window;
        public IReadOnlyList<string> ServerUnlocks => _unlocks;
        public int ServerPlayers => _players;
        /// <summary>057 review: the players at the last service starts (at most 3, newest last; the save's crewStarts).</summary>
        public IReadOnlyList<int> ServerCrewStarts => _crewStarts;
        /// <summary>057 review: the players connected now (null: the server's connections; the tests set it).</summary>
        public Func<int> ConnectedPlayers { get; set; }
        /// <summary>Server: what the last stage 2 service end charged (AC12's log).</summary>
        public RentRules.Result LastRent { get; private set; }
        public ClimbCatalog Climb => _climb;

        /// <summary>Server: raised when the stage moved (the save writes StageChanged).</summary>
        public event Action StageChanged;

        /// <summary>The stage this process sees now: the server's value, a client's synced one, else the open kitchen.</summary>
        public byte StageNow => NetworkObject == null || IsServerInitialized ? _stage : IsClientInitialized ? Stage.Value : _stage;

        /// <summary>Spec 057: the server runs this object now (false offline: the tests, a component not yet initialized).</summary>
        private bool ServerUp => NetworkObject != null && IsServerInitialized;

        public static ShopStage Find() => FindFirstObjectByType<ShopStage>();

        private EconomyCatalog EconomyData => _economy != null ? _economy.Catalog : EconomyCatalog.CreateDefault();

        /// <summary>Server: a game starts at <paramref name="stage"/> with its goal, pitch fee and whether the goal was reached already (the open kitchen, tests).</summary>
        public void ServerBegin(byte stage, long goal, long pitchFee, bool reached)
        {
            ServerBegin(stage, pitchFee, reached, false, null, 0L, null, null);
            if (stage != ShopStageRules.FoodCorner && stage != ShopStageRules.FirstRestaurant) _goal = goal;
            Push();
        }

        /// <summary>
        /// Spec 057, server: a game starts from a save's climb part: its stage, whether the goal was reached, a pending move, the unlocks (the
        /// owned recipes among them), the rent owed, the star window and the owned recipes that are off the menu. The goal follows the stage
        /// and the crew connected now (measured again at each service start).
        /// </summary>
        public void ServerBegin(byte stage, long pitchFee, bool reached, bool movePending, IReadOnlyList<string> unlocks, long rentOwed,
            IReadOnlyList<int> window, IReadOnlyList<string> menuOff, IReadOnlyList<int> crewStarts = null)
        {
            _stage = stage;
            _pitchFee = pitchFee;
            _reached = reached;
            _movePending = stage == ShopStageRules.FoodCorner && movePending;
            _unlocks.Clear();
            if (unlocks != null) foreach (string id in unlocks) if (!string.IsNullOrEmpty(id)) _unlocks.Add(id);
            _owned = OwnedOf(_climb, _unlocks);
            _menuNext = MenuOf(_climb, _owned, menuOff);
            _menuNow = _menuNext;
            _window = GateRules.Push(window ?? Array.Empty<int>(), GateRules.NotCounted);
            _confirm.Clear();
            _serviceOpen = false;
            _crewStarts = RestaurantRules.PushCrew(crewStarts, 0);
            _rentLast = 0L;
            _rentCrew = RestaurantRules.Crew(_climb, CountPlayers());
            _rentOwed = Math.Max(0L, rentOwed);
            RefreshCrew(); // 057 review: a load counts the saved service starts too, not only who joined yet
            _due = _stage >= ShopStageRules.FirstRestaurant ? RestaurantRules.Due(EconomyData, _climb, _rentCrew, _unlocks) : 0L;
            Push();
            if (_economy != null) _economy.SetMenuStage(stage); // spec 037: the board sells this stage's lines
            Debug.Log("[Stage] " + Describe());
            if (_movePending) Move(false); // a save written during the service the rent was paid in: the move happens at the load
        }

        private string Describe()
        {
            switch (_stage)
            {
                case ShopStageRules.OpenKitchen: return "the open kitchen (every station)";
                case ShopStageRules.FoodCorner:
                    return "stage 1, the food corner: goal " + EconomyRules.Money(_goal) + (_reached ? " (reached)" : string.Empty) + ", pitch fee "
                        + EconomyRules.Money(_pitchFee) + (_movePending ? ", the move is pending" : string.Empty) + ", crew " + _players;
                default:
                    return "stage " + _stage + ", the first restaurant: licence " + EconomyRules.Money(_goal) + (_reached ? " (within reach)" : string.Empty)
                        + ", rent " + EconomyRules.Money(_due) + " per service, owed " + EconomyRules.Money(_rentOwed) + ", recipes " + _owned
                        + ", menu " + _menuNext + ", window " + string.Join(",", _window) + ", crew " + _players;
            }
        }

        /// <summary>Spec 057: the owned recipe rows of <paramref name="unlocks"/>.</summary>
        public static byte OwnedOf(ClimbCatalog climb, IReadOnlyList<string> unlocks)
        {
            byte owned = 0;
            if (unlocks == null) return owned;
            foreach (string id in unlocks)
            {
                int row = RestaurantMenu.RowOf(climb, id);
                if (row >= 0) owned = RestaurantMenu.With(owned, row, true);
            }
            return owned;
        }

        /// <summary>Spec 057: the menu of the owned rows that are not in <paramref name="menuOff"/>, cut to what fits.</summary>
        public static byte MenuOf(ClimbCatalog climb, byte owned, IReadOnlyList<string> menuOff)
        {
            byte menu = owned;
            if (menuOff != null)
                foreach (string id in menuOff)
                {
                    int row = RestaurantMenu.RowOf(climb, id);
                    if (row >= 0) menu = RestaurantMenu.With(menu, row, false);
                }
            return RestaurantMenu.Clamp(menu, owned, RestaurantRules.MaxDishes(climb));
        }

        /// <summary>Spec 057: the owned recipe ids that are off the next menu (the save's menuOff).</summary>
        public static string[] MenuOffOf(ClimbCatalog climb, byte owned, byte menu)
        {
            var off = new List<string>();
            IReadOnlyList<PurchaseDef> recipes = RestaurantRules.Recipes(climb);
            for (int i = 0; i < recipes.Count && i < RestaurantMenu.MaxRows; i++)
                if (RestaurantMenu.Has(owned, i) && !RestaurantMenu.Has(menu, i)) off.Add(recipes[i].Id);
            return off.ToArray();
        }

        public string[] ServerMenuOff => MenuOffOf(_climb, _owned, _menuNext);

        private long GoalFor(byte stage)
        {
            if (stage == ShopStageRules.FoodCorner) return RestaurantRules.RentGoal(EconomyData, _climb, _crew);
            if (stage >= ShopStageRules.FirstRestaurant) return RestaurantRules.GateCost(EconomyData, _climb, _crew);
            return 0L;
        }

        private int CountPlayers()
        {
            int clients = ConnectedPlayers != null ? ConnectedPlayers() : ServerUp && ServerManager != null ? ServerManager.Clients.Count : 0;
            return Math.Max(RestaurantRules.MinPlayers, Math.Min(RestaurantRules.MaxPlayers, clients));
        }

        /// <summary>
        /// 057 review: the one-time crew now (the largest of the last 3 service starts and the players connected now) and the stage's goal
        /// for it. Called at a load, a service start and end, and before every goal board press, so the price charged is the one shown.
        /// </summary>
        private void RefreshCrew()
        {
            _players = RestaurantRules.OneTimePlayers(_crewStarts, CountPlayers());
            _crew = RestaurantRules.Crew(_climb, _players);
            _goal = GoalFor(_stage);
        }

        private CustomerService Customers => _customers != null ? _customers : _customers = FindFirstObjectByType<CustomerService>();

        private bool ServiceOpen => _serviceOpen || (Customers != null && Customers.ServiceOpen);

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (ServerManager != null) ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            Push();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (ServerManager != null) ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
        }

        private void OnRemoteConnectionState(FishNet.Connection.NetworkConnection conn, FishNet.Transporting.RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == FishNet.Transporting.RemoteConnectionState.Stopped) ServerPlayerLeft(conn.ClientId);
        }

        /// <summary>057 review, server: a player left: an armed goal board press of theirs is forgotten.</summary>
        public void ServerPlayerLeft(int player) => _confirm.Disarm(player);

        private void Push()
        {
            if (!ServerUp) return;
            if (Stage.Value != _stage) Stage.Value = _stage;
            if (GoalMoney.Value != _goal) GoalMoney.Value = _goal;
            if (GoalReached.Value != _reached) GoalReached.Value = _reached;
            if (MovePending.Value != _movePending) MovePending.Value = _movePending;
            if (RecipesOwned.Value != _owned) RecipesOwned.Value = _owned;
            if (MenuNext.Value != _menuNext) MenuNext.Value = _menuNext;
            if (MenuNow.Value != _menuNow) MenuNow.Value = _menuNow;
            int gw = RestaurantRules.GateWindow(_climb), stars = RestaurantRules.GateStarsTenths(_climb);
            short average = (short)GateRules.AverageTenths(_window, gw);
            byte count = (byte)GateRules.Counted(_window, gw);
            byte pips = GateRules.Pips(_window, gw, stars);
            if (WindowAverageTenths.Value != average) WindowAverageTenths.Value = average;
            if (WindowCount.Value != count) WindowCount.Value = count;
            if (WindowPips.Value != pips) WindowPips.Value = pips;
            if (RentOwed.Value != _rentOwed) RentOwed.Value = _rentOwed;
            byte players = (byte)_players;
            if (CrewPlayers.Value != players) CrewPlayers.Value = players;
            if (RentLast.Value != _rentLast) RentLast.Value = _rentLast;
        }

        /// <summary>
        /// Server, a service opens: the crew is measured (D40) and the goal follows it; stage 1's fixed drop fills the potato slot; stage 2
        /// freezes the menu for this service and fixes its rent. Returns the units dropped.
        /// </summary>
        public int ServerServiceOpened()
        {
            _serviceOpen = true;
            int starting = CountPlayers();
            _crewStarts = RestaurantRules.PushCrew(_crewStarts, starting);
            _rentCrew = RestaurantRules.Crew(_climb, starting);
            RefreshCrew();
            int units = 0;
            if (_stage >= ShopStageRules.FirstRestaurant)
            {
                _menuNow = _menuNext;
                _due = RestaurantRules.Due(EconomyData, _climb, _rentCrew, _unlocks);
                Debug.Log("[Stage] the restaurant opens: crew " + starting + " (one-time prices for " + _players + "), menu " + _menuNow + ", rent and upkeep " + EconomyRules.Money(_due)
                    + ", licence " + EconomyRules.Money(_goal));
                if (_window.Length == 0 && _feed != null) _feed.ServerAdd(GoalBoardRules.OrderStockText);
            }
            else if (ShopStageRules.DropsStock(_stage) && _supply != null)
            {
                units = _supply.DropStock(FoodCornerRules.StockGood, out long cost);
                string text = FoodCornerRules.DeliveryText(units, cost); // follow-up 2026-11-11 evening: "Potatoes delivered -$96"
                Debug.Log("[Stage] " + text + " (crew " + _players + ", goal " + EconomyRules.Money(_goal) + ")");
                if (_feed != null) _feed.ServerAdd(text);
            }
            Push();
            return units;
        }

        /// <summary>
        /// Server, a service ended (after its summary numbers <paramref name="last"/>). Stage 1 pays the pitch fee, the goal flips once when the
        /// balance reached it, then a pending move happens. Stage 2 pays the rent and upkeep (RentRules), adds the service to the star window
        /// and flips the gate once when it holds. Returns what changed (the save's trigger).
        /// </summary>
        public StageEnd ServerServiceEnded(ShiftSummary last)
        {
            _serviceOpen = false;
            if (_economy == null) return StageEnd.None;
            if (_stage == ShopStageRules.FoodCorner)
            {
                bool flipped = false;
                if (_pitchFee > 0)
                {
                    long paid = _economy.PayFee(_pitchFee);
                    string fee = "Pitch fee for the corner: " + EconomyRules.Money(paid);
                    Debug.Log("[Stage] " + fee + ", the shop has " + EconomyRules.Money(_economy.ShopBalance));
                    if (_feed != null) _feed.ServerAdd(fee);
                }
                RefreshCrew();
                if (!_movePending && FoodCornerRules.ReachesGoal(_reached, _economy.ShopBalance, _goal))
                {
                    _reached = true;
                    flipped = true;
                    Debug.Log("[Stage] " + FoodCornerRules.GoalReachedText);
                    if (_feed != null) _feed.ServerAdd(FoodCornerRules.GoalReachedText);
                }
                Push();
                if (_movePending)
                {
                    Move(false); // the caller saves StageChanged
                    return StageEnd.Moved;
                }
                return flipped ? StageEnd.GoalReached : StageEnd.None;
            }
            if (_stage < ShopStageRules.FirstRestaurant) return StageEnd.None;

            long due = _due;
            long before = _economy.ShopBalance;
            RentRules.Result rent = RentRules.Charge(before, due, _rentOwed, RestaurantRules.RentReserve(EconomyData, _climb),
                RestaurantRules.OwedCap(due));
            long paidNow = rent.Paid > 0 ? _economy.PayFee(rent.Paid) : 0L;
            if (paidNow != rent.Paid) Debug.LogWarning("[Stage] the rent paid " + paidNow + " of " + rent.Paid);
            _rentOwed = rent.OwedAfter;
            _rentLast = paidNow;
            LastRent = rent;
            if (_feed != null)
            {
                _feed.ServerAdd(RentRules.PaidText(paidNow));
                if (_rentOwed > 0) _feed.ServerAdd(RentRules.OwedText(_rentOwed));
            }
            int tenths = GateRules.ServiceStarsTenths(last);
            _window = GateRules.Push(_window, tenths);
            // AC12 (measurement for the stage 2 calibration): one line per stage 2 service.
            RefreshCrew();
            Debug.Log("[Stage] restaurant service: crew " + _rentCrew.Players + ", guests " + (last.Served + last.Missed) + ", served " + last.Served
                + ", missed " + last.Missed + ", revenue " + EconomyRules.Money(last.Earned) + ", rent due " + EconomyRules.Money(due)
                + ", rent paid " + EconomyRules.Money(paidNow) + ", owed " + EconomyRules.Money(_rentOwed) + ", forgiven "
                + EconomyRules.Money(rent.Forgiven) + ", stars " + (tenths == GateRules.NotCounted ? "-" : GateRules.Stars(tenths))
                + ", window " + string.Join(",", _window) + ", balance " + EconomyRules.Money(_economy.ShopBalance));
            int gw = RestaurantRules.GateWindow(_climb), stars = RestaurantRules.GateStarsTenths(_climb);
            bool gate = !_reached && GateRules.GateHolds(_window, gw, stars, _economy.ShopBalance, _goal);
            if (gate)
            {
                _reached = true;
                Debug.Log("[Stage] " + GateRules.ReachedText);
                if (_feed != null) _feed.ServerAdd(GateRules.ReachedText);
            }
            Push();
            return gate ? StageEnd.GoalReached : StageEnd.None;
        }

        /// <summary>The move to the first restaurant (stage 1 to 2). The wallet, stock, hygiene, day, stats and unlocks carry over.</summary>
        private void Move(bool raise)
        {
            _stage = ShopStageRules.FirstRestaurant;
            _movePending = false;
            if (!_unlocks.Contains(FoodCornerRules.GoalPurchase)) _unlocks.Add(FoodCornerRules.GoalPurchase);
            _reached = false;
            _goal = GoalFor(_stage);
            _due = RestaurantRules.Due(EconomyData, _climb, _rentCrew ?? _crew, _unlocks);
            _window = Array.Empty<int>();
            _confirm.Clear();
            Push();
            if (_economy != null) _economy.SetMenuStage(_stage);
            Debug.Log("[Stage] " + GoalBoardRules.MovingText + ": " + Describe());
            if (_feed != null) _feed.ServerAdd(GoalBoardRules.MovingText);
            if (!raise) return;
            try { StageChanged?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>
        /// Spec 057, server: a press of goal board row <paramref name="row"/> by player <paramref name="player"/> (reach was checked). Rent and
        /// purchases need a second press of the same row by the same player within 4 s (BoardConfirm); toggles apply at once to the next
        /// menu. Refusals change nothing.
        /// </summary>
        public CookOutcome ServerPressBoard(int player, int row) => ServerPressBoard(player, row, Time.time);

        public CookOutcome ServerPressBoard(int player, int row, float now)
        {
            if (_economy == null) return CookOutcome.UnknownStation;
            RefreshCrew(); // 057 review: the crew now (a join since the service start raises the prices; a short drop does not lower them)
            Push();
            IReadOnlyList<PurchaseDef> recipes = RestaurantRules.Recipes(_climb);
            int count = Math.Min(GoalBoardRules.RowCount - 1, recipes.Count);
            PurchaseDef recipe = row >= 1 && row <= count ? recipes[row - 1] : null;
            long cost = recipe != null ? RestaurantRules.RecipeCost(recipe.Id, EconomyData, _climb, _crew) : 0L;
            var state = new GoalBoardRules.State(_stage, _economy.ShopBalance, _goal, _movePending, _owned, _menuNext, _rentOwed,
                RestaurantRules.RentReserve(EconomyData, _climb));
            CookOutcome outcome = GoalBoardRules.Decide(state, row, count, cost, RestaurantRules.MaxDishes(_climb), out GoalBoardAction action);
            if (outcome != CookOutcome.Ok || action == GoalBoardAction.None)
            {
                _confirm.Disarm(player);
                return outcome;
            }
            // 057 review: an armed purchase whose row got bought meanwhile (by another player) does nothing: no toggle by a second press.
            if (action == GoalBoardAction.Toggle && _confirm.IsArmed(player, row, now))
            {
                _confirm.Disarm(player);
                Debug.Log("[Stage] player " + player + " confirmed row " + row + ", which another player bought meanwhile: nothing changes");
                return CookOutcome.Ok;
            }
            if (GoalBoardRules.NeedsConfirm(action) && !_confirm.Press(player, row, now))
            {
                Debug.Log("[Stage] player " + player + " pressed goal board row " + row + " (" + action + "): press again within "
                    + BoardConfirm.WindowSeconds + " s");
                return CookOutcome.ConfirmAgain;
            }
            switch (action)
            {
                case GoalBoardAction.Rent:
                    if (!_economy.TrySpend(_goal, "the restaurant's rent")) return CookOutcome.NotEnoughMoney;
                    _movePending = true;
                    Push();
                    if (_feed != null)
                    {
                        _feed.ServerAdd(GoalBoardRules.RentPaidText(player, _goal));
                        _feed.ServerAdd(GoalBoardRules.RentedText);
                    }
                    Debug.Log("[Stage] player " + player + " rented the restaurant for " + EconomyRules.Money(_goal)
                        + (ServiceOpen ? "; the move waits for the service end" : "; moving now"));
                    if (!ServiceOpen) Move(true);
                    return CookOutcome.Ok;
                case GoalBoardAction.Buy:
                    if (!_economy.TrySpend(cost, recipe.Name)) return CookOutcome.NotEnoughMoney;
                    if (!_unlocks.Contains(recipe.Id)) _unlocks.Add(recipe.Id);
                    _owned = RestaurantMenu.With(_owned, row - 1, true);
                    byte withIt = RestaurantMenu.With(_menuNext, row - 1, true);
                    if (RestaurantMenu.Fits(withIt, _owned, RestaurantRules.MaxDishes(_climb))) _menuNext = withIt;
                    _due = RestaurantRules.Due(EconomyData, _climb, _rentCrew ?? _crew, _unlocks);
                    Push();
                    if (_feed != null)
                    {
                        _feed.ServerAdd(GoalBoardRules.BoughtText(player, recipe.Name, cost));
                        _feed.ServerAdd(RestaurantMenu.Has(_menuNext, row - 1) ? GoalBoardRules.NextServiceText
                            : GoalBoardRules.MenuFullText(RestaurantRules.MaxDishes(_climb)));
                    }
                    Debug.Log("[Stage] player " + player + " bought " + recipe.Id + " for " + EconomyRules.Money(cost) + "; next menu " + _menuNext);
                    return CookOutcome.Ok;
                case GoalBoardAction.Toggle:
                    Toggle(player, row - 1, recipe);
                    return CookOutcome.Ok;
                default:
                    return CookOutcome.Ok;
            }
        }

        /// <summary>
        /// Spec 059, server: the 057 recipe toggle from the office terminal's Menu app by player <paramref name="player"/>: owned recipe row
        /// <paramref name="recipeRow"/> on or off the next service's menu, by the same rules as the goal board's press (GoalBoardRules.Decide:
        /// the fries stay on, the menu holds MaxDishes, a change applies at the next service start). Only a toggle: buying stays at the goal
        /// board (its two presses). A row that is not owned is refused (NotOnMenu).
        /// </summary>
        public CookOutcome ServerToggleRecipe(int player, int recipeRow)
        {
            if (_economy == null) return CookOutcome.UnknownStation;
            IReadOnlyList<PurchaseDef> recipes = RestaurantRules.Recipes(_climb);
            int count = Math.Min(GoalBoardRules.RowCount - 1, recipes.Count);
            if (recipeRow < 0 || recipeRow >= count) return CookOutcome.InvalidOption;
            if (_stage < ShopStageRules.FirstRestaurant) return CookOutcome.AlwaysOn;
            if (!RestaurantMenu.Has(_owned, recipeRow)) return CookOutcome.NotOnMenu;
            var state = new GoalBoardRules.State(_stage, _economy.ShopBalance, _goal, _movePending, _owned, _menuNext, _rentOwed,
                RestaurantRules.RentReserve(EconomyData, _climb));
            CookOutcome outcome = GoalBoardRules.Decide(state, recipeRow + 1, count, 0L, RestaurantRules.MaxDishes(_climb), out GoalBoardAction action);
            if (outcome != CookOutcome.Ok) return outcome;
            if (action != GoalBoardAction.Toggle) return CookOutcome.InvalidOption;
            _confirm.Disarm(player); // like a toggle press at the board
            Toggle(player, recipeRow, recipes[recipeRow]);
            return CookOutcome.Ok;
        }

        /// <summary>Spec 059, server: a recipe went on or off the next menu: the player, the recipe row and whether it is on now (the office log).</summary>
        public event Action<int, int, bool> MenuToggled;

        /// <summary>057's toggle, shared by the goal board and the office terminal (spec 059: one way to switch a dish).</summary>
        private void Toggle(int player, int recipeRow, PurchaseDef recipe)
        {
            _menuNext = GoalBoardRules.Toggled(_menuNext, recipeRow);
            Push();
            bool on = RestaurantMenu.Has(_menuNext, recipeRow);
            if (_feed != null) _feed.ServerAdd(GoalBoardRules.ToggledText(GoalBoardRules.DishName(recipe), on));
            Debug.Log("[Stage] player " + player + " turned " + recipe.Id + (on ? " on" : " off") + "; next menu " + _menuNext);
            try { MenuToggled?.Invoke(player, recipeRow, on); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
