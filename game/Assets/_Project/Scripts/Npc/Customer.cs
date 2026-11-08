using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

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
        // Spec 025: customer tints from the palette in docs/art-direction.md (Mustard, Sage, SoftBlue; Cream for an
        // unknown wish). They move to the shared Palette class in spec 026.
        private static readonly Color MealColor = new Color(0xD9 / 255f, 0xA4 / 255f, 0x41 / 255f);  // Mustard #D9A441
        private static readonly Color SnackColor = new Color(0x8F / 255f, 0xA7 / 255f, 0x7E / 255f); // Sage #8FA77E
        private static readonly Color DrinkColor = new Color(0x7F / 255f, 0xA3 / 255f, 0xB8 / 255f); // SoftBlue #7FA3B8
        private static readonly Color OtherColor = new Color(0xF2 / 255f, 0xE6 / 255f, 0xD0 / 255f); // Cream #F2E6D0
        private static readonly Color TicketColor = new Color(1f, 0.92f, 0.55f);
        private static readonly Color HappyColor = new Color(0.55f, 1f, 0.55f);
        private static readonly Color OkColor = new Color(1f, 0.75f, 0.35f);
        private static readonly Color UnhappyColor = new Color(1f, 0.45f, 0.4f);
        private static readonly Color MissedColor = new Color(0.75f, 0.75f, 0.75f);
        /// <summary>Spec 024: how far the character's colours are pulled towards the wish colour (0 = none).</summary>
        public const float TintStrength = 0.4f;

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

        // Server only.
        private Vector3 _target;
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
            _leaving = false;
            Refresh();
        }

        internal void ServerSetTarget(Vector3 target)
        {
            if (!_leaving) _target = target;
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
                case "meal": return MealColor;
                case "snack": return SnackColor;
                case "drink": return DrinkColor;
                default: return OtherColor;
            }
        }
    }
}
