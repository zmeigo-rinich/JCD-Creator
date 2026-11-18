using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace JcdBuilder
{
    /// <summary>Область просмотра картинки с подвижной рамкой сетки.</summary>
    public class ImageCanvas : Control
    {
        private Bitmap image;
        private RectangleF frame;
        private int gridW;
        private int gridH;
        private byte[] cells;
        private bool showFill = true;
        private byte paintColor = JcdPalette.Black;

        private float scale = 1f;
        private float offX;
        private float offY;

        private int dragMode;          // 0 — нет, 1 — перенос, 2..9 — изменение размера
        private Point lastMouse;
        private bool moved;

        private static readonly SolidBrush[] CellBrushes = CreateCellBrushes();
        private static readonly Pen GridPen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f);
        private static readonly Pen FramePen = new Pen(Color.FromArgb(255, 255, 205, 60), 2f);
        private static readonly SolidBrush HandleBrush = new SolidBrush(Color.White);
        private static readonly Pen HandlePen = new Pen(Color.FromArgb(210, 30, 30, 36), 1f);
        private static readonly SolidBrush DimBrush = new SolidBrush(Color.FromArgb(105, 18, 18, 22));

        public event EventHandler FrameChanged;
        public event EventHandler CellsChanged;

        public ImageCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(42, 42, 48);
            Font = new Font("Segoe UI", 10f);
        }

        static SolidBrush[] CreateCellBrushes()
        {
            SolidBrush[] brushes = new SolidBrush[16];
            for (int i = 0; i < 16; i++)
                brushes[i] = new SolidBrush(Color.FromArgb(215, JcdPalette.ToColor((byte)i)));
            return brushes;
        }

        public Bitmap Image
        {
            get { return image; }
            set
            {
                image = value;
                UpdateTransform();
                Invalidate();
            }
        }

        public RectangleF Frame
        {
            get { return frame; }
            set
            {
                frame = value;
                Invalidate();
            }
        }

        public int GridW
        {
            get { return gridW; }
            set { gridW = value; Invalidate(); }
        }

        public int GridH
        {
            get { return gridH; }
            set { gridH = value; Invalidate(); }
        }

        public byte[] Cells
        {
            get { return cells; }
            set { cells = value; Invalidate(); }
        }

        public bool ShowFill
        {
            get { return showFill; }
            set { showFill = value; Invalidate(); }
        }

        /// <summary>Цвет кисти для ручного закрашивания клеток (15 — стирание).</summary>
        public byte PaintColor
        {
            get { return paintColor; }
            set { paintColor = (byte)(value & 15); }
        }

        void UpdateTransform()
        {
            if (image == null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                scale = 1f;
                offX = 0f;
                offY = 0f;
                return;
            }

            float sx = (ClientSize.Width - 24f) / image.Width;
            float sy = (ClientSize.Height - 24f) / image.Height;
            scale = Math.Min(sx, sy);
            if (scale <= 0.0001f) scale = 0.0001f;
            offX = (ClientSize.Width - image.Width * scale) / 2f;
            offY = (ClientSize.Height - image.Height * scale) / 2f;
        }

        RectangleF FrameToClient()
        {
            return new RectangleF(offX + frame.X * scale, offY + frame.Y * scale,
                                  frame.Width * scale, frame.Height * scale);
        }

        PointF ClientToImage(Point p)
        {
            return new PointF((p.X - offX) / scale, (p.Y - offY) / scale);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            if (image == null)
            {
                TextRenderer.DrawText(g, "Откройте изображение: меню «Файл» → «Открыть изображение...»",
                    Font, ClientRectangle, Color.FromArgb(205, 205, 212),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }

            UpdateTransform();

            RectangleF dst = new RectangleF(offX, offY, image.Width * scale, image.Height * scale);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(image, dst);

            RectangleF fr = FrameToClient();

            FillClipped(g, DimBrush, dst.Left, dst.Top, dst.Width, fr.Top - dst.Top);
            FillClipped(g, DimBrush, dst.Left, fr.Bottom, dst.Width, dst.Bottom - fr.Bottom);
            FillClipped(g, DimBrush, dst.Left, fr.Top, fr.Left - dst.Left, fr.Height);
            FillClipped(g, DimBrush, fr.Right, fr.Top, dst.Right - fr.Right, fr.Height);

            if (gridW > 0 && gridH > 0 && fr.Width > 1f && fr.Height > 1f)
            {
                float cw = fr.Width / gridW;
                float ch = fr.Height / gridH;

                if (showFill && cells != null && cells.Length == gridW * gridH)
                {
                    for (int y = 0; y < gridH; y++)
                    {
                        for (int x = 0; x < gridW; x++)
                        {
                            byte value = cells[y * gridW + x];
                            if (value == JcdPalette.White) continue;
                            g.FillRectangle(CellBrushes[value & 15], fr.Left + x * cw, fr.Top + y * ch, cw + 0.4f, ch + 0.4f);
                        }
                    }
                }

                if (cw >= 2f && ch >= 2f)
                {
                    for (int i = 0; i <= gridW; i++)
                    {
                        float x = fr.Left + i * cw;
                        g.DrawLine(GridPen, x, fr.Top, x, fr.Bottom);
                    }
                    for (int j = 0; j <= gridH; j++)
                    {
                        float y = fr.Top + j * ch;
                        g.DrawLine(GridPen, fr.Left, y, fr.Right, y);
                    }
                }
            }

            g.SmoothingMode = SmoothingMode.None;
            g.DrawRectangle(FramePen, fr.X, fr.Y, fr.Width, fr.Height);

            PointF[] handles = HandlePoints(fr);
            for (int i = 0; i < handles.Length; i++)
            {
                RectangleF r = new RectangleF(handles[i].X - 3.5f, handles[i].Y - 3.5f, 7f, 7f);
                g.FillRectangle(HandleBrush, r);
                g.DrawRectangle(HandlePen, r.X, r.Y, r.Width, r.Height);
            }
        }

        static void FillClipped(Graphics g, Brush brush, float x, float y, float w, float h)
        {
            if (w <= 0f || h <= 0f) return;
            g.FillRectangle(brush, x, y, w, h);
        }

        PointF[] HandlePoints(RectangleF clientFrame)
        {
            float left = clientFrame.Left;
            float midX = clientFrame.Left + clientFrame.Width / 2f;
            float right = clientFrame.Right;
            float top = clientFrame.Top;
            float midY = clientFrame.Top + clientFrame.Height / 2f;
            float bottom = clientFrame.Bottom;

            return new PointF[]
            {
                new PointF(left, top), new PointF(midX, top), new PointF(right, top),
                new PointF(right, midY), new PointF(right, bottom), new PointF(midX, bottom),
                new PointF(left, bottom), new PointF(left, midY)
            };
        }

        int HitHandle(Point p)
        {
            PointF[] handles = HandlePoints(FrameToClient());
            for (int i = 0; i < handles.Length; i++)
            {
                if (Math.Abs(p.X - handles[i].X) <= 6f && Math.Abs(p.Y - handles[i].Y) <= 6f) return i;
            }
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (image == null || e.Button != MouseButtons.Left) return;

            Focus();
            int handle = HitHandle(e.Location);
            if (handle >= 0) dragMode = 2 + handle;
            else if (FrameToClient().Contains(e.Location)) dragMode = 1;
            else return;

            lastMouse = e.Location;
            moved = false;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (image == null) return;

            if (dragMode == 0)
            {
                Cursor = CursorForPoint(e.Location);
                return;
            }

            int dxPixels = e.X - lastMouse.X;
            int dyPixels = e.Y - lastMouse.Y;
            if (!moved && Math.Abs(dxPixels) + Math.Abs(dyPixels) < 3) return;
            moved = true;

            float dx = dxPixels / scale;
            float dy = dyPixels / scale;
            lastMouse = e.Location;

            RectangleF f = frame;
            const float minSize = 6f;

            switch (dragMode)
            {
                case 1:
                    f.X += dx;
                    f.Y += dy;
                    break;
                case 2: // NW
                    f.X += dx;
                    f.Y += dy;
                    f.Width -= dx;
                    f.Height -= dy;
                    break;
                case 3: // N
                    f.Y += dy;
                    f.Height -= dy;
                    break;
                case 4: // NE
                    f.Width += dx;
                    f.Y += dy;
                    f.Height -= dy;
                    break;
                case 5: // E
                    f.Width += dx;
                    break;
                case 6: // SE
                    f.Width += dx;
                    f.Height += dy;
                    break;
                case 7: // S
                    f.Height += dy;
                    break;
                case 8: // SW
                    f.X += dx;
                    f.Width -= dx;
                    f.Height += dy;
                    break;
                case 9: // W
                    f.X += dx;
                    f.Width -= dx;
                    break;
            }

            if (f.Width < minSize) f.Width = minSize;
            if (f.Height < minSize) f.Height = minSize;
            if (f.X < 0f) f.X = 0f;
            if (f.Y < 0f) f.Y = 0f;
            if (f.Right > image.Width) f.X = image.Width - f.Width;
            if (f.Bottom > image.Height) f.Y = image.Height - f.Height;
            if (f.X < 0f) f.X = 0f;
            if (f.Y < 0f) f.Y = 0f;
            if (f.Width > image.Width) f.Width = image.Width;
            if (f.Height > image.Height) f.Height = image.Height;

            frame = f;
            Invalidate();
            if (FrameChanged != null) FrameChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragMode == 0) return;

            bool click = !moved && dragMode == 1;
            dragMode = 0;
            Capture = false;

            if (click) ToggleCellAt(e.Location);
        }

        Cursor CursorForPoint(Point p)
        {
            int handle = HitHandle(p);
            switch (handle)
            {
                case 0:
                case 4:
                    return Cursors.SizeNWSE;
                case 2:
                case 6:
                    return Cursors.SizeNESW;
                case 1:
                case 5:
                    return Cursors.SizeNS;
                case 3:
                case 7:
                    return Cursors.SizeWE;
            }
            return FrameToClient().Contains(p) ? Cursors.Hand : Cursors.Default;
        }

        void ToggleCellAt(Point p)
        {
            if (gridW <= 0 || gridH <= 0) return;
            if (cells == null || cells.Length != gridW * gridH) return;

            PointF img = ClientToImage(p);
            if (!frame.Contains(img)) return;

            float cw = frame.Width / gridW;
            float ch = frame.Height / gridH;
            if (cw <= 0f || ch <= 0f) return;

            int x = (int)((img.X - frame.X) / cw);
            int y = (int)((img.Y - frame.Y) / ch);
            if (x < 0 || y < 0 || x >= gridW || y >= gridH) return;

            int index = y * gridW + x;
            if (paintColor == JcdPalette.White)
                cells[index] = JcdPalette.White;
            else
                cells[index] = cells[index] == paintColor ? JcdPalette.White : paintColor;
            Invalidate();
            if (CellsChanged != null) CellsChanged(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateTransform();
            Invalidate();
        }
    }
}
