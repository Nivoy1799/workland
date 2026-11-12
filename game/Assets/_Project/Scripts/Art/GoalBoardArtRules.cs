using UnityEngine;
using Worktown.Game.Climb;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 057, pure: the goal board's graybox (a stand-in until the art pass): a 0.80 x 0.50 m panel, 1.8 cm deep (the 062a catalog row
    /// <c>goal_board</c>: 800 x 30 mm, Wall; 1.8 cm keeps it 1 mm clear of the back line and the wall), on the back wall above the worktop between the fridge and the griddle, in reach from the aisle.
    /// Station-local metres, the origin at the board's middle, -Z towards the cook: one text line per row (GoalBoardRules.RowCount), the
    /// stage's step on top, each row's slot anchor at its line's left end.
    /// </summary>
    public static class GoalBoardArtRules
    {
        public const float Width = 0.80f;
        public const float Height = 0.50f;
        public const float Depth = 0.018f;
        /// <summary>m above the floor: the board's middle (its bottom 1.05 m, over the 0.90 m worktop; its top 1.55 m: every row in reach from the aisle).</summary>
        public const float CentreHeight = 1.30f;
        /// <summary>The first row's middle and the step between rows (down).</summary>
        public const float FirstRowY = 0.15f;
        public const float RowStep = 0.075f;
        /// <summary>The rows' text starts here (left edge).</summary>
        public const float TextX = -0.37f;
        /// <summary>The slot anchors sit in the middle of each row's line, just in front of the face.</summary>
        public const float AnchorX = 0f;

        public static float FrontZ => -Depth * 0.5f - 0.004f;

        /// <summary>The middle of row <paramref name="row"/>'s line (its slot anchor).</summary>
        public static Vector3 RowCentre(int row) => new Vector3(AnchorX, FirstRowY - Mathf.Clamp(row, 0, GoalBoardRules.RowCount - 1) * RowStep, FrontZ);

        /// <summary>Where row <paramref name="row"/>'s text starts (its left end, vertically centred).</summary>
        public static Vector3 RowTextCorner(int row) => new Vector3(TextX, RowCentre(row).y, FrontZ);
    }
}
