using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Net;

namespace Worktown.Game.UI
{
    /// <summary>
    /// Spec 022: the shift summary panel (served, earned, average stars, missed). Shown to every player for the 15 s
    /// after a shift closes (the server's flag on the ticket rail) and whenever F3 toggles it on. Without a finished shift
    /// it shows the open shift so far. Client only, display only: the numbers come from the rail's SyncVars.
    /// Dev only (development builds): <c>-demo-shift</c> opens it 25 s after start for screenshots; this only changes what
    /// this client shows, never the server's clock.
    /// Spec 032: plus the tips, the reviews (average, count, the last three with their stars and line) and the missed guests by
    /// reason, from the rail's <see cref="Npc.ShiftReport"/> values.
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
        private Label _tips;
        private Label _reviews;
        private readonly Label[] _reviewLines = new Label[3];
        private Label _footer;
        private Label _goal; // spec 053: the food corner's goal progress
        private Worktown.Game.Climb.ShopStage _shopStage;
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
            Npc.ShiftReport report = last ? _rail.LastReport.Value : _rail.LiveReport.Value;

            _title.text = (last ? "Shift summary" : "This shift so far") + (day > 0 ? "  ·  Day " + day : string.Empty);
            _served.text = "Dishes served:  " + served;
            _earned.text = "Money earned:  " + EconomyRules.Money(earned);
            int rounded = (stars + 5) / 10;
            _stars.text = "Average stars:  " + (served > 0 ? Npc.ShiftSummary.StarsText(stars) + "  " + StarRating.Format(rounded) : "-");
            _missed.text = MissedText(missed, report);
            _tips.text = "Tips:  " + EconomyRules.Money(report.Tips);
            _reviews.text = report.Reviews > 0
                ? "Reviews:  " + Npc.ShiftSummary.StarsText(report.ReviewTenths) + "  " + StarRating.Format((report.ReviewTenths + 5) / 10)
                    + "  (" + report.Reviews + ")"
                : "Reviews:  -";
            byte[] packed = { report.Review1, report.Review2, report.Review3 };
            for (int i = 0; i < _reviewLines.Length; i++)
            {
                string line = ReviewLine(packed[i]);
                _reviewLines[i].text = line;
                _reviewLines[i].style.display = line.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            UpdateGoal();
            _footer.text = _rail.SummaryOpen.Value ? "Next shift starts at 08:00 tomorrow." : "F3 closes this.";
        }

        /// <summary>Spec 053: "Save for the rent: $230 / $400 (57 %)" at the food corner (the one shop wallet), hidden in the open kitchen.</summary>
        private void UpdateGoal()
        {
            if (_shopStage == null) _shopStage = Worktown.Game.Climb.ShopStage.Find();
            string text = string.Empty;
            if (_shopStage != null && _shopStage.StageNow != Worktown.Game.Climb.ShopStageRules.OpenKitchen && _shopStage.GoalMoney.Value > 0)
            {
                PickupInteractor player = LocalPlayer.Find(FishNet.InstanceFinder.NetworkManager);
                long balance = player != null && player.TryGetComponent(out Wallet wallet) ? wallet.Balance.Value : 0L;
                text = GoalText(balance, _shopStage.GoalMoney.Value, _shopStage.GoalReached.Value);
            }
            _goal.text = text;
            _goal.style.display = text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Spec 053: the summary's goal line (with the reached message once the goal flipped).</summary>
        public static string GoalText(long balance, long goal, bool reached) =>
            reached ? Worktown.Game.Climb.FoodCornerRules.GoalReachedText : Worktown.Game.Climb.FoodCornerRules.GoalProgressLine(balance, goal);

        private VisualElement BuildPanel()
        {
            var panel = new VisualElement { name = "shift-summary", pickingMode = PickingMode.Ignore };
            panel.AddToClassList("panel");
            IStyle style = panel.style;
            style.position = Position.Absolute;
            style.top = 16;
            style.left = Length.Percent(50);
            style.width = 400; // spec 032: room for a review line
            style.marginLeft = -200;
            style.paddingTop = 12;
            style.paddingBottom = 12;
            _title = AddLabel(panel, 20, Palette.Mustard);
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.style.marginBottom = 6;
            _served = AddLabel(panel, 16, Palette.Cream);
            _earned = AddLabel(panel, 16, Palette.Cream);
            _stars = AddLabel(panel, 16, Palette.Cream);
            _tips = AddLabel(panel, 16, Palette.Cream); // spec 032
            _reviews = AddLabel(panel, 16, Palette.Cream);
            for (int i = 0; i < _reviewLines.Length; i++)
            {
                _reviewLines[i] = AddLabel(panel, 14, Palette.Mustard);
                _reviewLines[i].style.marginLeft = 12;
                _reviewLines[i].style.whiteSpace = WhiteSpace.Normal;
            }
            _missed = AddLabel(panel, 16, Palette.Cream);
            _missed.style.whiteSpace = WhiteSpace.Normal;
            _goal = AddLabel(panel, 16, Palette.Mustard); // spec 053
            _goal.style.whiteSpace = WhiteSpace.Normal;
            _goal.style.marginTop = 4;
            _footer = AddLabel(panel, 13, Palette.Plaster);
            _footer.style.marginTop = 6;
            return panel;
        }

        /// <summary>Spec 032: one review in the summary: "★★★★☆ "Tasty, I'd come back."" (empty for none).</summary>
        public static string ReviewLine(byte packed)
        {
            Worktown.Sim.Economy.GuestReview review = Worktown.Sim.Economy.GuestReview.Unpack(packed);
            return review == null ? string.Empty : StarRating.Format(review.Stars) + "  \u201C" + review.Text + "\u201D";
        }

        /// <summary>Spec 032: "Missed customers:  3 (out of patience 1, changed their mind 1, kitchen closed 1)".</summary>
        public static string MissedText(int missed, Npc.ShiftReport report)
        {
            string text = "Missed customers:  " + missed;
            if (missed <= 0) return text;
            var parts = new System.Collections.Generic.List<string>(3);
            if (report.MissedPatience > 0) parts.Add("out of patience " + report.MissedPatience);
            if (report.MissedGone > 0) parts.Add("changed their mind " + report.MissedGone);
            if (report.MissedClosed > 0) parts.Add("kitchen closed " + report.MissedClosed);
            return parts.Count > 0 ? text + "  (" + string.Join(", ", parts) + ")" : text;
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        /// <summary>Spec 032, tours: open or close the panel like F3 does.</summary>
        public void DevPin(bool pinned) => _pinned = pinned;

        /// <summary>Spec 032, tours: the panel's lines as shown now (for the log).</summary>
        public string DevText()
        {
            if (_panel == null) return string.Empty;
            var lines = new System.Collections.Generic.List<string>();
            foreach (VisualElement child in _panel.Children())
                if (child is Label label && label.style.display != DisplayStyle.None && !string.IsNullOrEmpty(label.text)) lines.Add(label.text);
            return string.Join(" | ", lines);
        }
#endif

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
