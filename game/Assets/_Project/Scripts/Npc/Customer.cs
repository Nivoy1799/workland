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
    /// </summary>
    public sealed class Customer : NetworkBehaviour
    {
        private static readonly Color MealColor = new Color(0.95f, 0.55f, 0.2f);
        private static readonly Color SnackColor = new Color(0.4f, 0.8f, 0.35f);
        private static readonly Color DrinkColor = new Color(0.35f, 0.6f, 0.95f);

        public readonly SyncVar<string> Wish = new SyncVar<string>(string.Empty);
        public readonly SyncVar<string> Bubble = new SyncVar<string>(string.Empty);

        [SerializeField] private TextMesh _label;
        [SerializeField] private Renderer _body;

        private MaterialPropertyBlock _block;

        // Server only.
        private Vector3 _target;
        private bool _leaving;
        private Action<Customer> _onExit;

        private void Awake()
        {
            Wish.OnChange += (_, _, _) => Refresh();
            Bubble.OnChange += (_, _, _) => Refresh();
        }

        public override void OnStartClient()
        {
            Refresh();
        }

        internal void ServerInit(string wish, Vector3 target)
        {
            Wish.Value = wish;
            Bubble.Value = CustomerRules.WishText(wish);
            _target = target;
            _leaving = false;
            Refresh();
        }

        internal void ServerSetTarget(Vector3 target)
        {
            if (!_leaving) _target = target;
        }

        internal void ServerLeave(string bubble, Vector3 exit, Action<Customer> onExit)
        {
            Bubble.Value = bubble;
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
            if (_label != null) _label.text = Bubble.Value;
            if (_body == null) return;
            _block ??= new MaterialPropertyBlock();
            _block.SetColor("_BaseColor", ColorOf(Wish.Value));
            _body.SetPropertyBlock(_block);
        }

        private static Color ColorOf(string wish)
        {
            switch (wish)
            {
                case "meal": return MealColor;
                case "snack": return SnackColor;
                case "drink": return DrinkColor;
                default: return Color.gray;
            }
        }
    }
}
