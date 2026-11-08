using System;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Dev;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 027 AC2: contact sheet layout (rows, columns, sizes, edge cases), downsampling, numbers, composing.</summary>
    public class ContactSheetTests
    {
        private static readonly Vector2Int Cell = new Vector2Int(320, 180);
        private const int G = ContactSheet.Gap;

        [TestCase(1, 6, 1, 1)]
        [TestCase(6, 6, 6, 1)]
        [TestCase(7, 6, 6, 2)]
        [TestCase(12, 6, 6, 2)]
        [TestCase(13, 6, 6, 3)]
        [TestCase(72, 6, 6, 12)]
        [TestCase(3, 6, 3, 1)]
        [TestCase(5, 1, 1, 5)]
        public void Layout_RowsAndColumns(int count, int columns, int usedColumns, int rows)
        {
            SheetLayout layout = ContactSheet.Layout(count, columns, Cell);
            Assert.AreEqual(count, layout.Count);
            Assert.AreEqual(usedColumns, layout.Columns);
            Assert.AreEqual(rows, layout.Rows);
            Assert.AreEqual(usedColumns * 320 + (usedColumns + 1) * G, layout.Width);
            Assert.AreEqual(rows * 180 + (rows + 1) * G, layout.Height);
        }

        [Test]
        public void Layout_CellsGoRowByRowFromTheTopLeft()
        {
            SheetLayout layout = ContactSheet.Layout(8, 6, Cell);
            Assert.AreEqual(new RectInt(G, G, 320, 180), layout.Cell(0));
            Assert.AreEqual(new RectInt(G + 5 * (320 + G), G, 320, 180), layout.Cell(5));
            Assert.AreEqual(new RectInt(G, G + 180 + G, 320, 180), layout.Cell(6));
            RectInt last = layout.Cell(7);
            Assert.LessOrEqual(last.xMax + G, layout.Width);
            Assert.AreEqual(layout.Height, last.yMax + G, "the last row ends one gap above the bottom edge");
        }

        [Test]
        public void Layout_NoCellsIsEmpty()
        {
            SheetLayout layout = ContactSheet.Layout(0, 6, Cell);
            Assert.AreEqual(0, layout.Rows);
            Assert.AreEqual(0, layout.Columns);
            Assert.AreEqual(0, layout.Width);
            Assert.AreEqual(0, layout.Height);
        }

        [Test]
        public void Layout_RejectsBadInput()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ContactSheet.Layout(-1, 6, Cell));
            Assert.Throws<ArgumentOutOfRangeException>(() => ContactSheet.Layout(4, 0, Cell));
            Assert.Throws<ArgumentOutOfRangeException>(() => ContactSheet.Layout(4, 6, new Vector2Int(0, 180)));
            Assert.Throws<ArgumentOutOfRangeException>(() => ContactSheet.Layout(4, 6, new Vector2Int(320, -1)));
        }

        [Test]
        public void Downsample_AveragesBlocks()
        {
            // 4x2 source, bottom row first: left block (2x2) black and white, right block all red.
            var white = new Color32(255, 255, 255, 0);
            var black = new Color32(0, 0, 0, 0);
            var red = new Color32(200, 0, 0, 10);
            Color32[] source = { black, white, red, red, white, black, red, red };
            Color32[] result = ContactSheet.Downsample(source, 4, 2, 2, 1);
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual(new Color32(127, 127, 127, 255), result[0]);
            Assert.AreEqual(new Color32(200, 0, 0, 255), result[1], "alpha becomes opaque");
        }

        [Test]
        public void Downsample_FullFrameToCell()
        {
            var source = new Color32[1280 * 720];
            for (int i = 0; i < source.Length; i++) source[i] = new Color32(10, 20, 30, 255);
            Color32[] cell = ContactSheet.Downsample(source, 1280, 720, 320, 180);
            Assert.AreEqual(320 * 180, cell.Length);
            Assert.AreEqual(new Color32(10, 20, 30, 255), cell[cell.Length / 2]);
            // Upscaling works too (each target pixel takes at least one source pixel).
            Assert.AreEqual(9, ContactSheet.Downsample(new[] { new Color32(1, 2, 3, 4) }, 1, 1, 3, 3).Length);
        }

        [Test]
        public void Downsample_RejectsBadSizes()
        {
            Assert.Throws<ArgumentNullException>(() => ContactSheet.Downsample(null, 1, 1, 1, 1));
            Assert.Throws<ArgumentException>(() => ContactSheet.Downsample(new Color32[3], 2, 2, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ContactSheet.Downsample(new Color32[4], 2, 2, 0, 1));
        }

        [Test]
        public void DrawNumber_StaysInsideItsBoxAndClips()
        {
            const int w = 40, h = 20;
            var pixels = new Color32[w * h];
            ContactSheet.DrawNumber(pixels, w, h, 1, 1, 7, 1);
            int boxWidth = ContactSheet.NumberWidth(7, 1) + 2, boxHeight = 7;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool inBox = x >= 1 && x < 1 + boxWidth && y >= 1 && y < 1 + boxHeight;
                    Color32 c = pixels[(h - 1 - y) * w + x];
                    if (!inBox) Assert.AreEqual(0, c.a, "pixel " + x + "," + y + " outside the box");
                    else Assert.AreEqual(255, c.a);
                }
            // Clipped at the edges without throwing.
            ContactSheet.DrawNumber(pixels, w, h, 35, 15, 123, 2);
            ContactSheet.DrawNumber(pixels, w, h, -10, -10, 5, 3);
            Assert.Throws<ArgumentOutOfRangeException>(() => ContactSheet.DrawNumber(pixels, w, h, 0, 0, -1, 1));
            Assert.AreEqual(7, ContactSheet.NumberWidth(42, 1), "two digits: 3 + 1 + 3 pixels");
        }

        [Test]
        public void Compose_PutsEachCellInItsPlace()
        {
            var size = new Vector2Int(40, 30); // larger than a frame number label
            SheetLayout layout = ContactSheet.Layout(3, 2, size);
            var cells = new Color32[3][];
            for (int i = 0; i < 3; i++)
            {
                cells[i] = new Color32[40 * 30];
                for (int p = 0; p < cells[i].Length; p++) cells[i][p] = new Color32((byte)(50 * (i + 1)), 0, 0, 255);
            }
            // Mark the bottom-left pixel of the first cell, so the row order is checked too.
            cells[0][0] = new Color32(0, 255, 0, 255);
            Color32[] sheet = ContactSheet.Compose(layout, cells);
            Assert.AreEqual(layout.Width * layout.Height, sheet.Length);

            Color32 At(int x, int top) => sheet[(layout.Height - 1 - top) * layout.Width + x];
            RectInt first = layout.Cell(0), second = layout.Cell(1), third = layout.Cell(2);
            Assert.AreEqual(new Color32(0, 255, 0, 255), At(first.x, first.yMax - 1), "bottom-left of cell 1");
            Assert.AreEqual(100, At(second.xMax - 1, second.yMax - 1).r, "cell 2 to the right");
            Assert.AreEqual(150, At(third.xMax - 1, third.yMax - 1).r, "cell 3 on the second row");
            Assert.AreEqual(ContactSheet.Background, At(second.x, third.y + 1), "the missing fourth place stays background");
        }

        [Test]
        public void Compose_EmptyAndMissingCells()
        {
            Assert.AreEqual(0, ContactSheet.Compose(ContactSheet.Layout(0, 6, Cell), new Color32[0][]).Length);
            SheetLayout layout = ContactSheet.Layout(2, 2, new Vector2Int(16, 16));
            Assert.AreEqual(layout.Width * layout.Height, ContactSheet.Compose(layout, new Color32[][] { null, new Color32[3] }).Length);
            Assert.Throws<ArgumentNullException>(() => ContactSheet.Compose(layout, null));
        }

        [Test]
        public void MakeOpaque_SetsAlpha()
        {
            var pixels = new[] { new Color32(1, 2, 3, 0) };
            ContactSheet.MakeOpaque(pixels);
            Assert.AreEqual(255, pixels[0].a);
            Assert.DoesNotThrow(() => ContactSheet.MakeOpaque(null));
        }

        [Test]
        public void EncodePng_WritesAPng()
        {
            byte[] png = ContactSheet.EncodePng(new Color32[4 * 2], 4, 2);
            Assert.Greater(png.Length, 8);
            Assert.AreEqual(0x89, png[0]);
            Assert.AreEqual((byte)'P', png[1]);
        }
    }
}
