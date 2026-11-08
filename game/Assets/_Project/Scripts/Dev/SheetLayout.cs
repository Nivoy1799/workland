using UnityEngine;

namespace Worktown.Game.Dev
{
    /// <summary>Spec 027: where the cells of a contact sheet go. Cell rectangles count from the top-left corner.</summary>
    public readonly struct SheetLayout
    {
        public int Count { get; }
        public int Columns { get; }
        public int Rows { get; }
        public int CellWidth { get; }
        public int CellHeight { get; }
        public int Gap { get; }
        public int Width { get; }
        public int Height { get; }

        public SheetLayout(int count, int columns, int rows, int cellWidth, int cellHeight, int gap)
        {
            Count = count;
            Columns = columns;
            Rows = rows;
            CellWidth = cellWidth;
            CellHeight = cellHeight;
            Gap = gap;
            Width = columns > 0 ? columns * cellWidth + (columns + 1) * gap : 0;
            Height = rows > 0 ? rows * cellHeight + (rows + 1) * gap : 0;
        }

        /// <summary>Cell <paramref name="index"/> (0-based, row by row) as x, y from the top-left, width, height.</summary>
        public RectInt Cell(int index)
        {
            int column = index % Columns;
            int row = index / Columns;
            return new RectInt(Gap + column * (CellWidth + Gap), Gap + row * (CellHeight + Gap), CellWidth, CellHeight);
        }
    }
}
