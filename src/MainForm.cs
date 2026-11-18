using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace JcdBuilder
{
    public class MainForm : Form
    {
        private const string AppName = "JCD Creator";

        private Bitmap source;
        private ImageSampler sampler;
        private RectangleF frame;
        private int cols = 15;
        private int rows = 15;
        private int[] cellRgb;
        private double[] cellLum;
        private byte[] cells;
        private bool autoRefill;
        private int lastThreshold;
        private string imagePath = "";
        private Timer refillTimer;

        private ImageCanvas canvas;
        private NumericUpDown nudCols;
        private NumericUpDown nudRows;
        private TrackBar tbThreshold;
        private Label lblThreshold;
        private CheckBox chkAuto;
        private CheckBox chkInvert;
        private CheckBox chkColor;
        private CheckBox chkShow;
        private ToolStripStatusLabel lblStatus;
        private ColorSwatch[] swatches;
        private byte selectedColor = JcdPalette.Black;

        public MainForm()
        {
            Text = AppName;
            ClientSize = new Size(1080, 720);
            MinimumSize = new Size(900, 660);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            canvas = new ImageCanvas();
            canvas.Dock = DockStyle.Fill;
            canvas.FrameChanged += delegate { OnFrameChanged(); };
            canvas.CellsChanged += delegate
            {
                autoRefill = false;
                UpdateStatus();
            };
            Controls.Add(canvas);

            Panel side = new Panel();
            side.Dock = DockStyle.Right;
            side.Width = 264;
            side.AutoScroll = true;
            side.BackColor = Color.FromArgb(244, 245, 248);
            side.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(side);
            BuildSidePanel(side);

            StatusStrip status = new StatusStrip();
            lblStatus = new ToolStripStatusLabel();
            lblStatus.Spring = true;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            status.Items.Add(lblStatus);
            Controls.Add(status);

            MenuStrip menu = BuildMenu();
            Controls.Add(menu);
            MainMenuStrip = menu;

            refillTimer = new Timer();
            refillTimer.Interval = 200;
            refillTimer.Tick += delegate
            {
                refillTimer.Stop();
                if (autoRefill) DoFill();
            };

            UpdateStatus();
        }

        // ---------------------------------------------------------------- интерфейс

        void BuildSidePanel(Panel side)
        {
            const int x = 12;
            const int w = 238;

            Button btnOpen = new Button();
            btnOpen.Text = "Открыть изображение...";
            btnOpen.Bounds = new Rectangle(x, 10, w, 32);
            btnOpen.Click += delegate { OpenImageDialog(); };
            side.Controls.Add(btnOpen);

            Label lblCols = new Label();
            lblCols.Text = "Колонок:";
            lblCols.Bounds = new Rectangle(x, 54, 100, 20);
            side.Controls.Add(lblCols);

            nudCols = new NumericUpDown();
            nudCols.Bounds = new Rectangle(x + 110, 52, 128, 24);
            nudCols.Minimum = 5;
            nudCols.Maximum = 80;
            nudCols.Value = cols;
            nudCols.ValueChanged += delegate { OnGridSizeChanged(); };
            side.Controls.Add(nudCols);

            Label lblRows = new Label();
            lblRows.Text = "Строк:";
            lblRows.Bounds = new Rectangle(x, 84, 100, 20);
            side.Controls.Add(lblRows);

            nudRows = new NumericUpDown();
            nudRows.Bounds = new Rectangle(x + 110, 82, 128, 24);
            nudRows.Minimum = 5;
            nudRows.Maximum = 80;
            nudRows.Value = rows;
            nudRows.ValueChanged += delegate { OnGridSizeChanged(); };
            side.Controls.Add(nudRows);

            GroupBox gbFill = new GroupBox();
            gbFill.Text = "Заливка";
            gbFill.Bounds = new Rectangle(x, 106, w, 190);
            side.Controls.Add(gbFill);

            chkAuto = new CheckBox();
            chkAuto.Text = "Авто-порог (по методу Оцу)";
            chkAuto.Bounds = new Rectangle(12, 20, 200, 22);
            chkAuto.Checked = true;
            chkAuto.CheckedChanged += delegate
            {
                tbThreshold.Enabled = !chkAuto.Checked;
                OnFillOptionsChanged();
            };
            gbFill.Controls.Add(chkAuto);

            lblThreshold = new Label();
            lblThreshold.Text = "Порог: 128";
            lblThreshold.Bounds = new Rectangle(12, 46, 200, 18);
            gbFill.Controls.Add(lblThreshold);

            tbThreshold = new TrackBar();
            tbThreshold.Bounds = new Rectangle(8, 64, 214, 45);
            tbThreshold.Minimum = 0;
            tbThreshold.Maximum = 255;
            tbThreshold.TickFrequency = 32;
            tbThreshold.SmallChange = 1;
            tbThreshold.LargeChange = 8;
            tbThreshold.Value = 128;
            tbThreshold.Enabled = false;
            tbThreshold.Scroll += delegate { OnFillOptionsChanged(); };
            gbFill.Controls.Add(tbThreshold);

            chkInvert = new CheckBox();
            chkInvert.Text = "Инвертировать";
            chkInvert.Bounds = new Rectangle(12, 112, 200, 22);
            chkInvert.CheckedChanged += delegate { OnFillOptionsChanged(); };
            gbFill.Controls.Add(chkInvert);

            chkColor = new CheckBox();
            chkColor.Text = "Цветной кроссворд";
            chkColor.Bounds = new Rectangle(12, 136, 200, 22);
            chkColor.Checked = true;
            chkColor.CheckedChanged += delegate { OnFillOptionsChanged(); };
            gbFill.Controls.Add(chkColor);

            chkShow = new CheckBox();
            chkShow.Text = "Показывать заполнение";
            chkShow.Bounds = new Rectangle(12, 160, 200, 22);
            chkShow.Checked = true;
            chkShow.CheckedChanged += delegate
            {
                if (canvas != null) canvas.ShowFill = chkShow.Checked;
            };
            gbFill.Controls.Add(chkShow);

            GroupBox gbBrush = new GroupBox();
            gbBrush.Text = "Цвет кисти";
            gbBrush.Bounds = new Rectangle(x, 296, w, 86);
            side.Controls.Add(gbBrush);

            swatches = new ColorSwatch[16];
            ToolTip tips = new ToolTip();
            tips.InitialDelay = 300;
            for (int i = 0; i < 16; i++)
            {
                ColorSwatch swatch = new ColorSwatch((byte)i);
                swatch.Size = new Size(27, 22);
                swatch.Location = new Point(10 + (i % 8) * 28, 22 + (i / 8) * 26);
                swatch.Click += delegate(object sender, EventArgs e)
                {
                    SelectBrush(((ColorSwatch)sender).ColorIndex);
                };
                swatches[i] = swatch;
                gbBrush.Controls.Add(swatch);
                tips.SetToolTip(swatch, JcdPalette.Name((byte)i));
            }
            SelectBrush(JcdPalette.Black);

            GroupBox gbGrid = new GroupBox();
            gbGrid.Text = "Сетка";
            gbGrid.Bounds = new Rectangle(x, 390, w, 124);
            side.Controls.Add(gbGrid);

            Button btnFit = new Button();
            btnFit.Text = "Рамка = всё изображение";
            btnFit.Bounds = new Rectangle(10, 22, 218, 28);
            btnFit.Click += delegate { FitFrameToImage(); };
            gbGrid.Controls.Add(btnFit);

            Label hint = new Label();
            hint.Text = "Рамку можно двигать мышью,\nтяните за края и углы.\nЩелчок — закрасить цветом кисти,\nтем же цветом — очистить.";
            hint.Bounds = new Rectangle(10, 52, 218, 66);
            hint.ForeColor = Color.FromArgb(80, 80, 90);
            gbGrid.Controls.Add(hint);

            Button btnFill = new Button();
            btnFill.Text = "Заполнить";
            btnFill.Bounds = new Rectangle(x, 518, w, 38);
            btnFill.Font = new Font(Font, FontStyle.Bold);
            btnFill.Click += delegate { DoFill(); };
            side.Controls.Add(btnFill);

            Button btnClear = new Button();
            btnClear.Text = "Очистить";
            btnClear.Bounds = new Rectangle(x, 562, w, 30);
            btnClear.Click += delegate { ClearCells(); };
            side.Controls.Add(btnClear);

            Button btnSave = new Button();
            btnSave.Text = "Сохранить .jcd...";
            btnSave.Bounds = new Rectangle(x, 600, w, 30);
            btnSave.Click += delegate { SaveDialog(); };
            side.Controls.Add(btnSave);

            Button btnVerify = new Button();
            btnVerify.Text = "Проверить .jcd...";
            btnVerify.Bounds = new Rectangle(x, 638, w, 30);
            btnVerify.Click += delegate { VerifyDialog(); };
            side.Controls.Add(btnVerify);
        }

        MenuStrip BuildMenu()
        {
            MenuStrip menu = new MenuStrip();

            ToolStripMenuItem file = new ToolStripMenuItem("Файл");

            ToolStripMenuItem open = new ToolStripMenuItem("Открыть изображение...");
            open.ShortcutKeys = Keys.Control | Keys.O;
            open.Click += delegate { OpenImageDialog(); };
            file.DropDownItems.Add(open);

            ToolStripMenuItem verify = new ToolStripMenuItem("Открыть .jcd для проверки...");
            verify.Click += delegate { VerifyDialog(); };
            file.DropDownItems.Add(verify);

            file.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem save = new ToolStripMenuItem("Сохранить .jcd...");
            save.ShortcutKeys = Keys.Control | Keys.S;
            save.Click += delegate { SaveDialog(); };
            file.DropDownItems.Add(save);

            file.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("Выход");
            exit.Click += delegate { Close(); };
            file.DropDownItems.Add(exit);

            ToolStripMenuItem help = new ToolStripMenuItem("Справка");
            ToolStripMenuItem about = new ToolStripMenuItem("О программе");
            about.Click += delegate { ShowAbout(); };
            help.DropDownItems.Add(about);

            menu.Items.Add(file);
            menu.Items.Add(help);
            return menu;
        }

        // ---------------------------------------------------------------- события

        bool suppressGrid;

        void SetNumericSilently(NumericUpDown box, int value)
        {
            suppressGrid = true;
            try
            {
                if (value < box.Minimum) value = (int)box.Minimum;
                if (value > box.Maximum) value = (int)box.Maximum;
                box.Value = value;
            }
            finally
            {
                suppressGrid = false;
            }
        }

        void OnGridSizeChanged()
        {
            if (suppressGrid || canvas == null) return;

            cols = (int)nudCols.Value;
            rows = (int)nudRows.Value;
            canvas.GridW = cols;
            canvas.GridH = rows;

            if (cells != null && cells.Length != cols * rows)
            {
                cells = null;
                autoRefill = false;
                canvas.Cells = null;
            }

            if (autoRefill) ScheduleRefill();
            UpdateStatus();
        }

        void OnFillOptionsChanged()
        {
            if (lblThreshold != null) lblThreshold.Text = "Порог: " + tbThreshold.Value;
            if (autoRefill) ScheduleRefill();
        }

        void SelectBrush(byte index)
        {
            selectedColor = (byte)(index & 15);
            if (swatches != null)
            {
                for (int i = 0; i < swatches.Length; i++)
                {
                    if (swatches[i] != null) swatches[i].Selected = (i == selectedColor);
                }
            }
            if (canvas != null) canvas.PaintColor = selectedColor;
            UpdateStatus();
        }

        void OnFrameChanged()
        {
            if (canvas == null) return;
            frame = canvas.Frame;
            if (autoRefill) ScheduleRefill();
            UpdateStatus();
        }

        void ScheduleRefill()
        {
            if (refillTimer == null) return;
            refillTimer.Stop();
            refillTimer.Start();
        }

        // ---------------------------------------------------------------- действия

        void OpenImageDialog()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Открыть изображение";
                dialog.Filter = "Изображения (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Все файлы (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                LoadImage(dialog.FileName);
            }
        }

        void LoadImage(string path)
        {
            Cursor previous = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap bitmap;
                using (Image loaded = System.Drawing.Image.FromFile(path))
                {
                    bitmap = new Bitmap(loaded.Width, loaded.Height, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(loaded, 0, 0, loaded.Width, loaded.Height);
                    }
                }

                if (source != null) source.Dispose();
                source = bitmap;
                sampler = new ImageSampler(source);
                imagePath = path;
                frame = new RectangleF(0, 0, source.Width, source.Height);

                int suggestedCols;
                int suggestedRows;
                SuggestGrid(source.Width, source.Height, out suggestedCols, out suggestedRows);

                autoRefill = false;
                cells = null;
                cols = suggestedCols;
                rows = suggestedRows;
                SetNumericSilently(nudCols, suggestedCols);
                SetNumericSilently(nudRows, suggestedRows);

                canvas.Image = source;
                canvas.Frame = frame;
                canvas.Cells = null;
                canvas.GridW = cols;
                canvas.GridH = rows;

                Text = AppName + " — " + Path.GetFileName(path);
                UpdateStatus();
                DoFill();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось открыть изображение:\n" + ex.Message,
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = previous;
            }
        }

        static void SuggestGrid(int width, int height, out int colsOut, out int rowsOut)
        {
            const int target = 20;
            if (width >= height)
            {
                colsOut = target;
                rowsOut = (int)Math.Round((double)height * target / width);
            }
            else
            {
                rowsOut = target;
                colsOut = (int)Math.Round((double)width * target / height);
            }
            if (colsOut < 5) colsOut = 5;
            if (colsOut > 80) colsOut = 80;
            if (rowsOut < 5) rowsOut = 5;
            if (rowsOut > 80) rowsOut = 80;
        }

        void FitFrameToImage()
        {
            if (source == null) return;
            frame = new RectangleF(0, 0, source.Width, source.Height);
            canvas.Frame = frame;
            OnFrameChanged();
        }

        void DoFill()
        {
            if (source == null || sampler == null)
            {
                MessageBox.Show(this, "Сначала откройте изображение.", "Заливка",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            cols = (int)nudCols.Value;
            rows = (int)nudRows.Value;

            Cursor previous = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                int total = cols * rows;
                if (cellRgb == null || cellRgb.Length != total)
                {
                    cellRgb = new int[total];
                    cellLum = new double[total];
                }

                sampler.Sample(frame, cols, rows, cellRgb, cellLum);

                JcdPuzzle puzzle = new JcdPuzzle(cols, rows);
                FillOptions options = new FillOptions();
                options.AutoThreshold = chkAuto.Checked;
                options.Threshold = tbThreshold.Value;
                options.Invert = chkInvert.Checked;
                options.ColorMode = chkColor.Checked;

                lastThreshold = FillEngine.Apply(puzzle, cellRgb, cellLum, options);

                cells = puzzle.Cells;
                autoRefill = true;

                canvas.GridW = cols;
                canvas.GridH = rows;
                canvas.Cells = cells;
                canvas.ShowFill = chkShow.Checked;
                canvas.Invalidate();

                lblThreshold.Text = "Порог: " + lastThreshold + (chkAuto.Checked ? " (авто)" : "");
                UpdateStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Ошибка заливки:\n" + ex.Message, "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = previous;
            }
        }

        void ClearCells()
        {
            if (cells == null) return;
            for (int i = 0; i < cells.Length; i++) cells[i] = JcdPalette.White;
            autoRefill = false;
            canvas.Cells = cells;
            canvas.Invalidate();
            UpdateStatus();
        }

        JcdPuzzle CurrentPuzzle()
        {
            if (cells == null || cells.Length != cols * rows) return null;
            JcdPuzzle puzzle = new JcdPuzzle(cols, rows);
            Buffer.BlockCopy(cells, 0, puzzle.Cells, 0, cells.Length);
            return puzzle;
        }

        void SaveDialog()
        {
            if (source == null)
            {
                MessageBox.Show(this, "Сначала откройте изображение.", "Сохранение",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            JcdPuzzle puzzle = CurrentPuzzle();
            if (puzzle == null)
            {
                MessageBox.Show(this, "Сначала заполните сетку кнопкой «Заполнить».", "Сохранение",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Сохранить кроссворд";
                dialog.Filter = "Кроссворд JCD (*.jcd)|*.jcd";
                dialog.DefaultExt = "jcd";
                dialog.AddExtension = true;
                dialog.FileName = SuggestedFileName();
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    string title = Path.GetFileNameWithoutExtension(dialog.FileName);
                    JcdFormat.Save(dialog.FileName, puzzle, title);

                    int runs;
                    JcdPuzzle check = JcdFormat.Load(dialog.FileName, out runs);
                    bool ok = check.SameAs(puzzle);

                    FileInfo info = new FileInfo(dialog.FileName);
                    string message = "Файл сохранён:\n" + dialog.FileName +
                        "\n\nРазмер: " + info.Length + " байт" +
                        "\nСетка: " + puzzle.Width + "×" + puzzle.Height +
                        "\nЗаполнено клеток: " + puzzle.FilledCount() + " из " + puzzle.Cells.Length +
                        "\nЧисел в определении: " + runs +
                        "\nИспользовано цветов: " + puzzle.UsedColorCount() +
                        "\n\nПроверка чтением: " + (ok ? "успешно" : "РАСХОЖДЕНИЕ!");

                    MessageBox.Show(this, message, "Готово", MessageBoxButtons.OK,
                        ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                    UpdateStatus();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Не удалось сохранить файл:\n" + ex.Message, "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        string SuggestedFileName()
        {
            string name = "crossword";
            if (!string.IsNullOrEmpty(imagePath))
            {
                try { name = Path.GetFileNameWithoutExtension(imagePath); }
                catch (ArgumentException) { }
            }
            if (string.IsNullOrEmpty(name)) name = "crossword";
            return name + ".jcd";
        }

        void VerifyDialog()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Открыть .jcd для проверки";
                dialog.Filter = "Кроссворд JCD (*.jcd)|*.jcd|Все файлы (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    int runs;
                    JcdPuzzle puzzle = JcdFormat.Load(dialog.FileName, out runs);
                    FileInfo info = new FileInfo(dialog.FileName);
                    ShowPreview(puzzle, dialog.FileName, runs, info.Length);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Не удалось прочитать файл:\n" + ex.Message, "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        void ShowPreview(JcdPuzzle puzzle, string path, int runs, long fileSize)
        {
            const int cellSize = 20;
            Bitmap preview = new Bitmap(puzzle.Width * cellSize, puzzle.Height * cellSize, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(preview)) g.Clear(Color.White);

            if (source != null) source.Dispose();
            source = preview;
            sampler = new ImageSampler(source);
            imagePath = Path.GetFileName(path);
            frame = new RectangleF(0, 0, preview.Width, preview.Height);

            autoRefill = false;
            cols = puzzle.Width;
            rows = puzzle.Height;
            cells = puzzle.Cells;

            SetNumericSilently(nudCols, cols);
            SetNumericSilently(nudRows, rows);

            canvas.Image = preview;
            canvas.Frame = frame;
            canvas.GridW = cols;
            canvas.GridH = rows;
            canvas.Cells = cells;

            chkShow.Checked = true;
            canvas.ShowFill = true;

            Text = AppName + " — " + Path.GetFileName(path);
            UpdateStatus();

            MessageBox.Show(this,
                "Файл прочитан успешно.\n\n" + Path.GetFileName(path) +
                "\nРазмер файла: " + fileSize + " байт" +
                "\nСетка: " + puzzle.Width + "×" + puzzle.Height +
                "\nЗаполнено клеток: " + puzzle.FilledCount() + " из " + puzzle.Cells.Length +
                "\nЧисел в определении: " + runs +
                "\nИспользовано цветов: " + puzzle.UsedColorCount(),
                "Проверка .jcd", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void UpdateStatus()
        {
            if (lblStatus == null) return;

            string text;
            if (source == null) text = "Изображение не открыто";
            else text = "Изображение: " + source.Width + "×" + source.Height + " px";

            text += "   ·   сетка " + cols + "×" + rows;
            text += "   ·   кисть " + JcdPalette.Name(selectedColor);

            if (cells != null && cells.Length == cols * rows)
            {
                int filled = 0;
                for (int i = 0; i < cells.Length; i++)
                    if (cells[i] != JcdPalette.White) filled++;
                text += "   ·   заполнено " + filled + " из " + cells.Length +
                        "   ·   порог " + lastThreshold;
            }
            else
            {
                text += "   ·   нажмите «Заполнить»";
            }

            lblStatus.Text = text;
        }

        void ShowAbout()
        {
            MessageBox.Show(this,
                "JCD Creator — построение японского кроссворда из картинки.\n" +
                "Версия 1.0.",
                "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
