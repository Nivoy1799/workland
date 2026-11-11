using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Player;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 032, client only: the guest takes the food. When a guest turns to paying (the server handed it the dish), a parcel of
    /// the dish (<see cref="HandoffRules.ParcelOf"/>: wrap paper, a bag, a box) flies from the serving counter into its right hand on
    /// a short ease-out arc while its arm comes up (the character's hold pose), and stays in the hand to the register and out of the
    /// shop. The server's bubble says thanks. A guest first seen while paying already holds it. Display only.
    /// Spec 044: an eat-in guest leaves its plate: after paying, the plate goes on the same arc onto the dish return and is gone there
    /// (<see cref="HandoffRules.LeavesPlate"/>); the arm comes down.
    /// </summary>
    [RequireComponent(typeof(Customer))]
    public sealed class GuestHandoffView : MonoBehaviour
    {
        [SerializeField] private ArtCatalog _catalog;

        private Customer _customer;
        private CharacterVisual _visual;
        private GameObject _parcel;
        private Vector3 _from;
        private float _start = float.NegativeInfinity;
        private bool _holding;
        private int _lastMood = -1;
        // Spec 044: the plate on its way to the dish return.
        private bool _returning;
        private Vector3 _returnTo;

        /// <summary>True while the guest holds its dish (tests and the tour log).</summary>
        public bool Holding => _holding && _parcel != null;

        /// <summary>Spec 044: the eat-in plate is on its way to the dish return (or there).</summary>
        public bool ReturningPlate => _returning && _parcel != null;

        /// <summary>The hand-off arc is still flying.</summary>
        public bool Flying => _holding && Time.unscaledTime - _start < HandoffRules.FlySeconds;

        private void Awake()
        {
            _customer = GetComponent<Customer>();
            _visual = GetComponent<CharacterVisual>();
            if (Application.isBatchMode) enabled = false;
        }

        private void OnDisable()
        {
            Drop();
            _lastMood = -1;
        }

        /// <summary>The synced mood, read once a frame (on a host the SyncVar's callbacks run for the server and the client).</summary>
        private void OnMood(int previous, byte next)
        {
            if (next == CustomerRules.MoodWaiting) Drop(); // a pooled guest starts over
            else if (next == CustomerRules.MoodPaying) Take(previous == CustomerRules.MoodWaiting); // fly from the counter; a late joiner holds it
            else if (_holding && _parcel != null && previous >= 0 && HandoffRules.LeavesPlate((byte)previous, next, _customer.EatIn.Value, _customer.Dish.Value))
                ReturnPlate();
        }

        /// <summary>Spec 044: the plate leaves the hand for the dish return (or the counter when there is none).</summary>
        private void ReturnPlate()
        {
            Station target = NearestStation(StationKind.DishReturn) ?? NearestStation(StationKind.Counter);
            _returnTo = target != null ? target.transform.position + Vector3.up * (ArtSizeTable.ServingCounterHeight * 0.5f + 0.05f)
                : transform.position + Vector3.up * 0.9f + transform.forward * 0.5f;
            _from = _parcel.transform.position;
            _start = Time.unscaledTime;
            _returning = true;
        }

        /// <summary>The dish into the hand: from the counter on an arc (<paramref name="fly"/>), or held at once (a late joiner).</summary>
        private void Take(bool fly)
        {
            Drop();
            ArtKey key = HandoffRules.ParcelOf(_customer.Dish.Value, _customer.EatIn.Value); // spec 035: an eat-in plate
            if (_catalog == null || !_catalog.TryGet(key, out GameObject prefab)) return;
            _parcel = Instantiate(prefab);
            _parcel.name = "Handed dish";
            foreach (Collider c in _parcel.GetComponentsInChildren<Collider>()) c.enabled = false;
            Station counter = NearestCounter();
            _from = counter != null ? counter.transform.position + Vector3.up * (ArtSizeTable.ServingCounterHeight * 0.5f + 0.05f)
                : transform.position + Vector3.up * 1.1f;
            _start = fly ? Time.unscaledTime : float.NegativeInfinity;
            _holding = true;
            _parcel.transform.position = fly ? _from : HandPoint();
        }

        private void Drop()
        {
            _holding = false;
            _returning = false;
            if (_parcel != null) Destroy(_parcel);
            _parcel = null;
        }

        private Vector3 HandPoint()
        {
            Transform hand = _visual != null ? _visual.Hand : null;
            return hand != null ? hand.position + transform.forward * 0.06f : transform.position + Vector3.up * 1.05f + transform.forward * 0.3f;
        }

        private void LateUpdate()
        {
            if (!_customer.IsSpawned) return;
            byte mood = _customer.Mood.Value;
            if (mood != _lastMood)
            {
                OnMood(_lastMood, mood);
                _lastMood = mood;
            }
            if (!_holding || _parcel == null) return;
            float t = (Time.unscaledTime - _start) / HandoffRules.FlySeconds;
            if (_returning)
            {
                // Spec 044: onto the dish return, then gone (the arm is down).
                if (t >= 1f) Drop();
                else _parcel.transform.position = HandoffRules.Arc(_from, _returnTo, t);
                return;
            }
            if (_visual != null) _visual.MarkHolding(); // the arm comes up and stays up
            Vector3 hand = HandPoint();
            Transform p = _parcel.transform;
            p.position = t >= 1f ? hand : HandoffRules.Arc(_from, hand, t);
            p.rotation = Quaternion.LookRotation(transform.forward, Vector3.up);
        }

        private Station NearestCounter() => NearestStation(StationKind.Counter);

        private Station NearestStation(StationKind kind)
        {
            Station best = null;
            float bestSqr = float.MaxValue;
            foreach (Station s in FindObjectsByType<Station>(FindObjectsSortMode.None))
            {
                if (s.Kind != kind) continue;
                float sqr = (s.transform.position - transform.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = s;
            }
            return best;
        }
    }
}
