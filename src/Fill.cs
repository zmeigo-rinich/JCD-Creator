using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace JcdBuilder
{
    /// <summary>Быстрая выборка цвета из картинки: пиксели один раз копируются в память.</summary>
    public class ImageSampler
    {
        public readonly int Width;
        public readonly int Height;
        private int[] pixels;

        public ImageSampler(Bitmap bitmap)
        {
            Width = bitmap.Width;
            Height = bitmap.Height;
            pixels = new int[Width * Height];

            Rectangle rect = new Rectangle(0, 0, Width, Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                if (stride == Width * 4)
                {
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                }
                else
                {
                    byte[] row = new byte[stride];
                    for (int y = 0; y < Height; y++)
                    {
                        Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * stride), row, 0, stride);
                        for (int x = 0; x < Width; x++)
                        {
                            int o = x * 4;
                            pixels[y * Width + x] = row[o] | (row[o + 1] << 8) | (row[o + 2] << 16) | (row[o + 3] << 24);
                        }
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        /// <summary>Средний цвет каждой клетки и её яркость (0..255).</summary>
        public void Sample(RectangleF frame, int cols, int rows, int[] rgb, double[] lum)
        {
            float cellW = frame.Width / cols;
            float cellH = frame.Height / rows;

            for (int cy = 0; cy < rows; cy++)
            {
                for (int cx = 0; cx < cols; cx++)
                {
                    float x0 = frame.X + cx * cellW;
                    float y0 = frame.Y + cy * cellH;
                    float insetX = cellW * 0.10f;
                    float insetY = cellH * 0.10f;

                    int sx0 = (int)Math.Floor(x0 + insetX);
                    int sx1 = (int)Math.Ceiling(x0 + cellW - insetX);
                    int sy0 = (int)Math.Floor(y0 + insetY);
                    int sy1 = (int)Math.Ceiling(y0 + cellH - insetY);

                    if (sx0 < 0) sx0 = 0;
                    if (sy0 < 0) sy0 = 0;
                    if (sx1 > Width - 1) sx1 = Width - 1;
                    if (sy1 > Height - 1) sy1 = Height - 1;
                    if (sx1 < sx0) sx1 = sx0;
                    if (sy1 < sy0) sy1 = sy0;

                    int stepX = Math.Max(1, (sx1 - sx0 + 1) / 16);
                    int stepY = Math.Max(1, (sy1 - sy0 + 1) / 16);

                    long sumR = 0, sumG = 0, sumB = 0;
                    int count = 0;
                    for (int y = sy0; y <= sy1; y += stepY)
                    {
                        int rowOffset = y * Width;
                        for (int x = sx0; x <= sx1; x += stepX)
                        {
                            int v = pixels[rowOffset + x];
                            sumR += (v >> 16) & 255;
                            sumG += (v >> 8) & 255;
                            sumB += v & 255;
                            count++;
                        }
                    }

                    int index = cy * cols + cx;
                    int r = 255, g = 255, b = 255;
                    if (count > 0)
                    {
                        r = (int)(sumR / count);
                        g = (int)(sumG / count);
                        b = (int)(sumB / count);
                    }
                    rgb[index] = (r << 16) | (g << 8) | b;
                    lum[index] = 0.299 * r + 0.587 * g + 0.114 * b;
                }
            }
        }
    }

    public class FillOptions
    {
        public bool AutoThreshold = true;
        public int Threshold = 128;
        public bool Invert = false;
        public bool ColorMode = true;
    }

    /// <summary>Преобразование изображения в клетки кроссворда.</summary>
    public static class FillEngine
    {
        /// <summary>Заполняет клетки кроссворда по выборке цветов. Возвращает применённый порог.</summary>
        public static int Apply(JcdPuzzle puzzle, int[] rgb, double[] lum, FillOptions options)
        {
            int total = puzzle.Cells.Length;
            int[] ink = new int[total];

            int bgR = 255, bgG = 255, bgB = 255;
            if (options.ColorMode) EstimateBackground(rgb, ref bgR, ref bgG, ref bgB);

            for (int i = 0; i < total; i++)
            {
                int value;
                if (options.ColorMode)
                {
                    int r = (rgb[i] >> 16) & 255;
                    int g = (rgb[i] >> 8) & 255;
                    int b = rgb[i] & 255;
                    int dr = r - bgR;
                    int dg = g - bgG;
                    int db = b - bgB;
                    double dist = Math.Sqrt(dr * dr + dg * dg + db * db);
                    value = (int)Math.Round(dist * 255.0 / 441.7);
                }
                else
                {
                    value = (int)Math.Round(255.0 - lum[i]);
                }
                if (value < 0) value = 0;
                if (value > 255) value = 255;
                ink[i] = options.Invert ? 255 - value : value;
            }

            int threshold = options.AutoThreshold ? Otsu(ink) : options.Threshold;

            for (int i = 0; i < total; i++)
            {
                if (ink[i] <= threshold)
                {
                    puzzle.Cells[i] = JcdPalette.White;
                    continue;
                }

                byte cellValue = JcdPalette.Black;
                if (options.ColorMode)
                {
                    int r = (rgb[i] >> 16) & 255;
                    int g = (rgb[i] >> 8) & 255;
                    int b = rgb[i] & 255;
                    int max = Math.Max(r, Math.Max(g, b));
                    int min = Math.Min(r, Math.Min(g, b));
                    if (max >= 90 && max - min >= 45) cellValue = JcdPalette.NearestHue(r, g, b);
                }
                puzzle.Cells[i] = cellValue;
            }

            return threshold;
        }

        /// <summary>Оценка фона — самый частый цвет изображения.</summary>
        static void EstimateBackground(int[] rgb, ref int bgR, ref int bgG, ref int bgB)
        {
            int[] counts = new int[32768];
            for (int i = 0; i < rgb.Length; i++)
            {
                int v = rgb[i];
                int r = (v >> 16) & 255;
                int g = (v >> 8) & 255;
                int b = v & 255;
                counts[((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3)]++;
            }

            int best = 0;
            int bestCount = -1;
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] > bestCount)
                {
                    bestCount = counts[i];
                    best = i;
                }
            }

            long sumR = 0, sumG = 0, sumB = 0;
            int n = 0;
            for (int i = 0; i < rgb.Length; i++)
            {
                int v = rgb[i];
                int r = (v >> 16) & 255;
                int g = (v >> 8) & 255;
                int b = v & 255;
                int key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                if (key == best)
                {
                    sumR += r;
                    sumG += g;
                    sumB += b;
                    n++;
                }
            }

            if (n > 0)
            {
                bgR = (int)(sumR / n);
                bgG = (int)(sumG / n);
                bgB = (int)(sumB / n);
            }
        }

        /// <summary>Автоматический порог методом Оцу.</summary>
        public static int Otsu(int[] values)
        {
            int[] hist = new int[256];
            for (int i = 0; i < values.Length; i++) hist[values[i]]++;

            int total = values.Length;
            double sum = 0;
            for (int i = 0; i < 256; i++) sum += i * (double)hist[i];

            double sumB = 0;
            int weightB = 0;
            double maxVariance = -1;
            int best = 0;

            for (int t = 0; t < 256; t++)
            {
                weightB += hist[t];
                if (weightB == 0) continue;
                int weightF = total - weightB;
                if (weightF == 0) break;
                sumB += t * (double)hist[t];
                double meanB = sumB / weightB;
                double meanF = (sum - sumB) / weightF;
                double variance = (double)weightB * weightF * (meanB - meanF) * (meanB - meanF);
                if (variance > maxVariance)
                {
                    maxVariance = variance;
                    best = t;
                }
            }

            return best;
        }
    }
}
