using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace JcdBuilder
{
    /// <summary>Кроссворд: размеры и клетки (строка за строкой).</summary>
    public class JcdPuzzle
    {
        public int Width;
        public int Height;

        /// <summary>Клетки: индекс цвета палитры (15 — пустая клетка).</summary>
        public byte[] Cells;

        public JcdPuzzle(int width, int height)
        {
            Width = width;
            Height = height;
            Cells = new byte[width * height];
            for (int i = 0; i < Cells.Length; i++) Cells[i] = JcdPalette.White;
        }

        public JcdPuzzle Clone()
        {
            JcdPuzzle p = new JcdPuzzle(Width, Height);
            Buffer.BlockCopy(Cells, 0, p.Cells, 0, Cells.Length);
            return p;
        }

        public byte Get(int x, int y)
        {
            return Cells[y * Width + x];
        }

        public void Set(int x, int y, byte value)
        {
            Cells[y * Width + x] = value;
        }

        public int FilledCount()
        {
            int n = 0;
            for (int i = 0; i < Cells.Length; i++)
                if (Cells[i] != JcdPalette.White) n++;
            return n;
        }

        public int UsedColorCount()
        {
            bool[] used = new bool[16];
            for (int i = 0; i < Cells.Length; i++) used[Cells[i] & 15] = true;
            int n = 0;
            for (int i = 0; i < 16; i++) if (used[i]) n++;
            return n;
        }

        public bool SameAs(JcdPuzzle other)
        {
            if (other == null || other.Width != Width || other.Height != Height) return false;
            for (int i = 0; i < Cells.Length; i++)
                if (Cells[i] != other.Cells[i]) return false;
            return true;
        }
    }

    /// <summary>Фиксированная 16-цветная палитра JCD.</summary>
    public static class JcdPalette
    {
        public const byte Black = 0;
        public const byte White = 15;

        /// <summary>Цвета палитры в виде 0xRRGGBB.</summary>
        public static readonly int[] Rgb = new int[16]
        {
            0x000000, 0x808080, 0xC0C0C0, 0xB50000,
            0xFF0000, 0x808000, 0xFFFF00, 0x008000,
            0x00FF00, 0x00B5B5, 0x00FFFF, 0x0000B5,
            0x0000FF, 0xB500B5, 0xFF00FF, 0xFAF0F0
        };

        static readonly string[] Names = new string[16]
        {
            "чёрный", "серый", "светло-серый", "тёмно-красный",
            "красный", "оливковый", "жёлтый", "тёмно-зелёный",
            "зелёный", "бирюзовый", "голубой", "тёмно-синий",
            "синий", "фиолетовый", "пурпурный", "белый (пусто)"
        };

        public static Color ToColor(byte index)
        {
            int v = Rgb[index & 15];
            return Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
        }

        public static string Name(byte index)
        {
            return Names[index & 15];
        }

        /// <summary>Ближайший цветной оттенок палитры (3..14).</summary>
        public static byte NearestHue(int r, int g, int b)
        {
            int max = Math.Max(r, Math.Max(g, b));
            int min = Math.Min(r, Math.Min(g, b));
            int delta = max - min;
            if (max <= 0) return Black;

            double hue;
            if (delta == 0)
                hue = 0;
            else if (max == r)
                hue = 60.0 * (((g - b) / (double)delta) % 6.0);
            else if (max == g)
                hue = 60.0 * ((b - r) / (double)delta + 2.0);
            else
                hue = 60.0 * ((r - g) / (double)delta + 4.0);
            if (hue < 0) hue += 360.0;

            int[] anchors = new int[] { 0, 60, 120, 180, 240, 300 };
            byte[] dark = new byte[] { 3, 5, 7, 9, 11, 13 };
            byte[] light = new byte[] { 4, 6, 8, 10, 12, 14 };

            int best = 0;
            double bestDiff = 1000.0;
            for (int i = 0; i < anchors.Length; i++)
            {
                double diff = Math.Abs(hue - anchors[i]);
                if (diff > 180.0) diff = 360.0 - diff;
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = i;
                }
            }

            return Distance2(r, g, b, dark[best]) <= Distance2(r, g, b, light[best]) ? dark[best] : light[best];
        }

        static int Distance2(int r, int g, int b, byte paletteIndex)
        {
            int v = Rgb[paletteIndex];
            int dr = r - ((v >> 16) & 255);
            int dg = g - ((v >> 8) & 255);
            int db = b - (v & 255);
            return dr * dr + dg * dg + db * db;
        }
    }
}
