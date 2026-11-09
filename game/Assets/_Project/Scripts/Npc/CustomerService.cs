using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Net;
using Worktown.Game.Shop;
using Worktown.Game.UI;
using Worktown.Sim.Cooking;
using Worktown.Sim.Core;
using Worktown.Sim.Economy;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Server-side NPC customers (spec 014): keeps the visible queue in line with the economy's open
    /// demand, walks customers in and out, and lets one leave when a dish is sold to NPC demand.
    /// Spec 022: each customer orders a menu dish (drawn with a seeded RNG); a served plate goes to the first exact
    /// match in queue order, else the first in line, and the match sets the pay share. The shift (08:00 to 14:00
    /// in-game) opens and closes the queue; the summary is shown for 15 s, then the clock moves to the next 08:00.
    /// Orders, shift and numbers are published on the ticket rail. Lives next to the NetworkManager; does nothing on
    /// pure clients.
    /// Spec 032: orders 2.0. Each guest draws its modifiers, its patience base and its way of paying from the same seeded RNG
    /// (GuestOrders). Patience drains while it stands in the queue; when it runs out the guest leaves (missed). A guest handed its
    /// dish (<see cref="OnHandedOver"/>) takes it, walks to the register and waits there (no patience drain) until a cook takes the
    /// payment (<see cref="UseRegister"/>; 032 review: any cook, the money goes to the payee; the pure <see cref="RegisterFlow"/> and
    /// <see cref="RegisterDesk"/>): then it pays, leaves a review and goes. The kitchen closing, the payee leaving or 30 s without a
    /// touch settle a waiting payment by itself (the bill, no tip).
    /// Spec 033: half the guests also order a drink (its own seeded RNG, so the 022/032 draws keep their sequence). The dish and the
    /// drink are handed over on their own, in any order (<see cref="GuestTab"/>): a guest with a line still open stays in the queue
    /// (patience draining) and takes only that line; the last line sends it to the register with one bill for the whole order. A guest
    /// that leaves with a line still open pays what it got (no tip), once.
    /// </summary>
    public sealed class CustomerService : MonoBehaviour
    {
        private const float RailRefreshSeconds = 1f;

        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private EconomyService _economy;
        [SerializeField] private NetworkObject _customerPrefab;
        [Tooltip("Where customers come from and leave to (street entry).")]
        [SerializeField] private Transform _entry;
        [Tooltip("First queue slot, in front of the counter.")]
        [SerializeField] private Transform _queueHead;
        [SerializeField] private Vector3 _queueDirection = Vector3.right;
        [Tooltip("Spec 027a: the way waiting customers face once at their slot (towards the counter); zero keeps the walking direction.")]
        [SerializeField] private Vector3 _queueFacing = Vector3.zero;
        [Tooltip("Spec 022: the ticket rail above the counter (orders and shift numbers for every client).")]
        [SerializeField] private TicketRailView _rail;
        [Tooltip("Spec 032: the register's synced state (the head payer and the change tray).")]
        [SerializeField] private Register _register;
        [Tooltip("Spec 032: the register's station (its index for the reward moment over it).")]
        [SerializeField] private Station _registerStation;
        [Tooltip("Spec 032: where the first paying guest stands, in front of the register.")]
        [SerializeField] private Transform _payHead;
        [Tooltip("Spec 032: the line of paying guests runs this way from the pay spot.")]
        [SerializeField] private Vector3 _payDirection = Vector3.right;
        [Tooltip("Spec 032: the serve feed (the reward moment when a guest pays).")]
        [SerializeField] private ServeFeed _feed;
        [Tooltip("Spec 035: the shop's cleaning (its hygiene score shortens patience and costs review stars). Optional.")]
        [SerializeField] private CleaningService _cleaning;

        private readonly CustomerQueue _queue = new CustomerQueue();
        private readonly Dictionary<int, Customer> _customers = new Dictionary<int, Customer>();
        private readonly Dictionary<int, float> _arrivedAt = new Dictionary<int, float>();
        private readonly List<QueuedCustomer> _leaving = new List<QueuedCustomer>();
        private readonly List<string> _joining = new List<string>();
        private readonly List<int> _tickets = new List<int>(CustomerRules.MaxVisible);
        private readonly Shift _shift = new Shift();
        private IRandom _orders = new SeededRandom(CustomerRules.OrderSeed);
        private IRandom _drinkOrders = new SeededRandom(CustomerRules.DrinkSeed); // spec 033
        private IRandom _eatInOrders = new SeededRandom(CustomerRules.EatInSeed); // spec 035: its own RNG
        private readonly HashSet<int> _eatIn = new HashSet<int>(); // spec 035: the guests that eat in (their plate comes back dirty)
        private readonly GuestTab _tab = new GuestTab(); // spec 033: the order's lines (dish and drink)
        private readonly Dictionary<int, PickupInteractor> _linePayees = new Dictionary<int, PickupInteractor>();
        private bool _synced;
        private float _nextRailRefresh;

        // Spec 032: each guest's order details and patience, the register's line and who handed each paying guest its food.
        private sealed class Guest
        {
            public GuestOrder Order;
            public byte Modifiers;
        }

        private readonly Dictionary<int, Guest> _guests = new Dictionary<int, Guest>();
        private readonly PatienceBook _patience = new PatienceBook();
        private readonly List<KeyValuePair<int, byte>> _patienceBytes = new List<KeyValuePair<int, byte>>();
        private System.Func<int, bool> _atSlot;
        private RegisterFlow _flowField;
        private RegisterFlow Flow => _flowField ??= new RegisterFlow(_shift); // 032 review: the register's flow (pure, tested)
        private System.Func<int, IRegisterWallet> _walletFor;
        private readonly Dictionary<int, PickupInteractor> _payees = new Dictionary<int, PickupInteractor>();
        private readonly List<int> _outOfPatience = new List<int>();
        private readonly List<int> _payeeGone = new List<int>();
        private float _patienceScale = 1f;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private TourOrders _tourOrders;
#endif

        /// <summary>Spec 032, tests and the tour log: the register's line.</summary>
        public RegisterDesk Desk => Flow.Desk;

        private void Start()
        {
            if (_economy != null) _economy.HourTicked += OnHour;
            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // Spec 032, dev builds only: the cook tour's host scripts its first orders and gives guests more patience (the tour is
            // long); release builds never read the flag.
            if (TourOrders.Requested(System.Environment.GetCommandLineArgs()))
            {
                _tourOrders = new TourOrders();
                _patienceScale = TourOrders.PatienceScale;
                Debug.Log("[Customers] tour orders on (-tour-orders): scripted first orders, patience x" + _patienceScale);
            }
#endif
        }

        private void OnDestroy()
        {
            if (_economy != null) _economy.HourTicked -= OnHour;
            if (_networkManager != null && _networkManager.ServerManager != null)
                _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped || _networkManager.ServerManager.IsAnyServerStarted()) return;
            // FishNet despawns everything on stop; forget our references.
            _queue.Clear();
            _customers.Clear();
            _arrivedAt.Clear();
            _guests.Clear();
            _patience.Clear();
            Flow.Desk.Clear();
            _payees.Clear();
            _shift.Reset();
            _orders = new SeededRandom(CustomerRules.OrderSeed);
            _drinkOrders = new SeededRandom(CustomerRules.DrinkSeed);
            _eatInOrders = new SeededRandom(CustomerRules.EatInSeed);
            _eatIn.Clear();
            _tab.Clear();
            _linePayees.Clear();
            _synced = false;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (_tourOrders != null) _tourOrders = new TourOrders();
#endif
        }

        private void Update()
        {
            if (!_networkManager.IsServerStarted || _economy == null) return;
            if (!_synced)
            {
                _synced = true;
                OnHour();
            }
            if (_shift.UpdateSummary(Time.time))
            {
                // The 15 s summary is over: the night passes at once, the next shift opens at 08:00 (HourTicked).
                PublishShift();
                _economy.SkipHours(Shift.HoursToNextOpen(_economy.Tick));
            }
            Flow.HygienePercent = Hygiene; // spec 035: reviews notice a dirty shop
            UpdatePatience(Time.deltaTime, Time.time);
            SettleForPayeesWhoLeft();
            // 032 review: a head payment nobody took for 30 s settles by itself (bill only), so one cook away never blocks the till.
            if (Flow.Tick(Time.time, _walletFor ??= WalletFor, Stage))
            {
                PublishShift();
                PublishRegister();
            }
            if (Time.time >= _nextRailRefresh)
            {
                _nextRailRefresh = Time.time + RailRefreshSeconds;
                PublishTickets();
                PublishShift(); // only changed values are sent; also covers a rail spawned after the first frame
                PublishRegister();
            }
        }

        /// <summary>Every in-game hour (and once at start): open or close the shift, then match the queue to demand.</summary>
        private void OnHour()
        {
            if (!_networkManager.IsServerStarted) return;
            bool wasOpen = _shift.IsOpen;
            _shift.OnClock(_economy.Tick, Time.time, _queue.Entries.Count);
            if (wasOpen && !_shift.IsOpen) CloseQueue();
            else if (!_shift.IsOpen && !_shift.SummaryShowing && !Shift.IsShiftHour(_economy.Tick))
            {
                // Started outside the shift hours: move on to the next 08:00 (raises HourTicked again).
                PublishShift();
                _economy.SkipHours(Shift.HoursToNextOpen(_economy.Tick));
                return;
            }
            if (!wasOpen && _shift.IsOpen) Debug.Log("[Customers] shift of day " + _shift.Day + " opens");
            Sync();
            PublishShift();
        }

        /// <summary>14:00: everyone still waiting leaves (they were counted as missed by the shift); spec 032: those at the register pay.</summary>
        private void CloseQueue()
        {
            foreach (QueuedCustomer waiting in _queue.Entries)
            {
                PayOpenTab(waiting.Id, "the kitchen closed");
                _guests.Remove(waiting.Id);
                _patience.Remove(waiting.Id);
                if (_customers.TryGetValue(waiting.Id, out Customer customer))
                    customer.ServerLeave(CustomerRules.MissedText(MissReason.Closed), CustomerRules.MoodMissed, _entry.position, OnExited);
            }
            _queue.Clear();
            _arrivedAt.Clear();
            // Spec 032: the register settles every waiting payment by itself (the bill, no tip; the summary counts them).
            Flow.SettleAll(_walletFor ??= WalletFor, Stage);
            ShiftSummary sum = _shift.Last;
            Debug.Log("[Customers] shift of day " + _shift.LastDay + " closed: served " + sum.Served + ", earned "
                + EconomyRules.Money(sum.Earned) + " (tips " + EconomyRules.Money(sum.Tips) + "), stars "
                + ShiftSummary.StarsText(sum.AverageStarsTenths) + ", reviews " + sum.Reviews + " (" + ShiftSummary.StarsText(sum.AverageReviewTenths)
                + "), missed " + sum.Missed + " (patience " + sum.MissedPatience + ", gone " + sum.MissedGone + ", closed " + sum.MissedClosed + ")");
            PublishRegister();
        }

        private static bool HasDishes(string market) => Menu.HasDishes(market, Menu.CrateIngredients);

        /// <summary>Spec 035: the shop's hygiene score (100 without the cleaning service).</summary>
        private int Hygiene => _cleaning != null ? _cleaning.HygienePercent : HygieneRules.StartPercent;

        /// <summary>Spec 035, server: guest <paramref name="guestId"/> eats in (the plate it gets comes back dirty after its meal).</summary>
        public bool EatsIn(int guestId) => _eatIn.Contains(guestId);

        /// <summary>
        /// Spec 032, server, a pure read: the guest that gets <paramref name="plated"/> (the first in queue order whose order, with
        /// its modifiers, it matches exactly, else the first in line), how it fits, the bill's share and the wait. False when nobody
        /// waits or the shift is closed.
        /// </summary>
        public bool TryPick(Item plated, out GuestPick pick)
        {
            pick = default;
            if (!_networkManager.IsServerStarted || !_shift.IsOpen) return false;
            // Spec 033: a lidded cup goes to a guest still waiting for its drink, anything else to one still waiting for its dish.
            bool drink = Drinks.IsCup(plated);
            if (drink)
            {
                if (!_queue.TryPick(e => _tab.WantsDrink(e.Id), e => OrderRules.FitDrink(plated, _tab.DrinkOf(e.Id)).Match,
                        out QueuedCustomer thirsty, out _))
                    return false;
                OrderFit cupFit = OrderRules.FitDrink(plated, _tab.DrinkOf(thirsty.Id));
                _patience.Get(thirsty.Id, out float waitedFor, out float patienceFor);
                pick = new GuestPick(thirsty.Id, thirsty.Dish, cupFit, OrderRules.GuestSharePermille(cupFit.Match), Tenths(waitedFor),
                    Tenths(patienceFor), true);
                return true;
            }
            if (!_queue.TryPick(e => _tab.WantsFood(e.Id), e => OrderRules.Match(plated, Menu.ById(e.Dish), e.Modifiers),
                    out QueuedCustomer customer, out _))
                return false;
            OrderFit fit = OrderRules.Fit(plated, Menu.ById(customer.Dish), customer.Modifiers);
            float itemQuality = ItemRater.Rate(plated).Quality;
            _patience.Get(customer.Id, out float waitedSeconds, out float patienceSeconds);
            int waited = Tenths(waitedSeconds);
            int patience = Tenths(patienceSeconds);
            pick = new GuestPick(customer.Id, customer.Dish, fit, OrderRules.BillPermille(fit.Match, itemQuality, fit.Quality), waited, patience);
            return true;
        }

        private static int Tenths(float seconds) => seconds <= 0f ? 0 : (int)System.Math.Min(int.MaxValue, System.Math.Round(seconds * 10f));

        /// <summary>Spec 032 (review): guest <paramref name="guestId"/> waits in the register's line (it pays there, exactly once).</summary>
        public bool IsPaying(int guestId) => Flow.Desk.IsWaiting(guestId);

        /// <summary>
        /// Spec 033: the line of <paramref name="pick"/> is billed already: its guest waits in the register's line, or the line is on its
        /// tab (it pays for it with the rest of its order, or when it leaves). The counter's fallback never pays such a line again.
        /// </summary>
        public bool HasBill(GuestPick pick) => Flow.Desk.IsWaiting(pick.CustomerId) || _tab.IsBilled(pick.CustomerId, pick.IsDrink);

        /// <summary>
        /// 033 review: what the counter's fallback pays for <paramref name="pick"/> when its hand-over failed: the combined bill when the
        /// order's last line came but the guest never reached the register's line (once), else the line's own bill.
        /// </summary>
        public long FallbackBill(GuestPick pick, long simPayout) => FallbackBill(_tab, pick, simPayout);

        /// <summary>Pure core of <see cref="FallbackBill(GuestPick, long)"/> (tested).</summary>
        public static long FallbackBill(GuestTab tab, GuestPick pick, long simPayout) =>
            tab != null && tab.TakeCompletedBill(pick.CustomerId, out long whole) ? whole : GuestTab.LineBill(pick, simPayout);

        /// <summary>
        /// 033 review: whether the counter's fallback pays for this hand-over: not handed over, and its money not committed elsewhere. A
        /// line billed already before this hand-over (<paramref name="billedBefore"/>: a duplicate) is no commitment for this dish.
        /// </summary>
        public static bool FallbackPaysFor(bool handed, bool billedBefore, bool billedAfter) =>
            Worktown.Game.Shop.RegisterFlow.FallbackPays(handed, billedAfter && !billedBefore);

        /// <summary>
        /// Spec 032, server, after the sim sold the dish (credited nothing yet: TrySellForGuest): guest <paramref name="pick"/> takes it
        /// from <paramref name="payee"/>, walks to the register and waits to pay its bill (<paramref name="simPayout"/> at the pick's
        /// share) and its tip. False only when the guest is gone (then the caller pays the bill at once). 032 review: the register's
        /// line is the commit point (RegisterFlow.HandOver); once the guest is in it this returns true whatever the presentation does.
        /// Spec 033: an order with a drink takes its lines one by one (<see cref="GuestTab"/>); a line that leaves another open goes on
        /// the guest's tab (the commit point for it; true) and the guest waits in the queue for the rest.
        /// </summary>
        public bool OnHandedOver(GuestPick pick, PickupInteractor payee, long simPayout)
        {
            if (payee == null || _queue.IndexOf(pick.CustomerId) < 0) return false;
            // 033 review: a line this guest already got is refused (nothing changes); the counter's fallback pays it at once.
            if (_tab.IsDuplicate(pick)) return false;
            // Spec 033: one line of the order: on the tab (the commit point for a line) until the last one comes; then one pick for all.
            if (!_tab.Serve(pick, payee.OwnerId, GuestTab.LineBill(pick, simPayout), out GuestPick whole, out long bill))
            {
                _linePayees[pick.CustomerId] = payee;
                try
                {
                    QueuedCustomer entry = _queue.Entries[_queue.IndexOf(pick.CustomerId)];
                    if (_customers.TryGetValue(pick.CustomerId, out Customer waiting) && waiting != null)
                        waiting.ServerSay(CustomerRules.StillWaitingText(_tab.FoodServed(pick.CustomerId), entry.Dish, entry.Drink));
                    Debug.Log("[Customers] guest " + pick.CustomerId + " took its " + (pick.IsDrink ? "drink" : Menu.NameOf(pick.Dish)) + " ("
                        + OrderRules.Describe(pick.Fit.Match) + ", line bill " + EconomyRules.Money(GuestTab.LineBill(pick, simPayout))
                        + "); the rest of its order is still open");
                    PublishTickets();
                }
                catch (System.Exception e) { Debug.LogException(e); }
                return true;
            }
            _linePayees.Remove(pick.CustomerId);
            _queue.Remove(pick.CustomerId);
            _arrivedAt.Remove(pick.CustomerId);
            _guests.TryGetValue(pick.CustomerId, out Guest g);
            _guests.Remove(pick.CustomerId);
            _patience.Remove(pick.CustomerId); // spec 032: no patience at the register
            _payees[pick.CustomerId] = payee;
            Flow.HandOver(whole, payee.OwnerId, bill, g != null ? g.Order : null, Stage, Time.time);
            _tab.Close(pick.CustomerId); // 033 review: only now is the combined bill the register's
            try
            {
                Sync();
                PublishShift();
                PublishRegister();
            }
            catch (System.Exception e) { Debug.LogException(e); }
            return true;
        }

        /// <summary>
        /// Spec 032, server: one click on the register (RequestStation; reach was checked) by any cook: a coin or note into the change,
        /// take it back, hand it over, or the card terminal. The pure desk decides and pays the payee once; a refusal changes and pays
        /// nothing.
        /// </summary>
        public CookOutcome UseRegister(PickupInteractor player, int option)
        {
            if (player == null) return CookOutcome.NoPayment;
            RegisterPayment head = Flow.Desk.Head;
            CookOutcome outcome = Flow.Use(player.OwnerId, option, head != null ? WalletFor(head.GuestId) : null, Stage, Time.time);
            PublishShift();
            PublishRegister();
            return outcome;
        }

        /// <summary>Spec 032: the payee of a waiting payment left the server: the guest pays its bill by itself (no tip), the money is still theirs.</summary>
        private void SettleForPayeesWhoLeft()
        {
            if (_payees.Count == 0) return;
            _payeeGone.Clear();
            foreach (KeyValuePair<int, PickupInteractor> kv in _payees)
                if (kv.Value == null || !kv.Value.IsSpawned) _payeeGone.Add(kv.Key);
            foreach (int guest in _payeeGone)
            {
                _payees.Remove(guest);
                Flow.SettleAutomatically(guest, WalletFor(guest), Stage, "the payee left");
            }
            if (_payeeGone.Count > 0)
            {
                PublishShift();
                PublishRegister();
            }
        }

        /// <summary>Spec 032 (review): what the register's flow shows, through the guests' objects, the feed and the reward moment.</summary>
        private IGuestStage Stage => _stage ??= new ServiceStage(this);
        private IGuestStage _stage;

        private sealed class ServiceStage : IGuestStage
        {
            private readonly CustomerService _s;

            public ServiceStage(CustomerService service) => _s = service;

            public void Paying(int guestId, string bubble, int lineIndex)
            {
                if (_s._customers.TryGetValue(guestId, out Customer c) && c != null)
                    c.ServerPay(bubble, _s.PaySlot(lineIndex), _s._queueFacing);
            }

            public void Leave(int guestId, string bubble, byte mood)
            {
                if (_s._customers.TryGetValue(guestId, out Customer c) && c != null)
                    c.ServerLeave(bubble, mood, _s._entry.position, _s.OnExited);
            }

            public void LineMoved() => _s.MovePayers();

            public void Feed(string line)
            {
                if (_s._feed != null) _s._feed.ServerAdd(line);
            }

            public void Reward(int stars, long paid, long tip)
            {
                if (_s._feed != null) _s._feed.ServerReward(_s._registerStation != null ? _s._registerStation.Index : 0, stars, paid, tip);
            }

            public void Log(string line) => Debug.Log(line);
        }

        /// <summary>The wallet of the payee of guest <paramref name="guestId"/> (null without an economy).</summary>
        private IRegisterWallet WalletFor(int guestId)
        {
            if (_economy == null) return null;
            _payees.TryGetValue(guestId, out PickupInteractor payee);
            return new GuestWallet(_economy, payee);
        }

        /// <summary>Spec 032: <see cref="IRegisterWallet"/> through the economy service (the payee may have left; its account stays).</summary>
        private sealed class GuestWallet : IRegisterWallet
        {
            private readonly EconomyService _economy;
            private readonly PickupInteractor _payee;

            public GuestWallet(EconomyService economy, PickupInteractor payee)
            {
                _economy = economy;
                _payee = payee;
            }

            public CookOutcome Pay(int payee, long amount, out long paid)
            {
                NetworkObject nob = _payee != null && _payee.IsSpawned && _payee.OwnerId == payee ? _payee.NetworkObject : null;
                return _economy.TryPayGuest(payee, nob, amount, out paid) ? CookOutcome.Ok : CookOutcome.SimRefused;
            }
        }

        /// <summary>
        /// Spec 032: patience drains for every guest standing at its queue slot (the pure <see cref="PatienceBook"/>); one whose
        /// patience ran out leaves (missed, reason Patience). The ring's byte goes out at most every <see cref="PatienceBook.SyncSeconds"/>.
        /// </summary>
        private void UpdatePatience(float dt, float now)
        {
            if (!_shift.IsOpen || _patience.Count == 0) return;
            _atSlot ??= id => _customers.TryGetValue(id, out Customer c) && c != null && c.ServerAtTarget;
            _patience.Tick(dt, now, _queue.Entries, _atSlot, _outOfPatience, _patienceBytes);
            foreach (KeyValuePair<int, byte> kv in _patienceBytes)
                if (_customers.TryGetValue(kv.Key, out Customer c)) c.ServerSetPatience(kv.Value);
            if (_outOfPatience.Count == 0) return;
            foreach (int id in _outOfPatience)
            {
                PayOpenTab(id, "out of patience");
                _queue.Remove(id);
                _arrivedAt.Remove(id);
                _guests.Remove(id);
                if (_customers.TryGetValue(id, out Customer customer))
                {
                    customer.ServerSetPatience(0);
                    customer.ServerLeave(CustomerRules.MissedText(MissReason.Patience), CustomerRules.MoodMissed, _entry.position, OnExited);
                    Debug.Log("[Customers] a guest ran out of patience (" + Menu.NameOf(customer.Dish.Value) + ")");
                }
            }
            _shift.RecordMissed(_outOfPatience.Count, MissReason.Patience);
            Sync();
            PublishShift();
        }

        /// <summary>Matches the visible queue to the current open demand (nobody while the shift is closed).</summary>
        private void Sync()
        {
            if (!_networkManager.IsServerStarted) return;
            IReadOnlyList<string> desired = CustomerRules.DesiredWishes(_economy.OpenDemand, HasDishes, _shift.IsOpen);
            // Spec 033: a guest whose dish came already (sold to the demand) still waits for its drink: it keeps its place in the
            // reconcile, so nobody else is sent away for the dish it got.
            if (_tab.Count > 0 && _shift.IsOpen) desired = CustomerRules.KeepOpenOrders(desired, _queue.Entries, _tab.FoodServed);
            _queue.Reconcile(desired, _leaving, _joining);

            foreach (QueuedCustomer gone in _leaving)
            {
                PayOpenTab(gone.Id, "changed its mind");
                _arrivedAt.Remove(gone.Id);
                _guests.Remove(gone.Id);
                _patience.Remove(gone.Id);
                if (_customers.TryGetValue(gone.Id, out Customer customer))
                    customer.ServerLeave(CustomerRules.MissedText(MissReason.Gone), CustomerRules.MoodMissed, _entry.position, OnExited);
            }
            _shift.RecordMissed(_leaving.Count, MissReason.Gone);

            // At most MaxVisible customer objects exist at once, including those still walking out and (spec 032) those paying;
            // the rest join when someone has left (OnExited syncs again). Review finding.
            int free = CustomerRules.MaxVisible - _customers.Count;
            int spawned = 0;
            foreach (string wish in _joining)
            {
                if (spawned >= free) break;
                MenuDish dish = DrawDish(wish, out GuestOrder order, out float patienceScale, out string drink);
                if (dish == null) continue; // cannot happen: only markets with dishes are desired
                RecipeTemplate template = dish.Template != null ? RecipeTemplates.ById(dish.Template) : null;
                byte modifiers = OrderModifiers.Pack(template, order.Modifiers);
                NetworkObject nob = _networkManager.GetPooledInstantiated(_customerPrefab, true);
                nob.transform.SetPositionAndRotation(_entry.position, _entry.rotation);
                var customer = nob.GetComponent<Customer>();
                byte drinkByte = DrinkRules.DrinkByte(drink); // spec 033
                bool eatIn = GuestOrders.DrawEatIn(_eatInOrders); // spec 035: its own RNG, one draw per guest
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (_tourOrders != null && _tourOrders.LastScripted) eatIn = _tourOrders.LastEatIn; // the tour's scripted guests
#endif
                customer.ServerInit(wish, dish.Id, modifiers, drinkByte, eatIn, _entry.position); // before Spawn: values go out with the spawn
                _networkManager.ServerManager.Spawn(nob);
                _customers[nob.ObjectId] = customer;
                _queue.Add(nob.ObjectId, wish, dish.Id, modifiers, drinkByte);
                _tab.Open(nob.ObjectId, drink);
                _arrivedAt[nob.ObjectId] = Time.time;
                _guests[nob.ObjectId] = new Guest { Order = order, Modifiers = modifiers };
                if (eatIn) _eatIn.Add(nob.ObjectId);
                else _eatIn.Remove(nob.ObjectId);
                // Spec 033: a drink is one more layer of work for the patience. Spec 035: a dirty shop (below 30 %) makes it 20 % shorter.
                _patience.Add(nob.ObjectId, CustomerRules.PatienceFor(order, CustomerRules.LayersOf(dish, modifiers), drink != null, patienceScale,
                    Hygiene));
                spawned++;
            }

            IReadOnlyList<QueuedCustomer> entries = _queue.Entries;
            for (int i = 0; i < entries.Count; i++)
                if (_customers.TryGetValue(entries[i].Id, out Customer customer))
                    customer.ServerSetTarget(CustomerRules.QueueSlot(_queueHead.position, _queueDirection, i), _queueFacing);

            if (_leaving.Count > 0 || spawned > 0)
                Debug.Log("[Customers] queue " + entries.Count + ": " + spawned + " walking in, "
                    + _leaving.Count + " left unserved" + (spawned < _joining.Count ? ", " + (_joining.Count - spawned) + " waiting for room" : string.Empty));
            PublishTickets();
        }

        /// <summary>Spec 032: the next guest's dish (022's seeded draw) and its order details (the same RNG, GuestOrders).</summary>
        private MenuDish DrawDish(string wish, out GuestOrder order, out float patienceScale, out string drink)
        {
            patienceScale = _patienceScale;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (_tourOrders != null && _tourOrders.TryNext(wish, out MenuDish scripted, out order, out patienceScale, out drink)) return scripted;
#endif
            MenuDish dish = Menu.Draw(_orders, wish, Menu.CrateIngredients);
            order = GuestOrders.Draw(_orders, dish != null && dish.Template != null ? RecipeTemplates.ById(dish.Template) : null);
            drink = dish != null ? Drinks.DrawOrderDrink(_drinkOrders) : null; // spec 033: its own RNG
            return dish;
        }

        /// <summary>
        /// Spec 033: guest <paramref name="guestId"/> leaves (<paramref name="why"/>) with a line of its order still open: it pays what it
        /// got (the lines' bill, no tip) to the cook who handed it over, once (the tab is closed), and the shift counts the sale.
        /// </summary>
        private void PayOpenTab(int guestId, string why)
        {
            _linePayees.TryGetValue(guestId, out PickupInteractor payee);
            _linePayees.Remove(guestId);
            if (!_tab.TakeOpenBill(guestId, out long bill, out int payeeId, out int stars, out int misses)) return;
            try
            {
                NetworkObject nob = payee != null && payee.IsSpawned && payee.OwnerId == payeeId ? payee.NetworkObject : null;
                long paid = 0;
                if (_economy != null && bill > 0 && !_economy.TryPayGuest(payeeId, nob, bill, out paid))
                    Debug.LogError("[Customers] could not pay " + EconomyRules.Money(bill) + " for guest " + guestId + "'s open order");
                GuestReview review = ReviewRules.Review(stars, 0, 1, misses, false, Hygiene); // spec 035
                _shift.RecordSale(stars, paid, 0, review);
                if (_feed != null) _feed.ServerAdd("Guest paid " + EconomyRules.Money(paid) + " for what it got (" + why + ", no tip)");
                Debug.Log("[Customers] guest " + guestId + " left with its order open (" + why + "): paid " + EconomyRules.Money(paid)
                    + " to player " + payeeId);
            }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        /// <summary>Spec 032: the paying guests stand in line at the register, the first in front of it.</summary>
        private void MovePayers()
        {
            IReadOnlyList<RegisterPayment> line = Flow.Desk.Line;
            for (int i = 0; i < line.Count; i++)
                if (_customers.TryGetValue(line[i].GuestId, out Customer customer))
                    customer.ServerSetTarget(PaySlot(i), _queueFacing);
        }

        private Vector3 PaySlot(int index)
        {
            Vector3 head = _payHead != null ? _payHead.position : _queueHead.position;
            return CustomerRules.QueueSlot(head, _payDirection, index);
        }

        private void OnExited(Customer customer)
        {
            PayOpenTab(customer.ObjectId, "the guest is gone"); // spec 033: normally paid when it left the queue already
            _customers.Remove(customer.ObjectId);
            _guests.Remove(customer.ObjectId);
            _patience.Remove(customer.ObjectId);
            _eatIn.Remove(customer.ObjectId); // spec 035
            // 032 review: a guest whose object went while it waited to pay settles by itself (bill only), so no money is lost.
            if (Flow.Desk.IsWaiting(customer.ObjectId)) Flow.Exited(customer.ObjectId, WalletFor(customer.ObjectId), Stage);
            _payees.Remove(customer.ObjectId);
            if (customer.IsSpawned) _networkManager.ServerManager.Despawn(customer.NetworkObject);
            Sync(); // room for a waiting customer
        }

        /// <summary>The rail cards: every queued order with the in-game minutes it has waited and (spec 032) its modifiers.</summary>
        private void PublishTickets()
        {
            if (_rail == null || !_rail.IsSpawned) return;
            _tickets.Clear();
            float now = Time.time;
            foreach (QueuedCustomer entry in _queue.Entries)
            {
                float since = _arrivedAt.TryGetValue(entry.Id, out float at) ? at : now;
                _tickets.Add(CustomerRules.PackTicket(entry.Dish, CustomerRules.WaitMinutes(now - since), entry.Modifiers,
                    _tab.WantsDrink(entry.Id) ? entry.Drink : (byte)0, _tab.FoodServed(entry.Id))); // spec 033: the open drink line
            }
            _rail.ServerSetTickets(_tickets);
        }

        private void PublishShift()
        {
            if (_rail != null && _rail.IsSpawned) _rail.ServerSetShift(_shift);
        }

        /// <summary>Spec 032: the register's head payer and tray for every client.</summary>
        private void PublishRegister()
        {
            if (_register == null || !_register.IsSpawned) return;
            RegisterPayment head = Flow.Desk.Head;
            _register.ServerSet(RegisterRules.PackPayment(head != null, head != null && head.Card, head != null ? head.ChangeDue : 0),
                head != null ? head.Tray : 0);
        }
    }
}
