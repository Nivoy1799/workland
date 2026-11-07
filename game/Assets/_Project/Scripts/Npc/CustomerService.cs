using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using Worktown.Game.Economy;
using Worktown.Game.UI;
using Worktown.Sim.Cooking;
using Worktown.Sim.Core;

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
        [Tooltip("Spec 022: the ticket rail above the counter (orders and shift numbers for every client).")]
        [SerializeField] private TicketRailView _rail;

        private readonly CustomerQueue _queue = new CustomerQueue();
        private readonly Dictionary<int, Customer> _customers = new Dictionary<int, Customer>();
        private readonly Dictionary<int, float> _arrivedAt = new Dictionary<int, float>();
        private readonly List<QueuedCustomer> _leaving = new List<QueuedCustomer>();
        private readonly List<string> _joining = new List<string>();
        private readonly List<int> _tickets = new List<int>(CustomerRules.MaxVisible);
        private readonly Shift _shift = new Shift();
        private IRandom _orders = new SeededRandom(CustomerRules.OrderSeed);
        private bool _synced;
        private float _nextRailRefresh;

        private void Start()
        {
            if (_economy != null) _economy.HourTicked += OnHour;
            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
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
            _shift.Reset();
            _orders = new SeededRandom(CustomerRules.OrderSeed);
            _synced = false;
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
            if (Time.time >= _nextRailRefresh)
            {
                _nextRailRefresh = Time.time + RailRefreshSeconds;
                PublishTickets();
                PublishShift(); // only changed values are sent; also covers a rail spawned after the first frame
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

        /// <summary>14:00: everyone still waiting leaves (they were counted as missed by the shift).</summary>
        private void CloseQueue()
        {
            foreach (QueuedCustomer waiting in _queue.Entries)
                if (_customers.TryGetValue(waiting.Id, out Customer customer))
                    customer.ServerLeave("Kitchen closed...", CustomerRules.MoodMissed, _entry.position, OnExited);
            _queue.Clear();
            _arrivedAt.Clear();
            ShiftSummary s = _shift.Last;
            Debug.Log("[Customers] shift of day " + _shift.LastDay + " closed: served " + s.Served + ", earned "
                + EconomyRules.Money(s.Earned) + ", stars " + ShiftSummary.StarsText(s.AverageStarsTenths) + ", missed " + s.Missed);
        }

        private static bool HasDishes(string market) => Menu.HasDishes(market, Menu.CrateIngredients);

        /// <summary>
        /// Server, before a counter sale (pure read, nothing changes): who would get <paramref name="plated"/> and the
        /// pay share. The first exact match in queue order, else the first in line. False when nobody waits or the shift
        /// is closed (then the sale pays the full sim price as before).
        /// </summary>
        public bool TryPick(Item plated, int stars, out int customerId, out byte dish, out OrderMatch match, out int payPermille)
        {
            customerId = 0;
            dish = Menu.None;
            match = OrderMatch.Wrong;
            payPermille = EconomyRules.FullPayPermille;
            if (!_networkManager.IsServerStarted || !_shift.IsOpen) return false;
            if (!_queue.TryPick(d => OrderRules.Match(plated, Menu.ById(d)), out QueuedCustomer customer, out match)) return false;
            customerId = customer.Id;
            dish = customer.Dish;
            payPermille = OrderRules.PayPermille(match, stars);
            return true;
        }

        /// <summary>Server, after the sale is complete: the picked customer got their dish and paid <paramref name="pay"/>.</summary>
        public void OnServed(int customerId, OrderMatch match, long pay, int stars)
        {
            if (!_queue.Remove(customerId)) return;
            _arrivedAt.Remove(customerId);
            _shift.RecordServed(stars, pay);
            if (_customers.TryGetValue(customerId, out Customer customer))
            {
                Debug.Log("[Customers] served " + Menu.NameOf(customer.Dish.Value) + " order: " + OrderRules.Describe(match)
                    + ", paid " + EconomyRules.Money(pay));
                customer.ServerLeave(CustomerRules.LeaveText(match, pay), CustomerRules.MoodOf(match), _entry.position, OnExited);
            }
            Sync();
            PublishShift();
        }

        /// <summary>Matches the visible queue to the current open demand (nobody while the shift is closed).</summary>
        private void Sync()
        {
            if (!_networkManager.IsServerStarted) return;
            IReadOnlyList<string> desired = CustomerRules.DesiredWishes(_economy.OpenDemand, HasDishes, _shift.IsOpen);
            _queue.Reconcile(desired, _leaving, _joining);

            foreach (QueuedCustomer gone in _leaving)
            {
                _arrivedAt.Remove(gone.Id);
                if (_customers.TryGetValue(gone.Id, out Customer customer))
                    customer.ServerLeave("Too slow...", CustomerRules.MoodMissed, _entry.position, OnExited);
            }
            _shift.RecordMissed(_leaving.Count);

            // At most MaxVisible customer objects exist at once, including those still walking out;
            // the rest join when someone has left (OnExited syncs again). Review finding.
            int free = CustomerRules.MaxVisible - _customers.Count;
            int spawned = 0;
            foreach (string wish in _joining)
            {
                if (spawned >= free) break;
                MenuDish dish = Menu.Draw(_orders, wish, Menu.CrateIngredients);
                if (dish == null) continue; // cannot happen: only markets with dishes are desired
                NetworkObject nob = _networkManager.GetPooledInstantiated(_customerPrefab, true);
                nob.transform.SetPositionAndRotation(_entry.position, _entry.rotation);
                var customer = nob.GetComponent<Customer>();
                customer.ServerInit(wish, dish.Id, _entry.position); // before Spawn: values go out with the spawn
                _networkManager.ServerManager.Spawn(nob);
                _customers[nob.ObjectId] = customer;
                _queue.Add(nob.ObjectId, wish, dish.Id);
                _arrivedAt[nob.ObjectId] = Time.time;
                spawned++;
            }

            IReadOnlyList<QueuedCustomer> entries = _queue.Entries;
            for (int i = 0; i < entries.Count; i++)
                if (_customers.TryGetValue(entries[i].Id, out Customer customer))
                    customer.ServerSetTarget(CustomerRules.QueueSlot(_queueHead.position, _queueDirection, i));

            if (_leaving.Count > 0 || spawned > 0)
                Debug.Log("[Customers] queue " + entries.Count + ": " + spawned + " walking in, "
                    + _leaving.Count + " left unserved" + (spawned < _joining.Count ? ", " + (_joining.Count - spawned) + " waiting for room" : string.Empty));
            PublishTickets();
        }

        private void OnExited(Customer customer)
        {
            _customers.Remove(customer.ObjectId);
            if (customer.IsSpawned) _networkManager.ServerManager.Despawn(customer.NetworkObject);
            Sync(); // room for a waiting customer
        }

        /// <summary>The rail cards: every queued order with the in-game minutes it has waited.</summary>
        private void PublishTickets()
        {
            if (_rail == null || !_rail.IsSpawned) return;
            _tickets.Clear();
            float now = Time.time;
            foreach (QueuedCustomer entry in _queue.Entries)
            {
                float since = _arrivedAt.TryGetValue(entry.Id, out float at) ? at : now;
                _tickets.Add(CustomerRules.PackTicket(entry.Dish, CustomerRules.WaitMinutes(now - since)));
            }
            _rail.ServerSetTickets(_tickets);
        }

        private void PublishShift()
        {
            if (_rail != null && _rail.IsSpawned) _rail.ServerSetShift(_shift);
        }
    }
}
