using System;
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
    /// </summary>
    public sealed class Customer : NetworkBehaviour
    {
        // Spec 025/026: customer tints from the shared palette (one of the guide's customer tints): a meal is Mustard,
        // a snack Sage, a drink SoftBlue, an unknown wish Cream.
        // Spec 026: label colours by mood from the palette.
        private static readonly Color TicketColor = Palette.Cream;
        private static readonly Color HappyColor = Palette.Sage;
        private static readonly Color OkColor = Palette.Mustard;
        private static readonly Color UnhappyColor = Palette.Terracotta;
        private static readonly Color MissedColor = Palette.Steel;
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

        // Server only.
        private Vector3 _target;
        private Vector3 _facing; // spec 027a: the way to face once at the target (zero: keep the walking direction)
        private bool _leaving;
        private Action<Customer> _onExit;

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

        internal void ServerInit(string wish, byte dish, Vector3 target)
        {
            Wish.Value = wish;
            Dish.Value = dish;
            Mood.Value = CustomerRules.MoodWaiting;
            Bubble.Value = dish == Menu.None ? CustomerRules.WishText(wish) : CustomerRules.OrderText(dish);
            _target = target;
            _facing = Vector3.zero;
            _leaving = false;
            Refresh();
        }

        /// <summary>Spec 027a: <paramref name="facing"/> is the way to turn once the slot is reached (zero: none).</summary>
        internal void ServerSetTarget(Vector3 target, Vector3 facing)
        {
            if (_leaving) return;
            _target = target;
            facing.y = 0f;
            _facing = facing;
        }

        internal void ServerLeave(string bubble, byte mood, Vector3 exit, Action<Customer> onExit)
        {
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
            Vector3 next = Vector3.MoveTowards(position, _target, CustomerRules.WalkSpeed * Time.deltaTime);
            Vector3 step = next - position;
            step.y = 0f;
            if (step.sqrMagnitude > 0.000001f) transform.rotation = Quaternion.LookRotation(step);
            else if (!_leaving && _facing.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(_facing), TurnSpeed * Time.deltaTime);
            transform.position = next;
            if (_leaving && (next - _target).sqrMagnitude < 0.01f)
            {
                _leaving = false;
                _onExit?.Invoke(this);
            }
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
                case "snack": return Palette.Sage;
                case "drink": return Palette.SoftBlue;
                default: return Palette.Cream;
            }
        }
    }
}
