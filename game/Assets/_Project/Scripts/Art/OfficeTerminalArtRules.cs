using UnityEngine;
using Worktown.Game.Office;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 059, pure: the office terminal's graybox (a stand-in until the art pass): a wooden case with a dark screen, 1.0 x 0.56 m and
    /// 1.8 cm deep (the 062a catalog row <c>office_terminal</c>: 1000 x 20 mm, Wall), on the back wall over the end worktop, its middle at
    /// <see cref="CentreHeight"/> (every slot in reach from the aisle). Station-local metres, the origin at the screen's middle, -Z towards
    /// the cook: the header on top, the tab row and the page buttons under it, <see cref="TerminalOptions.ScreenRows"/> rows (the text on the
    /// left, four buttons on the right), the two log lines at the bottom. The slots' anchors sit on the glass.
    /// </summary>
    public static class OfficeTerminalArtRules
    {
        public const float Width = 1.0f;
        public const float Height = 0.56f;
        /// <summary>1.8 cm (the catalog row is 20 mm): 1 mm clear of the back line and the wall, like the goal board.</summary>
        public const float Depth = 0.018f;
        /// <summary>m above the floor: the screen's middle (its bottom 1.02 m over the 0.90 m worktop, its top 1.58 m).</summary>
        public const float CentreHeight = 1.30f;
        public const float HeaderY = 0.235f;
        public const float TabY = 0.185f;
        public const float FirstRowY = 0.12f;
        public const float RowStep = 0.052f;
        public const float FooterY = -0.205f;
        public const float TextX = -0.46f;
        /// <summary>A row's own slot (its press) sits in the middle of its text.</summary>
        public const float RowSlotX = -0.24f;
        public const float Glass = 0.004f;

        private static readonly float[] TabX = { -0.38f, -0.22f, -0.06f };
        private static readonly float[] PageX = { 0.30f, 0.42f };
        /// <summary>The row buttons' middles: price down, price up, on/off, special.</summary>
        private static readonly float[] ButtonX = { 0.09f, 0.16f, 0.26f, 0.40f };
        private static readonly float[] ButtonW = { 0.055f, 0.055f, 0.11f, 0.13f };

        public static float FrontZ => -Depth * 0.5f - Glass;

        public static float RowY(int screenRow) => FirstRowY - Mathf.Clamp(screenRow, 0, TerminalOptions.ScreenRows - 1) * RowStep;

        /// <summary>Whether <paramref name="slot"/> is a slot of the screen (5 to 7 are left free).</summary>
        public static bool IsSlot(int slot) =>
            TerminalOptions.IsTabSlot(slot, out _) || TerminalOptions.IsPageSlot(slot, out _) || TerminalOptions.TryRowSlot(slot, out _, out _);

        /// <summary>The middle of slot <paramref name="slot"/> on the glass (zero for a free number).</summary>
        public static Vector3 SlotCentre(int slot)
        {
            if (TerminalOptions.IsTabSlot(slot, out TerminalApp app)) return new Vector3(TabX[(int)app], TabY, FrontZ);
            if (TerminalOptions.IsPageSlot(slot, out int delta)) return new Vector3(PageX[delta < 0 ? 0 : 1], TabY, FrontZ);
            if (!TerminalOptions.TryRowSlot(slot, out int row, out int action)) return Vector3.zero;
            float x = action == TerminalOptions.Press ? RowSlotX : ButtonX[action - 1];
            return new Vector3(x, RowY(row), FrontZ);
        }

        /// <summary>The size of slot <paramref name="slot"/>'s back (a tab, a page button, a row's bar, a row button), thin on z.</summary>
        public static Vector3 SlotSize(int slot)
        {
            if (TerminalOptions.IsTabSlot(slot, out _)) return new Vector3(0.15f, 0.04f, 0.003f);
            if (TerminalOptions.IsPageSlot(slot, out _)) return new Vector3(0.10f, 0.04f, 0.003f);
            if (!TerminalOptions.TryRowSlot(slot, out _, out int action)) return Vector3.zero;
            return action == TerminalOptions.Press ? new Vector3(0.50f, RowStep * 0.85f, 0.003f) : new Vector3(ButtonW[action - 1], RowStep * 0.75f, 0.003f);
        }

        /// <summary>Where a row's text starts (its left end, vertically centred).</summary>
        public static Vector3 RowTextCorner(int screenRow) => new Vector3(TextX, RowY(screenRow), FrontZ - 0.002f);

        public static Vector3 HeaderCentre => new Vector3(0f, HeaderY, FrontZ - 0.002f);

        public static Vector3 FooterCorner => new Vector3(TextX, FooterY, FrontZ - 0.002f);

        /// <summary>The page buttons' labels.</summary>
        public static string PageLabel(int slot) => slot == TerminalOptions.PrevPageSlot ? "<" : slot == TerminalOptions.NextPageSlot ? ">" : string.Empty;
    }
}
