namespace Worktown.Game.Office
{
    /// <summary>
    /// Spec 059, pure: the office terminal's presses on the existing station intent (RequestStation; no new RPC):
    /// <c>option = app &lt;&lt; 12 | row &lt;&lt; 4 | action</c>. The row is the app's row (not the screen's: the page is the client's own),
    /// the action one of <see cref="Press"/>, <see cref="Minus"/>, <see cref="Plus"/>, <see cref="Toggle"/>, <see cref="Special"/>.
    /// Anything else is refused (<see cref="TryUnpack"/>). The screen's physical slots (the station's StockSlots) are numbered apart:
    /// the tabs, the page buttons and each screen row's buttons; the client turns a row slot into an option with its tab and page
    /// (<see cref="OptionFor"/>), and the tabs and pages never leave the client.
    /// </summary>
    public static class TerminalOptions
    {
        public const int Press = 0;
        public const int Minus = 1;
        public const int Plus = 2;
        public const int Toggle = 3;
        public const int Special = 4;
        public const int ActionCount = 5;

        /// <summary>The apps this build has (<see cref="TerminalApp"/> values 0 to AppCount - 1).</summary>
        public const int AppCount = 3;
        /// <summary>The highest app row a press may name (8 bits).</summary>
        public const int MaxRow = 255;

        // ---- the screen's slots ----

        /// <summary>The rows on one screen.</summary>
        public const int ScreenRows = 6;
        /// <summary>Slots 0 to AppCount - 1 are the tabs.</summary>
        public const int FirstTabSlot = 0;
        public const int PrevPageSlot = 3;
        public const int NextPageSlot = 4;
        /// <summary>Screen row r's action a is slot FirstRowSlot + r * ActionCount + a.</summary>
        public const int FirstRowSlot = 8;
        public const int SlotCount = FirstRowSlot + ScreenRows * ActionCount;

        /// <summary>The option of a press, or -1 when <paramref name="app"/>, <paramref name="row"/> or <paramref name="action"/> is out of range.</summary>
        public static int Pack(TerminalApp app, int row, int action)
        {
            int a = (int)app;
            if (a < 0 || a >= AppCount || row < 0 || row > MaxRow || action < 0 || action >= ActionCount) return -1;
            return a << 12 | row << 4 | action;
        }

        /// <summary>A press's parts; false (all zero) for a negative option, an unknown app or action, or bits above the app's.</summary>
        public static bool TryUnpack(int option, out TerminalApp app, out int row, out int action)
        {
            app = TerminalApp.Menu;
            row = 0;
            action = 0;
            if (option < 0 || option >> 12 >= AppCount) return false;
            int a = option & 0xF;
            if (a >= ActionCount) return false;
            app = (TerminalApp)(option >> 12);
            row = (option >> 4) & 0xFF;
            action = a;
            return true;
        }

        public static int TabSlot(TerminalApp app) => FirstTabSlot + (int)app;

        /// <summary>Whether <paramref name="slot"/> is a tab, and which app it opens.</summary>
        public static bool IsTabSlot(int slot, out TerminalApp app)
        {
            app = TerminalApp.Menu;
            if (slot < FirstTabSlot || slot >= FirstTabSlot + AppCount) return false;
            app = (TerminalApp)(slot - FirstTabSlot);
            return true;
        }

        /// <summary>Whether <paramref name="slot"/> turns the page, and which way (-1 back, +1 on).</summary>
        public static bool IsPageSlot(int slot, out int delta)
        {
            delta = slot == PrevPageSlot ? -1 : slot == NextPageSlot ? 1 : 0;
            return delta != 0;
        }

        public static int RowSlot(int screenRow, int action) => FirstRowSlot + screenRow * ActionCount + action;

        /// <summary>Whether <paramref name="slot"/> is a screen row's button, which row (0 to ScreenRows - 1) and which action.</summary>
        public static bool TryRowSlot(int slot, out int screenRow, out int action)
        {
            screenRow = 0;
            action = 0;
            if (slot < FirstRowSlot || slot >= SlotCount) return false;
            screenRow = (slot - FirstRowSlot) / ActionCount;
            action = (slot - FirstRowSlot) % ActionCount;
            return true;
        }

        /// <summary>The option a row slot sends on <paramref name="app"/>'s page <paramref name="page"/> (-1: not a row slot, or past MaxRow).</summary>
        public static int OptionFor(int slot, TerminalApp app, int page)
        {
            if (!TryRowSlot(slot, out int screenRow, out int action) || page < 0) return -1;
            return Pack(app, page * ScreenRows + screenRow, action);
        }

        /// <summary>The pages a list of <paramref name="rows"/> rows fills (at least 1).</summary>
        public static int Pages(int rows) => rows <= ScreenRows ? 1 : (rows + ScreenRows - 1) / ScreenRows;
    }
}
