using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;

namespace Worktown.Game.UI
{
    /// <summary>
    /// Spec 022: the shift summary panel (served, earned, average stars, missed). Shown to every player for the 15 s
    /// after a shift closes (the server's flag on the ticket rail) and whenever F3 toggles it on. Without a finished shift
    /// it shows the open shift so far. Client only, display only: the numbers come from the rail's SyncVars.
    /// Dev only (development builds): <c>-demo-shift</c> opens it 25 s after start for screenshots; this only changes what
    /// this client shows, never the server's clock.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ShiftSummaryView : MonoBehaviour
    {
        private const string DemoArg = "-demo-shift";
        private const float DemoDelaySeconds = 25f;

        [SerializeField] private StyleSheet _style;

        private VisualElement _panel;
        private Label _title;
        private Label _served;
        private Label _earned;
        private Label _stars;
        private Label _missed;
        private Label _footer;
        private TicketRailView _rail;
        private bool _pinned;
        private float _demoAt = -1f;

        private void Start()
        {
            if (Application.isBatchMode) return; // headless server: no UI
            VisualElement documentRoot = GetComponent<UIDocument>().rootVisualElement;
            if (_style != null && !documentRoot.styleSheets.Contains(_style)) documentRoot.styleSheets.Add(_style);
            _panel = BuildPanel();
            documentRoot.Add(_panel);
            _panel.style.display = DisplayStyle.None;
            if (Debug.isDebugBuild && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), DemoArg) >= 0)
                _demoAt = Time.unscaledTime + DemoDelaySeconds;
        }

        private void OnDisable()
        {
            _panel?.RemoveFromHierarchy();
            _panel = null;
        }

        private void Update()
        {
            if (_panel == null) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame) _pinned = !_pinned;
            if (_demoAt >= 0f && Time.unscaledTime >= _demoAt)
            {
                _demoAt = -1f;
                _pinned = true;
                Debug.Log("[Demo] shift summary opened (-demo-shift)");
            }

            if (_rail == null) _rail = FindFirstObjectByType<TicketRailView>();
            bool known = _rail != null && _rail.IsSpawned;
            bool visible = known && (_pinned || _rail.SummaryOpen.Value);
            _panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible) Fill();
        }

        private void Fill()
        {
            bool last = _rail.LastDay.Value > 0 && (_rail.SummaryOpen.Value || !_rail.ShiftOpen.Value || _rail.LiveServed.Value + _rail.LiveMissed.Value == 0);
            int day = last ? _rail.LastDay.Value : _rail.Day.Value;
            int served = last ? _rail.LastServed.Value : _rail.LiveServed.Value;
            int earned = last ? _rail.LastEarned.Value : _rail.LiveEarned.Value;
            int stars = last ? _rail.LastStarsTenths.Value : _rail.LiveStarsTenths.Value;
            int missed = last ? _rail.LastMissed.Value : _rail.LiveMissed.Value;

            _title.text = (last ? "Shift summary" : "This shift so far") + (day > 0 ? "  ·  Day " + day : string.Empty);
            _served.text = "Dishes served:  " + served;
            _earned.text = "Money earned:  " + EconomyRules.Money(earned);
            int rounded = (stars + 5) / 10;
            _stars.text = "Average stars:  " + (served > 0 ? Npc.ShiftSummary.StarsText(stars) + "  " + StarRating.Format(rounded) : "-");
            _missed.text = "Missed customers:  " + missed;
            _footer.text = _rail.SummaryOpen.Value ? "Next shift starts at 08:00 tomorrow." : "F3 closes this.";
        }

        private VisualElement BuildPanel()
        {
            var panel = new VisualElement { name = "shift-summary", pickingMode = PickingMode.Ignore };
            panel.AddToClassList("panel");
            IStyle style = panel.style;
            style.position = Position.Absolute;
            style.top = 16;
            style.left = Length.Percent(50);
            style.width = 340;
            style.marginLeft = -170;
            style.paddingTop = 12;
            style.paddingBottom = 12;
            _title = AddLabel(panel, 20, Palette.Mustard);
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.style.marginBottom = 6;
            _served = AddLabel(panel, 16, Palette.Cream);
            _earned = AddLabel(panel, 16, Palette.Cream);
            _stars = AddLabel(panel, 16, Palette.Cream);
            _missed = AddLabel(panel, 16, Palette.Cream);
            _footer = AddLabel(panel, 13, Palette.Plaster);
            _footer.style.marginTop = 6;
            return panel;
        }

        private static Label AddLabel(VisualElement parent, int size, Color color)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            parent.Add(label);
            return label;
        }
    }
}
