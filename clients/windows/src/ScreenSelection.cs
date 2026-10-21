using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ZhiyiDesktop
{
    internal sealed class ScreenSelection : Form
    {
        private readonly Bitmap snapshot;
        private Point origin;
        private Rectangle selection;
        private bool drawing;
        private readonly string instruction;
        internal Rectangle SelectedArea { get { return new Rectangle(Left + selection.Left, Top + selection.Top, selection.Width, selection.Height); } }

        internal ScreenSelection(Rectangle windowBounds, bool clip)
        {
            Text = "知意 · 框选内容";
            Bounds = Rectangle.Intersect(windowBounds, SystemInformation.VirtualScreen);
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = true;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            snapshot = WeChatCapture.Snapshot(Bounds);
            instruction = clip ? "框住正在播放的视频 · 连续采样约 10 秒画面，不录声音 · Esc 取消" : "拖动框住要回复的消息或文章 · 松手确认 · Esc 取消";
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
            MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; Close(); return; } origin = e.Location; drawing = true; };
            MouseMove += delegate(object sender, MouseEventArgs e) { if (!drawing) return; selection = NormalizeSelection(origin, e.Location, ClientRectangle); Invalidate(); };
            MouseUp += delegate(object sender, MouseEventArgs e) {
                if (!drawing || e.Button != MouseButtons.Left) return;
                drawing = false;
                selection = NormalizeSelection(origin, e.Location, ClientRectangle);
                if (selection.Width < 40 || selection.Height < 40) { selection = Rectangle.Empty; Invalidate(); return; }
                DialogResult = DialogResult.OK;
                Close();
            };
        }

        internal static Rectangle NormalizeSelection(Point from, Point to, Rectangle bounds)
        {
            return Rectangle.Intersect(Rectangle.FromLTRB(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y), Math.Max(from.X, to.X), Math.Max(from.Y, to.Y)), bounds);
        }

        internal byte[] SelectedImage()
        {
            using (var cropped = snapshot.Clone(selection, snapshot.PixelFormat)) return WeChatCapture.EncodeImage(cropped);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(snapshot, 0, 0);
            using (var dark = new SolidBrush(Color.FromArgb(105, 0, 0, 0))) e.Graphics.FillRectangle(dark, ClientRectangle);
            if (!selection.IsEmpty)
            {
                e.Graphics.DrawImage(snapshot, selection, selection, GraphicsUnit.Pixel);
                using (var pen = new Pen(Color.FromArgb(79, 210, 160), 2)) e.Graphics.DrawRectangle(pen, selection);
            }
            using (var bg = new SolidBrush(Color.FromArgb(235, 24, 37, 33))) e.Graphics.FillRectangle(bg, 0, 0, Width, 45);
            using (var font = Theme.Font(10, FontStyle.Regular))
            using (var brush = new SolidBrush(Color.White)) e.Graphics.DrawString(instruction, font, brush, 15, 12);
        }

        protected override void Dispose(bool disposing) { if (disposing) snapshot.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class CaptureProgress : Form
    {
        private readonly Label label = new Label();
        internal bool Cancelled;
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000; return p; } }

        internal CaptureProgress(Rectangle area)
        {
            Text = "知意 · 正在读取画面";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(330, 48);
            BackColor = Theme.Ink;
            var screen = Screen.FromRectangle(area).WorkingArea;
            var positions = new[] { new Point(area.Left, area.Bottom + 8), new Point(area.Left, area.Top - Height - 8), new Point(area.Right + 8, area.Top), new Point(area.Left - Width - 8, area.Top) };
            bool placed = false;
            foreach (var point in positions)
            {
                var candidate = new Rectangle(Math.Max(screen.Left, Math.Min(screen.Right - Width, point.X)), Math.Max(screen.Top, Math.Min(screen.Bottom - Height, point.Y)), Width, Height);
                if (!candidate.IntersectsWith(area)) { Location = candidate.Location; placed = true; break; }
            }
            if (!placed) throw new InvalidOperationException("请只框住视频画面，留出一小块空间显示读取进度。");
            label.SetBounds(12, 12, 240, 26);
            label.ForeColor = Color.White;
            label.Font = Theme.Font(10, FontStyle.Regular);
            var cancel = new Button { Text = "取消", Bounds = new Rectangle(261, 7, 60, 34), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Theme.Ink };
            cancel.Click += delegate { Cancelled = true; };
            Controls.AddRange(new Control[] { label, cancel });
        }
        internal void Report(int count) { label.Text = "正在读取播放画面  " + count + " / 6"; }
    }

    internal static class FrameSheet
    {
        internal static byte[] Compose(IList<Bitmap> frames, IList<double> times)
        {
            if (frames == null || times == null || frames.Count < 1 || frames.Count > 6 || times.Count != frames.Count) throw new ArgumentException("画面采样数量无效。");
            bool portrait = frames[0].Height > frames[0].Width;
            int columns = portrait ? 3 : 2, tileWidth = portrait ? 420 : 630, tileHeight = portrait ? 580 : 360;
            int rows = (frames.Count + columns - 1) / columns;
            using (var sheet = new Bitmap(columns * tileWidth, rows * (tileHeight + 34)))
            using (var g = Graphics.FromImage(sheet))
            using (var font = Theme.Font(13, FontStyle.Bold))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                for (int i = 0; i < frames.Count; i++)
                {
                    int x = (i % columns) * tileWidth, y = (i / columns) * (tileHeight + 34);
                    g.DrawString("画面 " + (i + 1) + " · " + times[i].ToString("0.0") + " 秒（无声音）", font, Brushes.Black, x + 10, y + 5);
                    double scale = Math.Min((tileWidth - 12.0) / frames[i].Width, (tileHeight - 12.0) / frames[i].Height);
                    int width = (int)(frames[i].Width * scale), height = (int)(frames[i].Height * scale);
                    g.DrawImage(frames[i], x + (tileWidth - width) / 2, y + 34 + (tileHeight - height) / 2, width, height);
                }
                return WeChatCapture.EncodeImage(sheet);
            }
        }
    }
}
