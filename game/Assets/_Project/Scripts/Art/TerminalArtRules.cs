using UnityEngine;
using Worktown.Game.Shop;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 034, pure: the ordering terminal's graybox (docs/art-direction.md row "Ordering terminal"): a flat wall screen on the back
    /// wall above the landing worktop by the staff door, 1.0 × 0.46 m and 2 cm deep (it hangs between the worktop's back edge and the
    /// wall), its middle at <see cref="CentreHeight"/>: low enough that every slot is in reach from the aisle, high enough to read over
    /// the delivery boxes on the worktop. Station-local positions (the origin at the screen's middle, -Z towards the aisle): the header,
    /// <see cref="TerminalRules.Rows"/> rows on the left and the three buttons (page back, next page, packs) in a column on the right.
    /// The rows and buttons are the terminal's slots (their anchors on the glass).
    /// </summary>
    public static class TerminalArtRules
    {
        public const float Width = 1.0f;
        public const float Height = 0.46f;
        public const float Depth = 0.02f;
        /// <summary>m above the floor: the screen's middle.</summary>
        public const float CentreHeight = 1.36f;
        public const float RowHeight = 0.055f;
        public const float RowWidth = 0.70f;
        public const float RowX = -0.13f;
        public const float HeaderY = 0.185f;
        public const float FirstRowY = 0.12f;
        public const float ButtonX = 0.37f;
        public const float ButtonWidth = 0.2f;
        public const float ButtonHeight = 0.07f;
        public const float ButtonSpacing = 0.1f;
        /// <summary>m in front of the screen's face: the slots' anchors and the row backs sit on the glass.</summary>
        public const float Glass = 0.004f;

        /// <summary>The front face's local z (the aisle side).</summary>
        public static float FrontZ => -Depth * 0.5f;

        /// <summary>The middle of slot <paramref name="slot"/> (a row or a button) on the glass, station local.</summary>
        public static Vector3 SlotCentre(int slot)
        {
            float z = FrontZ - Glass;
            if (TerminalRules.IsRow(slot)) return new Vector3(RowX, FirstRowY - slot * RowHeight, z);
            int b = slot - TerminalRules.PrevSlot; // 0 back, 1 next, 2 packs
            return new Vector3(ButtonX, FirstRowY - 0.02f - b * ButtonSpacing, z);
        }

        /// <summary>The size of slot <paramref name="slot"/>'s back (a row's bar or a button), thin on z.</summary>
        public static Vector3 SlotSize(int slot) =>
            TerminalRules.IsRow(slot) ? new Vector3(RowWidth, RowHeight * 0.86f, 0.004f) : new Vector3(ButtonWidth, ButtonHeight, 0.004f);

        /// <summary>The header's middle, station local.</summary>
        public static Vector3 HeaderCentre => new Vector3(0f, HeaderY, FrontZ - Glass * 2f);

        /// <summary>The words of the three buttons.</summary>
        public static string ButtonText(int slot) =>
            slot == TerminalRules.PrevSlot ? "< PAGE" : slot == TerminalRules.NextSlot ? "PAGE >" : slot == TerminalRules.PacksSlot ? "PACKS +" : string.Empty;
    }
}
