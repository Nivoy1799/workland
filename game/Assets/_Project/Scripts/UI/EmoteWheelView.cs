using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Worktown.Game.Social;

namespace Worktown.Game.UI
{
    /// <summary>
    /// Emote wheel and quick-chat list (spec 012), UI Toolkit. Driven by the local SocialInteractor:
    /// it only displays; selection logic is SocialRules.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class EmoteWheelView : MonoBehaviour
    {
        private const float Radius = 150f;

        [SerializeField] private StyleSheet _style;

        private VisualElement _wheel;
        private readonly List<Label> _slots = new List<Label>();
        private Label _centre;
        private VisualElement _chat;
        private int _selected = -2;

        private void Start()
        {
            if (Application.isBatchMode) return;
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            if (_style != null && !root.styleSheets.Contains(_style)) root.styleSheets.Add(_style);

            _wheel = new VisualElement { pickingMode = PickingMode.Ignore };
            _wheel.AddToClassList("wheel-root");
            var ring = new VisualElement { pickingMode = PickingMode.Ignore };
            ring.AddToClassList("wheel-ring");
            _wheel.Add(ring);
            _centre = new Label("Emote") { pickingMode = PickingMode.Ignore };
            _centre.AddToClassList("wheel-centre");
            ring.Add(_centre);
            for (int i = 0; i < EmoteCatalog.All.Count; i++)
            {
                float angle = i * (360f / EmoteCatalog.All.Count) * Mathf.Deg2Rad;
                var slot = new Label(EmoteCatalog.All[i].Text) { pickingMode = PickingMode.Ignore };
                slot.AddToClassList("panel");
                slot.AddToClassList("wheel-slot");
                slot.style.left = Radius + Mathf.Sin(angle) * Radius - 60f;
                slot.style.top = Radius - Mathf.Cos(angle) * Radius - 16f;
                ring.Add(slot);
                _slots.Add(slot);
            }
            root.Add(_wheel);

            _chat = new VisualElement { pickingMode = PickingMode.Ignore };
            _chat.AddToClassList("panel");
            _chat.AddToClassList("chat-list");
            var title = new Label("Quick chat: press a number") { pickingMode = PickingMode.Ignore };
            title.AddToClassList("banner-heading");
            _chat.Add(title);
            for (int i = 0; i < QuickChatCatalog.All.Count; i++)
                _chat.Add(new Label((i + 1) + "  " + QuickChatCatalog.All[i].Text) { pickingMode = PickingMode.Ignore });
            root.Add(_chat);

            SetWheelOpen(false);
            SetQuickChatOpen(false);
        }

        public void SetWheelOpen(bool open)
        {
            if (_wheel == null) return;
            _wheel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open) SetSelection(-1);
        }

        public void SetQuickChatOpen(bool open)
        {
            if (_chat == null) return;
            _chat.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetSelection(int slot)
        {
            if (_wheel == null || slot == _selected) return;
            _selected = slot;
            for (int i = 0; i < _slots.Count; i++)
                _slots[i].EnableInClassList("wheel-slot-selected", i == slot);
            _centre.text = slot >= 0 ? EmoteCatalog.All[slot].Text : "Emote";
        }
    }
}
