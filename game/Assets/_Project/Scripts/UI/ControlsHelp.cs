using Worktown.Game.Settings;

namespace Worktown.Game.UI
{
    /// <summary>
    /// Spec 041, pure: the controls as the F1 menu shows them (the same mapping as Net.Rules.ControlRules). Q is a hidden alias of the
    /// right click and is not listed. Spec 048 (review): the keyboard keys are <c>{Action}</c> tokens that <see cref="Text"/> fills with
    /// the player's bound keys (KeyPrompts), so the list follows a rebind; the mouse and the gamepad triggers stay fixed.
    /// </summary>
    public static class ControlsHelp
    {
        public const string Title = "Controls";

        /// <summary>One line per input, mouse first, with the bound keys as tokens.</summary>
        public static readonly string[] Lines =
        {
            "Left click: do it (pick up, take, press, put into, cut, flip, serve)",
            "Hold left click: squeeze, pour, scrub, shave, wipe; drag down with the knife to cut",
            "Right click: put down what you hold (where the ghost shows), else drop it",
            "Mouse wheel: turn the food on the board, or what you hold before you put it down",
            "{Use} (third person): the same as left click on the nearest thing",
            "{ToggleView}: first / third person   {Move}: walk   {Sprint}: run   {Crouch}: sneak   {Jump}: jump",
            "{Players}: who is in the shop (invite friends, kick, leave the shop)", // spec 047
            "J: the pocket clipboard (today's menu, the goal, the last service)", // spec 059 (fixed key until 048 binds it)
            "Gamepad: RT = left click, LT = right click, Y = turn",
        };

        private static string _text;
        private static ControlBindings _textFor;

        /// <summary>The block for the menu: the title and the lines with <paramref name="bindings"/>' keys, one per row (built once per
        /// bindings table).</summary>
        public static string Text(ControlBindings bindings)
        {
            if (_text != null && ReferenceEquals(_textFor, bindings)) return _text;
            _textFor = bindings;
            _text = Build(bindings);
            return _text;
        }

        private static string Build(ControlBindings bindings)
        {
            var sb = new System.Text.StringBuilder(Title.Length + 8 + Lines.Length * 64);
            sb.Append(Title).Append(':');
            foreach (string line in Lines) sb.Append('\n').Append("  ").Append(KeyPrompts.Fill(line, bindings));
            return sb.ToString();
        }
    }
}
