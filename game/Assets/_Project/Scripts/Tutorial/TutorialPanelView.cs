using System;
using UnityEngine;
using UnityEngine.UIElements;
using Worktown.Game.Art;

namespace Worktown.Game.Tutorial
{
    /// <summary>
    /// Spec 038, UI Toolkit: the tutorial's card at the top centre (a kicker with the progress "3 / 14", the step text, "Skip step" for an
    /// optional step and "Skip tutorial", or "Close" on the done card) and the Mustard edge arrow that points to a target off the screen.
    /// Palette colours only. Texts change only when the step changes; the arrow only moves (no allocation per frame). Built into the HUD's
    /// document by <see cref="TutorialRunner"/>, so it needs no scene entry.
    /// </summary>
    public sealed class TutorialPanelView
    {
        public const float CardWidth = 540f; // between the wallet box (left) and the right edge at 1280 px
        public const float ArrowSize = 46f;

        private readonly VisualElement _row;
        private readonly Label _kicker;
        private readonly Label _text;
        private readonly Button _skipStep;
        private readonly Button _skipAll;
        private readonly Label _hint;
        private readonly VisualElement _arrow;
        private bool _cardShown;
        private bool _arrowShown;

        public TutorialPanelView(VisualElement root, Action skipStep, Action skipAll)
        {
            _row = new VisualElement { name = "tutorial-row", pickingMode = PickingMode.Ignore };
            IStyle row = _row.style;
            row.position = Position.Absolute;
            row.left = 0;
            row.right = 0;
            row.top = 14;
            row.flexDirection = FlexDirection.Row;
            row.justifyContent = Justify.Center;

            var card = new VisualElement { name = "tutorial-card" };
            card.AddToClassList("panel");
            IStyle c = card.style;
            c.maxWidth = CardWidth;
            c.borderLeftWidth = 4;
            c.borderLeftColor = Palette.Mustard;
            _row.Add(card);

            _kicker = new Label();
            _kicker.style.color = Palette.Mustard;
            _kicker.style.fontSize = 13;
            _kicker.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(_kicker);

            _text = new Label();
            _text.style.fontSize = 17;
            _text.style.whiteSpace = WhiteSpace.Normal;
            _text.style.color = Palette.Cream;
            card.Add(_text);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 4;
            _skipStep = new Button(() => skipStep?.Invoke()) { text = "Skip step" };
            _skipAll = new Button(() => skipAll?.Invoke()) { text = "Skip tutorial" };
            buttons.Add(_skipStep);
            buttons.Add(_skipAll);
            var hint = new Label();
            _hint = hint;
            hint.style.color = Palette.Steel;
            hint.style.fontSize = 12;
            hint.style.marginRight = 8;
            hint.style.alignSelf = Align.Center;
            buttons.Insert(0, hint);
            card.Add(buttons);

            _arrow = new VisualElement { name = "tutorial-arrow", pickingMode = PickingMode.Ignore };
            IStyle a = _arrow.style;
            a.position = Position.Absolute;
            a.width = ArrowSize;
            a.height = ArrowSize;
            a.marginLeft = -ArrowSize * 0.5f;
            a.marginTop = -ArrowSize * 0.5f;
            _arrow.generateVisualContent += DrawArrow;

            root.Add(_row);
            root.Add(_arrow);
            _row.style.display = DisplayStyle.None;
            _arrow.style.display = DisplayStyle.None;
        }

        /// <summary>The step's texts (call only when the step changes: it builds the progress string).</summary>
        public void ShowStep(int index, int count, string text, bool optional, bool done)
        {
            _kicker.text = (done ? "FIRST SHIFT  ·  DONE  ·  " : "FIRST SHIFT  ·  ") + TutorialRules.ProgressText(index, count);
            _text.text = text;
            _skipStep.style.display = optional ? DisplayStyle.Flex : DisplayStyle.None;
            _skipAll.text = done ? "Close" : "Skip tutorial";
            _hint.text = done ? "F4 closes" : optional ? "F5 skips this step  ·  F4 skips the tutorial" : "F4 skips the tutorial";
        }

        /// <summary>038b: the same step's instruction changes (its variant for what the player holds); the caller calls it only on a change.</summary>
        public void SetText(string text) => _text.text = text;

        public string Text => _text.text;
        public string Kicker => _kicker.text;
        public bool CardShown => _cardShown;
        public bool ArrowShown => _arrowShown;

        public void SetCard(bool shown)
        {
            if (shown == _cardShown) return;
            _cardShown = shown;
            _row.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>The arrow at <paramref name="viewport"/> (0..1, y up), pointing <paramref name="angleDegrees"/> (0 right, counter-clockwise).</summary>
        public void SetArrow(bool shown, Vector2 viewport, float angleDegrees)
        {
            if (shown != _arrowShown)
            {
                _arrowShown = shown;
                _arrow.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (!shown) return;
            _arrow.style.left = Length.Percent(viewport.x * 100f);
            _arrow.style.top = Length.Percent((1f - viewport.y) * 100f);
            _arrow.style.rotate = new Rotate(new Angle(-angleDegrees, AngleUnit.Degree)); // UI y runs down: clockwise is positive
        }

        public void Remove()
        {
            _row.RemoveFromHierarchy();
            _arrow.RemoveFromHierarchy();
        }

        /// <summary>A Mustard arrow head pointing right, with an Ink rim, on a dark Panel disc (it reads over any background).</summary>
        private static void DrawArrow(MeshGenerationContext ctx)
        {
            Painter2D p = ctx.painter2D;
            float s = ArrowSize;
            p.fillColor = Palette.Panel;
            p.BeginPath();
            p.Arc(new Vector2(s * 0.5f, s * 0.5f), s * 0.5f, 0f, 360f);
            p.ClosePath();
            p.Fill();
            p.fillColor = Palette.Mustard;
            p.strokeColor = Palette.Ink;
            p.lineWidth = 2f;
            p.BeginPath();
            p.MoveTo(new Vector2(s * 0.95f, s * 0.5f));
            p.LineTo(new Vector2(s * 0.1f, s * 0.12f));
            p.LineTo(new Vector2(s * 0.32f, s * 0.5f));
            p.LineTo(new Vector2(s * 0.1f, s * 0.88f));
            p.ClosePath();
            p.Fill();
            p.Stroke();
        }
    }
}
