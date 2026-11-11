using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 044 (027b feels off: "pressed buttons click but do not sink"): a station button sinks into the station and springs back
    /// (<see cref="ButtonPressRules.Depth"/>). The presser sees it at the click (<see cref="PressNow"/>, with the click sound); every
    /// other viewer sees it when the station's flags byte changes the way this button changes it (<see cref="ButtonPressRules.Pressed"/>).
    /// Client look only: it moves the button's own transform (a child of the station, never a networked root) along the station's
    /// depth (+z, the fronts face -z) by about a centimetre, and puts it back. Added by <see cref="StationButton"/> when graphics run.
    /// </summary>
    public sealed class ButtonPressView : MonoBehaviour
    {
        private StationButton _button;
        private StationContents _contents;
        private Vector3 _rest;
        private float _pressedAt = float.NegativeInfinity;
        private float _ownPressAt = float.NegativeInfinity;
        private byte _seen;
        private bool _hasSeen;
        private bool _moved;

        /// <summary>True while the button is drawn sunk (tests and the tour log).</summary>
        public bool IsSunk => _moved;

        /// <summary>The depth the button is drawn at now, in metres.</summary>
        public float Depth { get; private set; }

        private void Awake()
        {
            _button = GetComponent<StationButton>();
            _rest = transform.localPosition;
            if (_button != null && _button.Station != null) _button.Station.TryGetComponent(out _contents);
        }

        /// <summary>Our own press: sink now; the flags change that confirms it does not sink the button a second time.</summary>
        public void PressNow()
        {
            float now = Time.unscaledTime;
            _pressedAt = now;
            _ownPressAt = now;
        }

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            if (_contents != null && _button != null && _button.Station != null)
            {
                byte flags = _contents.StoveFlags.Value;
                if (!_hasSeen)
                {
                    _hasSeen = true;
                    _seen = flags;
                }
                else if (flags != _seen)
                {
                    if (ButtonPressRules.Pressed(_button.Station.Kind, _button.Option, _seen, flags))
                    {
                        if (ButtonPressRules.IsOwnConfirm(now, _ownPressAt)) _ownPressAt = float.NegativeInfinity; // our press, confirmed
                        else _pressedAt = now; // someone else pressed it
                    }
                    _seen = flags;
                }
            }
            Depth = ButtonPressRules.Depth(now - _pressedAt);
            if (Depth <= 0f && !_moved) return;
            transform.localPosition = _rest + Vector3.forward * Depth;
            _moved = Depth > 0f;
        }

        private void OnDisable()
        {
            if (_moved) transform.localPosition = _rest;
            _moved = false;
        }
    }
}
