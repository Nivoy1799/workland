using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Worktown.Game.Art;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// A visible NPC customer (spec 014). The server walks it to its queue slot or out of the street;
    /// clients see the movement through a server-authoritative NetworkTransform and the bubble and
    /// color through SyncVars. Display only: customers never touch prices or payouts.
    /// Spec 022: the ordered dish id and the mood are SyncVars too; the label is the ticket (dish name) while it
    /// waits and what it says on the way out, coloured by mood.
    /// Spec 024: the body is a low-poly person (a CharacterVisual on the same object picks one of the customer variants
    /// and animates it); the wish colour is a light tint over the character's own colours (spec 025: palette colours).
    /// Spec 062a: the server walks it through a list of points (the layout's route corners, <see cref="ServerWalkVia"/>) before its
    /// target, at <see cref="CustomerRules.WalkSpeed"/>; with no corners it walks straight, as before.
    /// </summary>
    public sealed class Customer : NetworkBehaviour
    {
        // Spec 025/026: customer tints from the shared palette (one of the guide's customer tints): a meal is Mustard,
        // a snack Sage, a drink SoftBlue, an unknown wish Cream.
        // Spec 026: label colours by mood from the palette.
        private static readonly Color TicketColor = Palette.Cream;
        private static readonly Color HappyColor = Palette.Ground;
        private static readonly Color OkColor = Palette.Mustard;
        private static readonly Color UnhappyColor = Palette.Terracotta;
        private static readonly Color MissedColor = Palette.Chrome;
        /// <summary>Spec 024: how far the character's colours are pulled towards the wish colour (0 = none).</summary>
        public const float TintStrength = 0.4f;
        /// <summary>Spec 027a: how fast a customer at its slot turns to face the counter (degrees per second).</summary>
        private const float TurnSpeed = 360f;

        public readonly SyncVar<string> Wish = new SyncVar<string>(string.Empty);
        public readonly SyncVar<string> Bubble = new SyncVar<string>(string.Empty);
        /// <summary>Spec 022: the ordered menu dish id (<see cref="Menu.None"/> if none).</summary>
        public readonly SyncVar<byte> Dish = new SyncVar<byte>(Menu.None);
        /// <summary>Spec 022: <see cref="CustomerRules.MoodWaiting"/> and the others.</summary>
        public readonly SyncVar<byte> Mood = new SyncVar<byte>(CustomerRules.MoodWaiting);
        /// <summary>
        /// Spec 032: the patience left (255 full, 0 gone; GuestPatience.ToByte), for the ring over the guest. One byte, sent only
        /// when it changes (a few times a second while waiting, never while it pays): the server owns the clock, the client only draws.
        /// </summary>
        public readonly SyncVar<byte> Patience = new SyncVar<byte>(255);
        /// <summary>
        /// Spec 035: the guest eats in (its plate comes back dirty) or takes its order away. Presentation only, set once before the
        /// spawn: the hand-off shows the plate instead of a parcel. The server keeps its own record for the plates.
        /// </summary>
        public readonly SyncVar<bool> EatIn = new SyncVar<bool>(false);

        [SerializeField] private TextMesh _label;
        [Tooltip("Spec 024: every renderer of every character variant; tinted with the wish colour.")]
        [SerializeField] private Renderer[] _tinted = new Renderer[0];

        private MaterialPropertyBlock _block;

        /// <summary>Spec 027d: the order bubble (WorldLabelFade decides when and how it shows).</summary>
        public TextMesh Label => _label;
        /// <summary>Spec 027d: the full bubble text as synced (the label may show an icon instead).</summary>
        public string FullText => Bubble.Value;
        /// <summary>Spec 027d: still waiting for its order (not leaving with a remark).</summary>
        public bool Waiting => Mood.Value == CustomerRules.MoodWaiting && !string.IsNullOrEmpty(Bubble.Value);

        /// <summary>Spec 032: served and waiting at the register to pay.</summary>
        public bool Paying => Mood.Value == CustomerRules.MoodPaying;

        /// <summary>Spec 032, server: standing at its slot (not walking, not leaving): its patience drains only then.</summary>
        public bool ServerAtTarget
        {
            get
            {
                if (_leaving || _via.Count > 0) return false;
                Vector3 d = transform.position - _target;
                d.y = 0f;
                return d.sqrMagnitude < 0.0025f;
            }
        }

        // Server only.
        private Vector3 _target;
        private Vector3 _facing; // spec 027a: the way to face once at the target (zero: keep the walking direction)
        private bool _leaving;
        private Action<Customer> _onExit;
        private readonly List<Vector3> _via = new List<Vector3>(); // spec 062a: the corners still to walk before the target

        private void Awake()
        {
            Wish.OnChange += (_, _, _) => Refresh();
            Bubble.OnChange += (_, _, _) => Refresh();
            Mood.OnChange += (_, _, _) => Refresh();
        }

        public override void OnStartClient()
        {
            Refresh();
        }

        internal void ServerInit(string wish, byte dish, Vector3 target) => ServerInit(wish, dish, 0, target);

        /// <summary>Spec 032: with the order's packed modifiers: the bubble says them in plain words; patience starts full.</summary>
        internal void ServerInit(string wish, byte dish, byte modifiers, Vector3 target) => ServerInit(wish, dish, modifiers, 0, target);

        /// <summary>Spec 033: with the order's drink line (DrinkRules.DrinkByte, 0 none): the bubble adds "+ lemonade".</summary>
        internal void ServerInit(string wish, byte dish, byte modifiers, byte drink, Vector3 target)
        {
            EatIn.Value = false;
            ServerInitOrder(wish, dish, modifiers, drink, target);
        }

        /// <summary>Spec 035: with the way it eats: the bubble adds "for here" or "to go".</summary>
        internal void ServerInit(string wish, byte dish, byte modifiers, byte drink, bool eatIn, Vector3 target)
        {
            EatIn.Value = eatIn;
            ServerInitOrder(wish, dish, modifiers, drink, target);
            if (dish != Menu.None) Bubble.Value = CustomerRules.OrderText(dish, modifiers, drink, eatIn);
            Refresh();
        }

        private void ServerInitOrder(string wish, byte dish, byte modifiers, byte drink, Vector3 target)
        {
            Wish.Value = wish;
            Dish.Value = dish;
            Mood.Value = CustomerRules.MoodWaiting;
            Patience.Value = 255;
            Bubble.Value = dish == Menu.None ? CustomerRules.WishText(wish) : CustomerRules.OrderText(dish, modifiers, drink);
            _target = target;
            _facing = Vector3.zero;
            _leaving = false;
            _via.Clear(); // a pooled guest starts without the last one's corners
            Refresh();
        }

        /// <summary>Spec 062a, server: walk through <paramref name="points"/> (in order) before the target; replaces the corners left.</summary>
        internal void ServerWalkVia(IReadOnlyList<Vector3> points)
        {
            _via.Clear();
            if (points != null) _via.AddRange(points);
        }

        /// <summary>Spec 062a, server: the corners still to walk before the target.</summary>
        internal int ServerViaLeft => _via.Count;

        /// <summary>Spec 027a: <paramref name="facing"/> is the way to turn once the slot is reached (zero: none).</summary>
        internal void ServerSetTarget(Vector3 target, Vector3 facing)
        {
            if (_leaving) return;
            _target = target;
            facing.y = 0f;
            _facing = facing;
        }

        /// <summary>Spec 033, server: what the waiting guest says now (one line of its order came, the other is still open).</summary>
        internal void ServerSay(string bubble)
        {
            if (_leaving || Bubble.Value == bubble) return;
            Bubble.Value = bubble;
            Refresh();
        }

        /// <summary>Spec 032, server: the patience byte (only a change is sent).</summary>
        internal void ServerSetPatience(byte left)
        {
            if (Patience.Value != left) Patience.Value = left;
        }

        /// <summary>Spec 032, server: the guest took its food and walks to the register (<paramref name="target"/>) to pay.</summary>
        internal void ServerPay(string bubble, Vector3 target, Vector3 facing)
        {
            Bubble.Value = bubble;
            Mood.Value = CustomerRules.MoodPaying;
            _target = target;
            facing.y = 0f;
            _facing = facing;
            _leaving = false;
            Refresh();
        }

        internal void ServerLeave(string bubble, byte mood, Vector3 exit, Action<Customer> onExit) => ServerLeave(bubble, mood, exit, null, onExit);

        /// <summary>Spec 062a: leaves through <paramref name="via"/> (the route's corners out; null or empty: straight to the exit).</summary>
        internal void ServerLeave(string bubble, byte mood, Vector3 exit, IReadOnlyList<Vector3> via, Action<Customer> onExit)
        {
            ServerWalkVia(via);
            Bubble.Value = bubble;
            Mood.Value = mood;
            _target = exit;
            _leaving = true;
            _onExit = onExit;
            Refresh();
        }

        private void Update()
        {
            if (IsServerInitialized) ServerWalk();
        }

        private void ServerWalk()
        {
            Vector3 position = transform.position;
            Vector3 next = Step(position, _target, _via, CustomerRules.WalkSpeed * Time.deltaTime);
            Vector3 step = next - position;
            step.y = 0f;
            if (step.sqrMagnitude > 0.000001f) transform.rotation = Quaternion.LookRotation(step);
            else if (!_leaving && _facing.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(_facing), TurnSpeed * Time.deltaTime);
            transform.position = next;
            if (_leaving && _via.Count == 0 && (next - _target).sqrMagnitude < 0.01f)
            {
                _leaving = false;
                _onExit?.Invoke(this);
            }
        }

        /// <summary>
        /// Spec 062a, pure: one frame's walk of up to <paramref name="distance"/> from <paramref name="position"/>: through the corners in
        /// <paramref name="via"/> (each reached corner is removed; what is left of the step goes on to the next), then towards
        /// <paramref name="target"/>. Returns the new position.
        /// </summary>
        public static Vector3 Step(Vector3 position, Vector3 target, List<Vector3> via, float distance)
        {
            while (via != null && via.Count > 0)
            {
                float toCorner = Vector3.Distance(position, via[0]);
                if (toCorner > distance) return Vector3.MoveTowards(position, via[0], distance);
                position = via[0];
                distance -= toCorner;
                via.RemoveAt(0);
            }
            return Vector3.MoveTowards(position, target, distance);
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (_label != null && cam != null)
                _label.transform.rotation = Quaternion.LookRotation(_label.transform.position - cam.transform.position);
        }

        private void Refresh()
        {
            if (_label != null)
            {
                _label.text = Bubble.Value;
                _label.color = MoodColor(Mood.Value);
            }
            if (_tinted.Length == 0) return;
            _block ??= new MaterialPropertyBlock();
            _block.SetColor("_BaseColor", TintOf(Wish.Value));
            foreach (Renderer body in _tinted)
                if (body != null) body.SetPropertyBlock(_block);
        }

        /// <summary>Spec 024: white (the texture's own colours) pulled <see cref="TintStrength"/> towards the wish colour.</summary>
        public static Color TintOf(string wish) => Color.Lerp(Color.white, ColorOf(wish), TintStrength);

        private static Color MoodColor(byte mood)
        {
            switch (mood)
            {
                case CustomerRules.MoodHappy: return HappyColor;
                case CustomerRules.MoodOk: return OkColor;
                case CustomerRules.MoodUnhappy: return UnhappyColor;
                case CustomerRules.MoodMissed: return MissedColor;
                default: return TicketColor;
            }
        }

        private static Color ColorOf(string wish)
        {
            switch (wish)
            {
                case "meal": return Palette.Mustard;
                case "snack": return Palette.Ground;
                case "drink": return Palette.Sky;
                default: return Palette.Cream;
            }
        }
    }
}
