using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 028b, pure: the shape of the stock stations, shared by the art catalog (graybox models) and the scene builder
    /// (bodies and slots). Both are <see cref="ArtSizeTable.StockHeight"/> (1.80 m, the guide's fridge) high, 1.2 m wide, and
    /// stand with their backs on the back line's back, so they are shallower than the 1.2 m worktops: the fridge 0.62 m (a
    /// real one, so its open doors stop at the worktops' front line), the dry shelf 0.5 m (a bookcase). The fridge's top
    /// part is open behind two doors opened to the sides; the part under it is a closed drawer. Slots are numbered row by row
    /// from the top, left to right as seen from the aisle (<see cref="StockLayout"/>). Positions here are in the station's
    /// local space: x right as seen from the aisle, y up from the floor, z from the front face (0) backwards (positive).
    /// </summary>
    public static class StockArt
    {
        public const float Width = 1.2f;
        public const float FridgeDepth = 0.62f;
        public const float ShelfDepth = 0.5f;
        /// <summary>Side, back and board thickness of the cabinet (m).</summary>
        public const float Wall = 0.04f;
        /// <summary>The fridge's closed drawer under the open part: its top is the lower shelf (m above the floor).</summary>
        public const float FridgeDrawerTop = 0.9f;
        /// <summary>The fridge's top panel (the header with its sign) starts here (m above the floor).</summary>
        public const float FridgeHeaderBottom = 1.68f;
        /// <summary>How far the doors are opened (degrees from closed). 90: they stand straight out, beside the opening.</summary>
        public const float DoorOpenAngle = 90f;
        /// <summary>How far the items stand behind the front face (m), so they show and sit well inside.</summary>
        public const float ItemSetback = 0.17f;

        /// <summary>Where the items of each fridge row stand (m above the floor), top row first.</summary>
        public static readonly System.Collections.Generic.IReadOnlyList<float> FridgeRowHeights = System.Array.AsReadOnly(new[] { 1.30f, FridgeDrawerTop });
        /// <summary>Where the items of each shelf board stand (m above the floor), top board first.</summary>
        public static readonly System.Collections.Generic.IReadOnlyList<float> ShelfRowHeights = System.Array.AsReadOnly(new[] { 1.32f, 0.88f, 0.44f });

        public static float DepthOf(StationKind kind) => kind == StationKind.Fridge ? FridgeDepth : ShelfDepth;

        /// <summary>The usable width between the side walls (m).</summary>
        public const float InnerWidth = Width - 2f * Wall;

        /// <summary>Distance between neighbouring slots in a row (m).</summary>
        public static float Pitch(StationKind kind)
        {
            int columns = StockLayout.Columns(kind);
            return columns > 0 ? InnerWidth / columns : 0f;
        }

        /// <summary>
        /// Where slot <paramref name="slot"/> of a fridge or shelf has its items standing (the middle of their bottom), in the
        /// station's local space (see the class summary). Zero for a slot the station does not have.
        /// </summary>
        public static Vector3 StandPoint(StationKind kind, int slot)
        {
            int columns = StockLayout.Columns(kind);
            int count = StockLayout.SlotsOf(kind).Count;
            if (columns == 0 || slot < 0 || slot >= count) return Vector3.zero;
            int row = slot / columns, column = slot % columns;
            System.Collections.Generic.IReadOnlyList<float> heights = kind == StationKind.Fridge ? FridgeRowHeights : ShelfRowHeights;
            float x = (column - (columns - 1) * 0.5f) * Pitch(kind);
            return new Vector3(x, heights[row], ItemSetback);
        }

        /// <summary>How many of one item stand side by side in a slot, for an item <paramref name="size"/> m across (1 to 3).</summary>
        public static int SamplesPerSlot(StationKind kind, float size)
        {
            float room = Pitch(kind) - 0.03f;
            int n = size > 0f ? Mathf.FloorToInt(room / (size + 0.01f)) : 1;
            return Mathf.Clamp(n, 1, 3);
        }
    }
}
