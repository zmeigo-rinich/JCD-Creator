using System;
using System.Drawing;
using System.Windows.Forms;

namespace JcdBuilder
{
    /// <summary>Квадратик палитры для выбора цвета кисти.</summary>
    public class ColorSwatch : Control
    {
        public readonly byte ColorIndex;
        private bool selected;

        public ColorSwatch(byte index)
        {
            ColorIndex = index;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);

            using (SolidBrush brush = new SolidBrush(JcdPalette.ToColor(ColorIndex)))
                g.FillRectangle(brush, 2, 2, Width - 4, Height - 4);

            Color border = selected ? Color.FromArgb(20, 90, 200) : Color.FromArgb(160, 160, 168);
            using (Pen pen = new Pen(border, selected ? 2f : 1f))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

            if (selected)
            {
                using (Pen inner = new Pen(Color.FromArgb(220, 255, 255, 255), 1f))
                    g.DrawRectangle(inner, 3, 3, Width - 7, Height - 7);
            }
        }
    }
}
