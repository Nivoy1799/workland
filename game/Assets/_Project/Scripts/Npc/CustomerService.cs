using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using Worktown.Game.Economy;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Server-side NPC customers (spec 014): keeps the visible queue in line with the economy's open
    /// demand, walks customers in and out, and lets one leave happily when a dish is sold to NPC demand.
    /// Lives next to the NetworkManager; does nothing on pure clients.
    /// </summary>
    public sealed class CustomerService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private EconomyService _economy;
        [SerializeField] private NetworkObject _customerPrefab;
        [Tooltip("Where customers come from and leave to (street entry).")]
        [SerializeField] private Transform _entry;
        [Tooltip("First queue slot, in front of the counter.")]
        [SerializeField] private Transform _queueHead;
        [SerializeField] private Vector3 _queueDirection = Vector3.right;

        private readonly CustomerQueue _queue = new CustomerQueue();
        private readonly Dictionary<int, Customer> _customers = new Dictionary<int, Customer>();
        private readonly List<QueuedCustomer> _leaving = new List<QueuedCustomer>();
        private readonly List<string> _joining = new List<string>();
        private bool _synced;

        private void Start()
        {
            if (_economy != null) _economy.HourTicked += Sync;
            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
        }

        private void OnDestroy()
        {
            if (_economy != null) _economy.HourTicked -= Sync;
            if (_networkManager != null && _networkManager.ServerManager != null)
                _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped || _networkManager.ServerManager.IsAnyServerStarted()) return;
            // FishNet despawns everything on stop; forget our references.
            _queue.Clear();
            _customers.Clear();
            _synced = false;
        }

        private void Update()
        {
            if (!_synced && _networkManager.IsServerStarted && _economy != null)
            {
                _synced = true;
                Sync();
            }
        }

        /// <summary>Server: a dish was sold. Only sales to NPC demand serve a customer.</summary>
        public void OnSale(string market, long payout)
        {
            if (!_queue.TryServe(market, out QueuedCustomer served)) return;
            if (_customers.TryGetValue(served.Id, out Customer customer))
                customer.ServerLeave("Thanks! +" + EconomyRules.Money(payout), _entry.position, OnExited);
            Debug.Log("[Customers] a " + served.Wish + " customer got their " + market + " (" + EconomyRules.Money(payout) + ")");
            Sync();
        }

        /// <summary>Matches the visible queue to the current open demand.</summary>
        private void Sync()
        {
            if (!_networkManager.IsServerStarted) return;
            IReadOnlyList<string> desired = CustomerRules.DesiredWishes(_economy.OpenDemand);
            _queue.Reconcile(desired, _leaving, _joining);

            foreach (QueuedCustomer gone in _leaving)
                if (_customers.TryGetValue(gone.Id, out Customer customer))
                    customer.ServerLeave("Too slow...", _entry.position, OnExited);

            // At most MaxVisible customer objects exist at once, including those still walking out;
            // the rest join when someone has left (OnExited syncs again). Review finding.
            int free = CustomerRules.MaxVisible - _customers.Count;
            int spawned = 0;
            foreach (string wish in _joining)
            {
                if (spawned >= free) break;
                NetworkObject nob = _networkManager.GetPooledInstantiated(_customerPrefab, true);
                nob.transform.SetPositionAndRotation(_entry.position, _entry.rotation);
                var customer = nob.GetComponent<Customer>();
                customer.ServerInit(wish, _entry.position); // before Spawn: values go out with the spawn
                _networkManager.ServerManager.Spawn(nob);
                _customers[nob.ObjectId] = customer;
                _queue.Add(nob.ObjectId, wish);
                spawned++;
            }

            IReadOnlyList<QueuedCustomer> entries = _queue.Entries;
            for (int i = 0; i < entries.Count; i++)
                if (_customers.TryGetValue(entries[i].Id, out Customer customer))
                    customer.ServerSetTarget(CustomerRules.QueueSlot(_queueHead.position, _queueDirection, i));

            if (_leaving.Count > 0 || spawned > 0)
                Debug.Log("[Customers] queue " + entries.Count + ": " + spawned + " walking in, "
                    + _leaving.Count + " left unserved" + (spawned < _joining.Count ? ", " + (_joining.Count - spawned) + " waiting for room" : string.Empty));
        }

        private void OnExited(Customer customer)
        {
            _customers.Remove(customer.ObjectId);
            if (customer.IsSpawned) _networkManager.ServerManager.Despawn(customer.NetworkObject);
            Sync(); // room for a waiting customer
        }
    }
}
