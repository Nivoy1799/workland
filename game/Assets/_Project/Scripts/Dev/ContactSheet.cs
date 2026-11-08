using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Worktown.Game.Art;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 027: one image with every shot and frame of a tour. Pure parts (layout, downsampling, numbers, composing) work
    /// on Color32 arrays in Unity's texture order (bottom row first); <see cref="EncodePng"/> writes the PNG (thread-safe,
    /// so frames are encoded off the main thread).
    /// </summary>
    public static class ContactSheet
    {
        /// <summary>Pixels between cells and around the edge.</summary>
        public const int Gap = 4;
        /// <summary>Size of one digit pixel in the frame numbers.</summary>
        public const int DigitScale = 3;

        /// <summary>Behind and between the cells.</summary>
        public static readonly Color32 Background = Palette.Ink;
        private static readonly Color32 LabelBack = Palette.Charcoal;
        private static readonly Color32 LabelInk = Palette.Interact;

        // 3x5 digits, one row per string, top row first.
        private static readonly string[][] Digits =
        {
            new[] { "###", "#.#", "#.#", "#.#", "###" }, new[] { ".#.", "##.", ".#.", ".#.", "###" },
            new[] { "###", "..#", "###", "#..", "###" }, new[] { "###", "..#", "###", "..#", "###" },
            new[] { "#.#", "#.#", "###", "..#", "..#" }, new[] { "###", "#..", "###", "..#", "###" },
            new[] { "###", "#..", "###", "#.#", "###" }, new[] { "###", "..#", "..#", "..#", "..#" },
            new[] { "###", "#.#", "###", "#.#", "###" }, new[] { "###", "#.#", "###", "..#", "###" },
        };

        /// <summary>
        /// Rows and size of a sheet with <paramref name="count"/> cells in at most <paramref name="columns"/> columns
        /// (fewer if there are fewer cells). Zero cells give an empty 0x0 layout.
        /// </summary>
        public static SheetLayout Layout(int count, int columns, Vector2Int cellSize)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "count must not be negative");
            if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns), "at least one column");
            if (cellSize.x < 1 || cellSize.y < 1) throw new ArgumentOutOfRangeException(nameof(cellSize), "cells need a size");
            if (count == 0) return new SheetLayout(0, 0, 0, cellSize.x, cellSize.y, Gap);
            int used = Math.Min(columns, count);
            int rows = (count + used - 1) / used;
            return new SheetLayout(count, used, rows, cellSize.x, cellSize.y, Gap);
        }

        /// <summary>Box-filters an image to <paramref name="width"/> x <paramref name="height"/> (any ratio; alpha becomes opaque).</summary>
        public static Color32[] Downsample(Color32[] source, int sourceWidth, int sourceHeight, int width, int height)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (sourceWidth < 1 || sourceHeight < 1 || source.Length < sourceWidth * sourceHeight)
                throw new ArgumentException("source size does not match the pixels");
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "target needs a size");
            var result = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                int y0 = y * sourceHeight / height;
                int y1 = Math.Max(y0 + 1, (y + 1) * sourceHeight / height);
                for (int x = 0; x < width; x++)
                {
                    int x0 = x * sourceWidth / width;
                    int x1 = Math.Max(x0 + 1, (x + 1) * sourceWidth / width);
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int sy = y0; sy < y1; sy++)
                    {
                        int row = sy * sourceWidth;
                        for (int sx = x0; sx < x1; sx++)
                        {
                            Color32 c = source[row + sx];
                            r += c.r;
                            g += c.g;
                            b += c.b;
                            n++;
                        }
                    }
                    int i = y * width + x;
                    result[i].r = (byte)(r / n);
                    result[i].g = (byte)(g / n);
                    result[i].b = (byte)(b / n);
                    result[i].a = 255;
                }
            }
            return result;
        }

        /// <summary>Width in pixels of <paramref name="number"/> drawn by <see cref="DrawNumber"/>, without the frame.</summary>
        public static int NumberWidth(int number, int scale)
        {
            int digits = Math.Max(1, Math.Abs(number).ToString().Length);
            return digits * 4 * scale - scale;
        }

        /// <summary>
        /// Draws a non-negative number in a dark box whose top-left corner is <paramref name="left"/>, <paramref name="top"/>
        /// (counted from the top of the image). Pixels outside the image are skipped.
        /// </summary>
        public static void DrawNumber(Color32[] pixels, int width, int height, int left, int top, int number, int scale)
        {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (number < 0) throw new ArgumentOutOfRangeException(nameof(number), "numbers are not negative");
            scale = Math.Max(1, scale);
            string text = number.ToString();
            int boxWidth = NumberWidth(number, scale) + 2 * scale;
            int boxHeight = 5 * scale + 2 * scale;
            Fill(pixels, width, height, left, top, boxWidth, boxHeight, LabelBack);
            for (int d = 0; d < text.Length; d++)
            {
                string[] glyph = Digits[text[d] - '0'];
                int glyphLeft = left + scale + d * 4 * scale;
                for (int row = 0; row < 5; row++)
                    for (int col = 0; col < 3; col++)
                        if (glyph[row][col] == '#')
                            Fill(pixels, width, height, glyphLeft + col * scale, top + scale + row * scale, scale, scale, LabelInk);
            }
        }

        /// <summary>
        /// The whole sheet: each cell (already <see cref="SheetLayout.CellWidth"/> x <see cref="SheetLayout.CellHeight"/>,
        /// bottom row first) at its place, with <paramref name="numbers"/>[i] in its top-left corner (null = 1, 2, 3, ...).
        /// A missing cell (null) stays background.
        /// </summary>
        public static Color32[] Compose(SheetLayout layout, IReadOnlyList<Color32[]> cells, IReadOnlyList<int> numbers = null)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            var pixels = new Color32[layout.Width * layout.Height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Background;
            int count = Math.Min(layout.Count, cells.Count);
            for (int i = 0; i < count; i++)
            {
                Color32[] cell = cells[i];
                RectInt rect = layout.Cell(i);
                if (cell != null && cell.Length >= layout.CellWidth * layout.CellHeight)
                {
                    for (int y = 0; y < layout.CellHeight; y++)
                    {
                        // Cell row y (bottom first) goes to sheet row (from the bottom) Height - 1 - (top + CellHeight - 1 - y).
                        int sheetRow = layout.Height - rect.y - layout.CellHeight + y;
                        Array.Copy(cell, y * layout.CellWidth, pixels, sheetRow * layout.Width + rect.x, layout.CellWidth);
                    }
                }
                int number = numbers != null && i < numbers.Count ? numbers[i] : i + 1;
                DrawNumber(pixels, layout.Width, layout.Height, rect.x + 2, rect.y + 2, Math.Max(0, number), DigitScale);
            }
            return pixels;
        }

        /// <summary>PNG bytes of an image in texture order. Safe to call from any thread.</summary>
        public static byte[] EncodePng(Color32[] pixels, int width, int height)
        {
            return ImageConversion.EncodeArrayToPNG(pixels, GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height);
        }

        /// <summary>Makes every pixel opaque (the back buffer's alpha is not meaningful).</summary>
        public static void MakeOpaque(Color32[] pixels)
        {
            if (pixels == null) return;
            for (int i = 0; i < pixels.Length; i++) pixels[i].a = 255;
        }

        // Fills a rectangle given from the top-left corner, clipped to the image.
        private static void Fill(Color32[] pixels, int width, int height, int left, int top, int w, int h, Color32 color)
        {
            for (int y = top; y < top + h; y++)
            {
                if (y < 0 || y >= height) continue;
                int row = (height - 1 - y) * width;
                for (int x = left; x < left + w; x++)
                    if (x >= 0 && x < width) pixels[row + x] = color;
            }
        }
    }
}
