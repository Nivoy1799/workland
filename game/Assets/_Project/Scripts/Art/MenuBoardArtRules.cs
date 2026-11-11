using UnityEngine;
using Worktown.Game.Shop;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 037, pure: the menu board's graybox (docs/art-direction.md row "Menu board"): a 0.70 x 0.50 m board, 3 cm deep, hanging above
    /// the front counter between the serving counter and the register, below the ticket rail and beside the guests' lines of sight (the
    /// queue head is west of it, the pay line east). Station-local metres, the origin at the board's middle, -Z towards the cook: the
    /// text (the lines) in the upper part and the four buttons (MenuBoardRules options) in a row along the bottom.
    /// </summary>
    public static class MenuBoardArtRules
    {
        public const float Width = 0.70f;
        public const float Height = 0.50f;
        public const float Depth = 0.03f;
        /// <summary>m above the floor: the board's middle (its bottom 1.85 m, its top 2.35 m, under the rail's 2.46 m).</summary>
        public const float CentreHeight = 2.10f;
        public static readonly Vector3 ButtonSize = new Vector3(0.11f, 0.065f, 0.03f);
        public const float ButtonY = -0.185f;
        public const float ButtonSpacing = 0.15f;
        /// <summary>The text's top-left corner, just in front of the board's face.</summary>
        public static readonly Vector3 TextCorner = new Vector3(-0.31f, 0.215f, -Depth * 0.5f - 0.004f);

        /// <summary>The middle of button <paramref name="option"/> (0-3), standing out of the board's face towards the cook.</summary>
        public static Vector3 ButtonCentre(int option) =>
            new Vector3((option - (MenuBoardRules.OptionCount - 1) * 0.5f) * ButtonSpacing, ButtonY, -Depth * 0.5f - ButtonSize.z * 0.5f);
    }
}
