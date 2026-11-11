using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.InputSystem;

namespace Worktown.Game.Settings
{
    /// <summary>
    /// Spec 048, pure: the key and gamepad button of every <see cref="ControlAction"/>. The defaults are the spec 041 mapping (WASD, Shift,
    /// C, Space, V, E, Q, T, G, Tab; gamepad A jump, B crouch, X put down, Y turn, LB emotes, RB ping, the stick presses run and switch
    /// the view). A key or button belongs to one action at most: <see cref="Rebind(ControlAction, Key)"/> detects the conflict and swaps
    /// the two actions (the other one gets this one's old key), and reports it. Fixed keys (<see cref="IsReserved"/>) are refused.
    /// <see cref="Serialize"/> / <see cref="Parse"/> store it by names (one line in the saved settings).
    /// </summary>
    public sealed class ControlBindings
    {
        private static readonly ControlAction[] AllActions = (ControlAction[])Enum.GetValues(typeof(ControlAction));

        private readonly Key[] _keys = new Key[AllActions.Length];
        private readonly PadButton[] _pads = new PadButton[AllActions.Length];

        private ControlBindings()
        {
        }

        /// <summary>Every action, in the order the settings screen lists them.</summary>
        public static IReadOnlyList<ControlAction> Actions => AllActions;

        /// <summary>The spec 041 mapping.</summary>
        public static ControlBindings Defaults()
        {
            var b = new ControlBindings();
            foreach (ControlAction action in AllActions)
            {
                b._keys[(int)action] = DefaultKey(action);
                b._pads[(int)action] = DefaultPad(action);
            }
            return b;
        }

        public static Key DefaultKey(ControlAction action)
        {
            switch (action)
            {
                case ControlAction.MoveForward: return Key.W;
                case ControlAction.MoveBack: return Key.S;
                case ControlAction.MoveLeft: return Key.A;
                case ControlAction.MoveRight: return Key.D;
                case ControlAction.Sprint: return Key.LeftShift;
                case ControlAction.Crouch: return Key.C;
                case ControlAction.Jump: return Key.Space;
                case ControlAction.ToggleView: return Key.V;
                case ControlAction.Use: return Key.E;
                case ControlAction.PutDown: return Key.Q;
                case ControlAction.QuickChat: return Key.T;
                case ControlAction.EmoteWheel: return Key.G;
                case ControlAction.Players: return Key.Tab;
                default: return Key.None; // Turn (the wheel), Ping (the middle button)
            }
        }

        public static PadButton DefaultPad(ControlAction action)
        {
            switch (action)
            {
                case ControlAction.Sprint: return PadButton.LeftStick;
                case ControlAction.Crouch: return PadButton.East;
                case ControlAction.Jump: return PadButton.South;
                case ControlAction.ToggleView: return PadButton.RightStick;
                case ControlAction.PutDown: return PadButton.West;
                case ControlAction.Turn: return PadButton.North;
                case ControlAction.EmoteWheel: return PadButton.LeftShoulder;
                case ControlAction.Ping: return PadButton.RightShoulder;
                default: return PadButton.None; // the left stick moves; RT is the left click (Use)
            }
        }

        public ControlBindings Clone()
        {
            var copy = new ControlBindings();
            Array.Copy(_keys, copy._keys, _keys.Length);
            Array.Copy(_pads, copy._pads, _pads.Length);
            return copy;
        }

        public Key KeyOf(ControlAction action) => _keys[(int)action];

        public PadButton PadOf(ControlAction action) => _pads[(int)action];

        /// <summary>
        /// A key the player cannot take: Esc (menus and the settings), Enter (closes the What's-new note), F1-F12 (menus and dev keys),
        /// the Alt keys (free the cursor), the Windows keys, the arrows (they always walk), Backspace (clears a binding while rebinding),
        /// Print Screen, and everything after the OEM keys (F13-F24, the media keys, the IME dummy) or not a key at all. 048 review: the
        /// digits 1-6 too, because the quick chat reads them fixed while its key is held (SocialInteractor).
        /// </summary>
        public static bool IsReserved(Key key)
        {
            if (key == Key.None) return false;
            if (key >= Key.F1 && key <= Key.F12) return true;
            if (key >= Key.Digit1 && key <= Key.Digit6) return true;
            if (key > Key.OEM5) return true;
            switch (key)
            {
                case Key.Escape:
                case Key.Enter:
                case Key.NumpadEnter:
                case Key.LeftAlt:
                case Key.RightAlt:
                case Key.UpArrow:
                case Key.DownArrow:
                case Key.LeftArrow:
                case Key.RightArrow:
                case Key.LeftMeta:
                case Key.RightMeta:
                case Key.ContextMenu:
                case Key.PrintScreen:
                case Key.Backspace:
                    return true;
                default:
                    return !Enum.IsDefined(typeof(Key), key);
            }
        }

        /// <summary>The other action that uses <paramref name="key"/> (never for None), or null.</summary>
        public ControlAction? ConflictFor(ControlAction action, Key key)
        {
            if (key == Key.None) return null;
            foreach (ControlAction other in AllActions)
                if (other != action && _keys[(int)other] == key) return other;
            return null;
        }

        /// <summary>The other action that uses <paramref name="button"/> (never for None), or null.</summary>
        public ControlAction? ConflictFor(ControlAction action, PadButton button)
        {
            if (button == PadButton.None) return null;
            foreach (ControlAction other in AllActions)
                if (other != action && _pads[(int)other] == button) return other;
            return null;
        }

        /// <summary>Every pair of actions sharing a key or a button (empty for a valid table; a hand-edited save can have some).</summary>
        public List<(ControlAction First, ControlAction Second)> Conflicts()
        {
            var found = new List<(ControlAction, ControlAction)>();
            for (int i = 0; i < AllActions.Length; i++)
            for (int j = i + 1; j < AllActions.Length; j++)
            {
                bool key = _keys[i] != Key.None && _keys[i] == _keys[j];
                bool pad = _pads[i] != PadButton.None && _pads[i] == _pads[j];
                if (key || pad) found.Add((AllActions[i], AllActions[j]));
            }
            return found;
        }

        /// <summary>Binds <paramref name="key"/> to <paramref name="action"/> (None clears it); a conflict swaps (see the class).</summary>
        public RebindResult Rebind(ControlAction action, Key key)
        {
            int i = (int)action;
            if (IsReserved(key)) return new RebindResult(RebindOutcome.Reserved);
            if (_keys[i] == key) return new RebindResult(RebindOutcome.Unchanged);
            if (key == Key.None)
            {
                _keys[i] = Key.None;
                return new RebindResult(RebindOutcome.Cleared);
            }
            ControlAction? other = ConflictFor(action, key);
            if (other.HasValue) _keys[(int)other.Value] = _keys[i];
            _keys[i] = key;
            return other.HasValue ? new RebindResult(RebindOutcome.Swapped, other) : new RebindResult(RebindOutcome.Bound);
        }

        /// <summary>Binds <paramref name="button"/> to <paramref name="action"/> (None clears it); a conflict swaps.</summary>
        public RebindResult Rebind(ControlAction action, PadButton button)
        {
            int i = (int)action;
            if (!Enum.IsDefined(typeof(PadButton), button)) return new RebindResult(RebindOutcome.Reserved);
            if (_pads[i] == button) return new RebindResult(RebindOutcome.Unchanged);
            if (button == PadButton.None)
            {
                _pads[i] = PadButton.None;
                return new RebindResult(RebindOutcome.Cleared);
            }
            ControlAction? other = ConflictFor(action, button);
            if (other.HasValue) _pads[(int)other.Value] = _pads[i];
            _pads[i] = button;
            return other.HasValue ? new RebindResult(RebindOutcome.Swapped, other) : new RebindResult(RebindOutcome.Bound);
        }

        /// <summary>Only the entries that differ from the defaults, as <c>Action=Key,Pad;...</c> (empty for the defaults).</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (ControlAction action in AllActions)
            {
                Key key = KeyOf(action);
                PadButton pad = PadOf(action);
                if (key == DefaultKey(action) && pad == DefaultPad(action)) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(action).Append('=').Append(key).Append(',').Append(pad);
            }
            return sb.ToString();
        }

        /// <summary>
        /// The table from <see cref="Serialize"/>'s text: the defaults with every valid entry applied. Unknown actions, keys or buttons,
        /// numbers and reserved keys are skipped; if the result still has a conflict (a hand-edited save), the defaults come back whole.
        /// Never throws.
        /// </summary>
        public static ControlBindings Parse(string text)
        {
            ControlBindings b = Defaults();
            if (string.IsNullOrWhiteSpace(text)) return b;
            foreach (string entry in text.Split(';'))
            {
                int eq = entry.IndexOf('=');
                if (eq <= 0) continue;
                string[] parts = entry.Substring(eq + 1).Split(',');
                if (parts.Length != 2 || !TryName(entry.Substring(0, eq), out ControlAction action)) continue;
                if (TryName(parts[0], out Key key) && !IsReserved(key)) b._keys[(int)action] = key;
                if (TryName(parts[1], out PadButton pad)) b._pads[(int)action] = pad;
            }
            return b.Conflicts().Count == 0 ? b : Defaults();
        }

        private static bool TryName<T>(string text, out T value) where T : struct
        {
            value = default;
            text = text?.Trim();
            if (string.IsNullOrEmpty(text) || char.IsDigit(text[0]) || text[0] == '-') return false; // names only, never numbers
            return Enum.TryParse(text, false, out value) && Enum.IsDefined(typeof(T), value);
        }
    }
}
