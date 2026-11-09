using UnityEngine;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 029, pure: the deep fryer's shape, shared by the art catalog (the graybox fryer and basket) and the scene builder
    /// (body, buttons, baskets). Positions are in the fryer station's space: x right as seen from the aisle, y up from the floor,
    /// z from the station's centre with -z towards the aisle (its front). Two oil wells side by side, a basket in each; the
    /// controls (a power knob and a button per basket) on the front face; a deck behind the wells with a stack of empty fries
    /// boxes. Sizes in the game (the basket is cookware, x1.15).
    /// </summary>
    public static class FryerArt
    {
        public const float Width = 0.8f;
        public const float Depth = 1.2f;
        public const float Height = ArtSizeTable.WorktopHeight;
        /// <summary>The body's collider stops under the wells, so the aim ray finds a lowered basket in the oil.</summary>
        public const float BodyColliderTop = 0.6f;
        public const float WellFloor = 0.6f;
        public const float OilHeight = 0.8f;
        public const float WellWidth = 0.3f;
        public const float WellFront = -0.45f;
        public const float WellBack = 0.07f;
        public const float FrontPanel = 0.04f;
        /// <summary>The basket centres (x), basket 0 on the left as seen from the aisle.</summary>
        public static readonly float[] BasketX = { -0.19f, 0.19f };
        /// <summary>The lowered basket: its body's centre z and its bottom's height.</summary>
        public const float BasketZ = -0.18f;
        public const float BasketBottom = 0.67f;
        public const float BasketWidth = 0.22f;
        public const float BasketLength = 0.34f;
        public const float BasketHeight = 0.15f;
        public const float WireThickness = 0.007f;
        /// <summary>The handle runs from the basket's front rim towards the aisle and up.</summary>
        public const float HandleLength = 0.17f;
        public const float HandleRise = 0.1f;
        /// <summary>On the hook: lifted, a little towards the back, front tilted up (degrees about x) so it drips into the oil.</summary>
        public static readonly Vector3 RaisedOffset = new Vector3(0f, 0.3f, 0.07f);
        public const float RaisedTilt = 12f;
        /// <summary>The control row on the front face (m above the floor), the knob in the middle, a button under each basket.</summary>
        public const float ButtonHeight = 0.72f;
        public const float LabelHeight = 0.855f;
        public const float PowerX = 0f;
        /// <summary>Where the empty fries boxes stand on the back deck (z) and how many.</summary>
        public const float BoxesZ = 0.36f;
        public const int BoxCount = 3;

        /// <summary>The basket's contents sit this high above its bottom (food on the wire floor).</summary>
        public const float ContentsHeight = 0.03f;
    }
}
