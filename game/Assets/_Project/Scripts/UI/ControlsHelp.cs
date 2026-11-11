namespace Worktown.Game.UI
{
    /// <summary>
    /// Spec 041, pure: the controls as the F1 menu shows them (the same mapping as Net.Rules.ControlRules). Q is a hidden alias of the
    /// right click and is not listed.
    /// </summary>
    public static class ControlsHelp
    {
        public const string Title = "Controls";

        /// <summary>One line per input, mouse first.</summary>
        public static readonly string[] Lines =
        {
            "Left click: do it (pick up, take, press, put into, cut, flip, serve)",
            "Hold left click: squeeze, pour, scrub, shave, wipe; drag down with the knife to cut",
            "Right click: put down what you hold (where the ghost shows), else drop it",
            "Mouse wheel: turn the food on the board, or what you hold before you put it down",
            "E (third person): the same as left click on the nearest thing",
            "V: first / third person   WASD: walk   Shift: run   C: sneak   Space: jump",
            "Tab: who is in the shop (invite friends, kick, leave the shop)", // spec 047
            "Gamepad: RT = left click, LT = right click, Y = turn",
        };

        private static string _text;

        /// <summary>The block for the menu: the title and the lines, one per row (built once).</summary>
        public static string Text() => _text ??= Build();

        private static string Build()
        {
            var sb = new System.Text.StringBuilder(Title.Length + 8 + Lines.Length * 64);
            sb.Append(Title).Append(':');
            foreach (string line in Lines) sb.Append('\n').Append("  ").Append(line);
            return sb.ToString();
        }
    }
}
