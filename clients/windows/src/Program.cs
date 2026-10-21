using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZhiyiDesktop
{
    internal static class Theme
    {
        internal static readonly Color Canvas = Color.FromArgb(248, 248, 244);
        internal static readonly Color Paper = Color.White;
        internal static readonly Color Ink = Color.FromArgb(23, 28, 26);
        internal static readonly Color Muted = Color.FromArgb(104, 116, 111);
        internal static readonly Color Green = Color.FromArgb(40, 107, 84);
        internal static readonly Color PaleGreen = Color.FromArgb(238, 245, 241);
        internal static readonly Color Line = Color.FromArgb(223, 229, 225);
        internal static readonly Color Yellow = Color.FromArgb(224, 179, 61);
        internal static Font Font(float size, FontStyle style) { return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point); }
    }

    internal sealed class BubbleForm : Form
    {
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly AssistantForm assistant;
        private readonly Form demo;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolTip tip = new ToolTip();
        private WeChatWindow currentWindow;
        private bool suspended, dragged, moving, pinned;
        private Point mouseOrigin, startLocation;
        internal bool IsDemo { get { return demo != null; } }
        internal AssistantForm Assistant { get { return assistant; } }
        internal WeChatWindow CurrentWindow { get { return currentWindow; } }
        protected override bool ShowWithoutActivation { get { return true; } }

        internal BubbleForm() : this(null) { }
        internal BubbleForm(Form demonstration)
        {
            demo = demonstration;
            Text = "知意 · 悬浮助手";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(58, 58);
            BackColor = Theme.Green;
            using (var path = new GraphicsPath()) { path.AddEllipse(0, 0, 58, 58); Region = new Region(path); }
            assistant = new AssistantForm(this);
            AllowDrop = true; DragEnter += assistant.AcceptMediaDrop;
            DragDrop += delegate(object sender, DragEventArgs e) { assistant.ShowBeside(currentWindow); assistant.ReceiveMediaDrop(sender, e); };
            menu.Font = Theme.Font(11, FontStyle.Regular);
            menu.ShowImageMargin = false;
            menu.Items.Add("粘贴分享链接 / 消息", null, delegate { assistant.PasteShare(); });
            menu.Items.Add("选取微信视频（免输入）", null, async delegate { await assistant.SelectVideoCard(); });
            menu.Items.Add("搜索视频号", null, delegate { assistant.SearchVideo(); });
            menu.Items.Add("读取已打开的分享链接", null, delegate { assistant.ReadCurrentShare(); });
            menu.Items.Add("框选一条文字消息", null, async delegate { await assistant.SelectContent(false); });
            menu.Items.Add("读取当前窗口", null, async delegate { await assistant.CaptureCurrentWindow(false); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("查看回复 / 导入视频", null, delegate { assistant.ShowBeside(currentWindow); });
            menu.Items.Add("恢复跟随微信", null, delegate { pinned = false; AttachToWeChat(); });
            menu.Items.Add("退出", null, delegate { Close(); });
            ContextMenuStrip = menu;
            tip.SetToolTip(this, "Ctrl+Alt+R 唤起或收起 · Ctrl+Alt+V 选取微信视频");
            Paint += delegate(object sender, PaintEventArgs e) {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var bg = new SolidBrush(currentWindow == null ? Theme.Muted : Theme.Green)) e.Graphics.FillEllipse(bg, 0, 0, 57, 57);
                using (var pen = new Pen(Color.FromArgb(180, 255, 255, 255), 2)) e.Graphics.DrawEllipse(pen, 2, 2, 53, 53);
                using (var font = Theme.Font(17, FontStyle.Bold)) TextRenderer.DrawText(e.Graphics, "回", font, ClientRectangle, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button != MouseButtons.Left) return; moving = true; dragged = false; mouseOrigin = Cursor.Position; startLocation = Location; Capture = true; };
            MouseMove += delegate { if (!moving) return; var delta = new Size(Cursor.Position.X - mouseOrigin.X, Cursor.Position.Y - mouseOrigin.Y); if (Math.Abs(delta.Width) + Math.Abs(delta.Height) > 5) { dragged = true; pinned = true; Location = Point.Add(startLocation, delta); } };
            MouseUp += delegate(object sender, MouseEventArgs e) { if (!moving) return; moving = false; Capture = false; if (!dragged && e.Button == MouseButtons.Left && !assistant.IsBusy) assistant.ShowBeside(currentWindow); };
            timer.Interval = 1000;
            timer.Tick += delegate { AttachToWeChat(); };
            timer.Start();
            AttachToWeChat();
            Shown += delegate { NativeMethods.RegisterHotKey(Handle, 1, 0x4003, (uint)Keys.R); NativeMethods.RegisterHotKey(Handle, 2, 0x4003, (uint)Keys.V); };
        }
        internal void Suspend(bool value) { suspended = value; if (value) { menu.Hide(); Hide(); } else { Show(); AttachToWeChat(); } }
        private void AttachToWeChat()
        {
            if (suspended || moving || menu.Visible || assistant.IsBusy) return;
            if (demo != null)
            {
                currentWindow = demo.IsDisposed ? null : new WeChatWindow { Handle = demo.Handle, Bounds = demo.Bounds, Title = demo.Text };
            }
            else currentWindow = WeChatCapture.FindWindow(currentWindow == null ? IntPtr.Zero : currentWindow.Handle);
            if (!pinned)
            {
                var area = currentWindow == null ? Screen.PrimaryScreen.WorkingArea : Screen.FromRectangle(currentWindow.Bounds).WorkingArea;
                int x = currentWindow == null ? area.Right - Width - 20 : currentWindow.Bounds.Right - Width - 14;
                int y = currentWindow == null ? area.Top + area.Height / 2 : currentWindow.Bounds.Top + currentWindow.Bounds.Height / 2;
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - Height, y)));
            }
            Invalidate();
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1) { BeginInvoke(new Action(delegate { if (assistant.Visible) assistant.Hide(); else assistant.ShowBeside(currentWindow); })); return; }
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 2) { BeginInvoke(new Action(async delegate { await assistant.SelectVideoCard(); })); return; }
            base.WndProc(ref m);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            NativeMethods.UnregisterHotKey(Handle, 1);
            NativeMethods.UnregisterHotKey(Handle, 2);
            timer.Dispose(); tip.Dispose(); menu.Dispose(); assistant.Dispose();
            base.OnFormClosed(e);
        }
    }

    internal sealed class AssistantForm : Form
    {
        private readonly BubbleForm bubble;
        private readonly ApiClient api = new ApiClient();
        private readonly ComboBox recipient = new ComboBox();
        private readonly RecipientMemory memory = new RecipientMemory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zhiyi", "recipients.json"));
        private VideoChoice selectedVideo;
        private readonly PictureBox preview = new PictureBox();
        private readonly Button select = new Button();
        private readonly Button paste = new Button();
        private bool linkInput;
        private string readEvidence = "";
        private readonly Button clip = new Button();
        private readonly Button another = new Button();
        private readonly Button append = new Button();
        private readonly Label coverage = new Label();
        private readonly ToolTip tips = new ToolTip();
        private WeChatWindow originWindow;
        private string captureNote = "";
        private bool demoCapture;
        private CancellationTokenSource requestCancellation;
        internal bool IsBusy { get { return busy; } }
        private readonly TextBox source = new TextBox();
        private readonly TextBox context = new TextBox();
        private readonly TextBox reply = new TextBox();
        private readonly Label status = new Label();
        private readonly Label summary = new Label();
        private readonly TextBox service = new TextBox();
        private readonly Panel settings = new Panel();
        private readonly Button generate = new Button();
        private readonly Button copy = new Button();
        private readonly Button read = new Button();
        private readonly Button import = new Button();
        private CaptureResult capture;
        private AttachmentData attachment;
        private int variant;
        private bool busy;

        internal AssistantForm(BubbleForm owner)
        {
            bubble = owner;
            Text = "知意 · 微信旁边的回复助手";
            ClientSize = new Size(480, 650);
            MinimumSize = new Size(450, 570);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            BackColor = Theme.Canvas;
            Font = Theme.Font(9, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.None;
            AllowDrop = true;
            DragEnter += AcceptMediaDrop;
            DragDrop += ReceiveMediaDrop;
            BuildUi();
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) Scale(new SizeF(graphics.DpiX / 96f, graphics.DpiY / 96f));
        }

        private void BuildUi()
        {
            var header = new Panel { Dock = DockStyle.Top, Width = ClientSize.Width, Height = 72, BackColor = Theme.Paper };
            header.Controls.Add(new Label { Text = "知意", Bounds = new Rectangle(22, 12, 160, 30), Font = Theme.Font(17, FontStyle.Bold), ForeColor = Theme.Ink });
            header.Controls.Add(new Label { Text = bubble.IsDemo ? "演示模式 · 合成聊天素材" : "在当前对话旁，把话回好", Bounds = new Rectangle(23, 44, 280, 20), Font = Theme.Font(9, FontStyle.Regular), ForeColor = Theme.Muted });
            var settingsButton = FlatButton("服务", 54, 32, false);
            settingsButton.Location = new Point(405, 21); settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            settingsButton.Click += delegate { settings.Visible = !settings.Visible; };
            var hide = FlatButton("收起", 54, 32, false);
            hide.Location = new Point(345, 21); hide.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            hide.Click += delegate { Hide(); };
            header.Controls.AddRange(new Control[] { hide, settingsButton });
            settings.Width = ClientSize.Width; settings.Dock = DockStyle.Top; settings.Height = 70; settings.Visible = false; settings.BackColor = Theme.PaleGreen;
            settings.Controls.Add(new Label { Text = "分析服务（密钥保存在服务端）", Bounds = new Rectangle(22, 7, 400, 22), ForeColor = Theme.Muted });
            service.Text = LoadServiceUrl(); service.SetBounds(22, 32, 436, 28); service.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            service.Leave += delegate { SaveServiceUrl(); }; settings.Controls.Add(service);

            var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 72, Padding = new Padding(22, 10, 22, 12), ColumnCount = 2, BackColor = Theme.Paper };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            StyleButton(generate, "生成回复", true); generate.Dock = DockStyle.Fill; generate.Margin = new Padding(0, 0, 8, 0);
            generate.Click += async delegate { await GenerateReply(); };
            StyleButton(copy, "复制 · 回到微信", false); copy.Dock = DockStyle.Fill; copy.Margin = Padding.Empty; copy.Enabled = false;
            copy.Click += CopyAndReturn; footer.Controls.Add(generate, 0, 0); footer.Controls.Add(copy, 1, 0);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(22, 14, 22, 14) };
            var body = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = Padding.Empty, Margin = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.AllowDrop = true; body.DragEnter += AcceptMediaDrop; body.DragDrop += ReceiveMediaDrop;
            var actions = new TableLayoutPanel { Height = 44, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 0, 0, 14) };
            for (int i = 0; i < 2; i++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            StyleButton(paste, "选取微信视频", true); paste.Click += async delegate { await SelectVideoCard(); };
            StyleButton(read, "粘贴链接 / 消息", false); read.Click += delegate { PasteShare(); };
            var actionButtons = new[] { paste, read }; for (int i = 0; i < 2; i++) { actionButtons[i].Dock = DockStyle.Fill; actionButtons[i].Margin = new Padding(i == 0 ? 0 : 7, 0, 0, 0); actions.Controls.Add(actionButtons[i], i, 0); }
            body.Controls.Add(actions);
            var drop = new Label { Text = "微信视频卡片拖不进？点此框选卡片，自动查找\n此处拖入仅支持视频文件或可复制的原链接", Dock = DockStyle.Top, Height = 54, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Green, BackColor = Theme.PaleGreen, AllowDrop = true, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 0, 10) };
            drop.Click += async delegate { await SelectVideoCard(); };
            drop.DragEnter += AcceptMediaDrop; drop.DragDrop += ReceiveMediaDrop; body.Controls.Add(drop);
            status.Text = "选取聊天里的视频卡片，或粘贴消息 / 链接。"; status.ForeColor = Theme.Green; status.Font = Theme.Font(10, FontStyle.Bold); status.Height = 44; status.Dock = DockStyle.Top; status.Margin = new Padding(0, 0, 0, 8); body.Controls.Add(status);
            preview.Height = 76; preview.Dock = DockStyle.Top; preview.BackColor = Theme.PaleGreen; preview.SizeMode = PictureBoxSizeMode.Zoom; preview.Cursor = Cursors.Hand; preview.Margin = new Padding(0, 0, 0, 6);
            preview.Paint += delegate(object sender, PaintEventArgs e) { if (preview.Image == null) using (var font = Theme.Font(10, FontStyle.Regular)) TextRenderer.DrawText(e.Graphics, "链接读取正文 · 视频文件分析语音\n也可框选消息或拖入媒体文件", font, preview.ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); };
            preview.Click += delegate { ShowEvidenceImage(); }; preview.AllowDrop = true; preview.DragEnter += AcceptMediaDrop; preview.DragDrop += ReceiveMediaDrop;
            preview.Visible = false; body.Controls.Add(preview);
            coverage.Text = "文章、视频自动分流；点击生成后开始读取。"; coverage.ForeColor = Theme.Muted; coverage.Font = Theme.Font(9, FontStyle.Regular); coverage.Height = 32; coverage.Dock = DockStyle.Top; coverage.Margin = new Padding(0, 0, 0, 8); body.Controls.Add(coverage);
            var tools = new FlowLayoutPanel { Height = 32, WrapContents = false, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 6) };
            var details = new LinkLabel { Text = "查看依据", AutoSize = true, LinkColor = Theme.Green, Margin = new Padding(0, 3, 18, 0) };
            details.Click += delegate { if (readEvidence.Length > 0) ShowReadEvidence(); else source.Visible = !source.Visible; }; tools.Controls.Add(details);
            StyleButton(import, "导入视频", false); import.Width = 90; import.Height = 28; import.Font = Theme.Font(9, FontStyle.Regular); import.Click += ImportMedia; tools.Controls.Add(import);
            StyleButton(append, "搜索视频号", false); append.Width = 112; append.Height = 28; append.Font = Theme.Font(9, FontStyle.Regular);
            append.Click += delegate { SearchVideo(); }; tools.Controls.Add(append); body.Controls.Add(tools);
            source.Multiline = true; source.ScrollBars = ScrollBars.Vertical; source.MaxLength = 6000; source.Height = 86; source.Dock = DockStyle.Top; source.Visible = true; source.Margin = new Padding(0, 0, 0, 12); body.Controls.Add(source);
            var targetRow = new TableLayoutPanel { ColumnCount = 2, Height = 82, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 8) };
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32)); targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
            targetRow.Controls.Add(new Label { Text = "回复对象", AutoSize = true, ForeColor = Theme.Muted }, 0, 0);
            targetRow.Controls.Add(new Label { Text = "关系与习惯（本机记住）", AutoSize = true, ForeColor = Theme.Muted }, 1, 0);
            foreach (var name in memory.Notes.Keys) recipient.Items.Add(name);
            recipient.TextChanged += delegate { string note; context.Text = memory.Notes.TryGetValue(recipient.Text, out note) ? note : ""; };
            recipient.Dock = DockStyle.Top; recipient.MaxLength = 40; recipient.Margin = new Padding(0, 2, 10, 0); context.Dock = DockStyle.Fill; context.Multiline = true; context.ScrollBars = ScrollBars.Vertical; context.MaxLength = 100; context.Margin = new Padding(0, 2, 0, 0);
            targetRow.Controls.Add(recipient, 0, 1); targetRow.Controls.Add(context, 1, 1); body.Controls.Add(targetRow);
            summary.Text = "读懂内容，再给你一句自然的回复。"; summary.ForeColor = Theme.Ink; summary.Font = Theme.Font(10, FontStyle.Regular); summary.Height = 64; summary.Dock = DockStyle.Top; summary.Margin = new Padding(0, 8, 0, 8); body.Controls.Add(summary);
            var resultHeader = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Margin = Padding.Empty };
            resultHeader.Controls.Add(new Label { Text = "回复草稿", AutoSize = true, Font = Theme.Font(11, FontStyle.Bold), Margin = new Padding(0, 4, 20, 0) });
            StyleButton(another, "换一句", false); another.AutoSize = false; another.Size = new Size(94, 34); another.Font = Theme.Font(9, FontStyle.Regular); another.Enabled = false; another.Click += async delegate { await GenerateReply(); }; resultHeader.Controls.Add(another); body.Controls.Add(resultHeader);
            reply.Multiline = true; reply.ScrollBars = ScrollBars.Vertical; reply.MaxLength = 1000; reply.Height = 116; reply.Dock = DockStyle.Top; reply.Font = Theme.Font(12, FontStyle.Regular); reply.BackColor = Theme.Paper; reply.BorderStyle = BorderStyle.FixedSingle; reply.Margin = new Padding(0, 4, 0, 8); reply.TextChanged += delegate { copy.Enabled = !busy && !String.IsNullOrWhiteSpace(reply.Text); }; body.Controls.Add(reply);
            body.Controls.Add(new Label { Text = "可直接编辑。复制后回到原窗口，由你粘贴、确认发送。", AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Font(9, FontStyle.Regular), Margin = new Padding(0, 0, 0, 8) });
            scroll.Controls.Add(body); Controls.Add(scroll); Controls.Add(footer); Controls.Add(settings); Controls.Add(header);
            tips.SetToolTip(preview, "点击放大查看将要分析的画面"); tips.SetToolTip(clip, "框选正在播放的视频，约 10 秒采样 6 帧；不录声音");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (preview.Image != null) preview.Image.Dispose(); tips.Dispose(); }
            base.Dispose(disposing);
        }
        private void SetPreview(byte[] bytes)
        {
            var previous = preview.Image; preview.Image = null;
            if (previous != null) previous.Dispose();
            if (bytes != null) using (var stream = new MemoryStream(bytes)) using (var image = Image.FromStream(stream)) preview.Image = new Bitmap(image);
            preview.Invalidate();
        }
        private void ShowEvidenceImage()
        {
            if (preview.Image == null) return;
            using (var dialog = new Form { Text = "本次分析画面 · 仅此选区", Size = new Size(850, 760), StartPosition = FormStartPosition.CenterParent, TopMost = true, MinimizeBox = false })
            {
                dialog.Controls.Add(new PictureBox { Image = preview.Image, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Canvas });
                dialog.ShowDialog(this);
            }
        }
        private void ResetMaterial(AttachmentData value, WeChatWindow window, string note, string text)
        {
            attachment = value; selectedVideo = null; originWindow = window; captureNote = note; demoCapture = false; linkInput = false; readEvidence = "";
            reply.Clear(); summary.Text = "核对内容与对象，再点「生成回复」。"; source.Text = text ?? "";
            variant = 0; another.Enabled = false;
            preview.Visible = value != null;
            SetPreview(value != null && value.Kind == "image" ? value.Bytes : null);
            coverage.Text = note; status.Text = bubble.IsDemo ? "演示素材已读取 · 尚未上传" : "内容已读取 · 尚未上传";
        }

        internal void PasteShare()
        {
            if (busy) return;
            ShowBeside(bubble.CurrentWindow);
            try
            {
                string text = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
                if (text.Length == 0 || text.Length > 6000) throw new InvalidOperationException("请先复制分享链接或消息文字（最多 6000 字）。微信卡片若不能复制链接，目前不能直接导入。");
                string target = recipient.Text;
                ResetMaterial(null, bubble.CurrentWindow, "后台自动识别文章或视频链接；不会启动录屏。", text);
                recipient.Text = target; linkInput = true; source.Visible = true;
                status.Text = "已粘贴 · 点击生成后读取正文与可用评论";
            }
            catch (Exception error) { status.Text = error.Message; }
        }

        internal async Task SelectVideoCard()
        {
            if (busy) return;
            var window = bubble.CurrentWindow;
            if (window == null) { status.Text = "请先打开含视频卡片的微信聊天，不用播放视频。"; ShowBeside(null); return; }
            string[] hints = null;
            SetBusy(true);
            try
            {
                Hide(); bubble.Suspend(true); NativeMethods.SetForegroundWindow(window.Handle); await Task.Delay(220);
                if (!WeChatCapture.IsUnchangedAndForeground(window)) throw new InvalidOperationException("微信窗口已变化，请重新选取。");
                byte[] image;
                using (var picker = new ScreenSelection(window.Bounds, false, "框住一张视频卡片（标题和作者）· 不用播放 · Esc 取消"))
                {
                    if (picker.ShowDialog() != DialogResult.OK) { status.Text = "已取消选取，原内容保留。"; return; }
                    image = picker.SelectedImage();
                }
                hints = CardLookup.Queries(await LocalOcr.Read(image));
                if (hints.Length == 0) throw new InvalidOperationException("没识别到标题或作者。请框住卡片上的文字，或粘贴原分享链接。");
            }
            catch (Exception error) { status.Text = error.Message; hints = null; }
            finally { SetBusy(false); bubble.Suspend(false); ShowBeside(window); }
            if (hints != null) SearchVideo(hints, window);
        }

        internal void SearchVideo(string[] hints = null, WeChatWindow origin = null)
        {
            if (busy) return;
            var window = origin ?? bubble.CurrentWindow;
            ShowBeside(window);
            using (var dialog = new VideoSearchForm(api, service.Text.Trim(), hints))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var video = dialog.Selection;
                ResetMaterial(null, window, "已选：" + video.author + " · " + video.duration + " 秒", video.title);
                selectedVideo = video; linkInput = true; source.Visible = true;
                status.Text = "视频已选好 · 生成后后台读取讲述与评论";
            }
        }

        internal void ReadCurrentShare()
        {
            if (busy) return;
            try
            {
                var window = bubble.CurrentWindow;
                string link = WeChatCapture.ReadShareLink(window);
                if (link.Length == 0) throw new InvalidOperationException("请先点开微信里的视频或文章卡片一次，再点读取分享。不必播放完；也可右键悬浮球粘贴链接。");
                string target = recipient.Text;
                ResetMaterial(null, window, "已取得当前分享链接 · 生成后后台读取正文与可用评论", link);
                recipient.Text = target; linkInput = true; source.Visible = false;
                status.Text = "分享链接已就绪 · 没有录屏，尚未提交分析";
            }
            catch (Exception error) { status.Text = error.Message; }
        }

        private void ShowReadEvidence()
        {
            using (var dialog = new Form { Text = "实际读取的依据", Size = new Size(720, 620), StartPosition = FormStartPosition.CenterParent, TopMost = true })
            {
                dialog.Controls.Add(new TextBox { Text = coverage.Text + "\r\n\r\n" + readEvidence, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = Theme.Font(11, FontStyle.Regular) });
                dialog.ShowDialog(this);
            }
        }

        internal void ShowBeside(WeChatWindow window)
        {
            var area = window == null ? Screen.PrimaryScreen.WorkingArea : Screen.FromRectangle(window.Bounds).WorkingArea;
            Height = Math.Min(Height, area.Height - 20);
            int x = area.Right - Width - 16;
            int y = area.Top + 20;
            if (window != null)
            {
                int right = window.Bounds.Right + 10;
                int left = window.Bounds.Left - Width - 10;
                if (right + Width <= area.Right) x = right;
                else if (left >= area.Left) x = left;
                else x = Math.Max(area.Left, Math.Min(area.Right - Width, window.Bounds.Right - Width - 16));
                y = Math.Min(area.Bottom - Height, Math.Max(area.Top, window.Bounds.Top));
            }
            Location = new Point(x, y);
            Show();
            Activate();
        }

        internal async Task CaptureCurrentWindow(bool add)
        {
            if (busy) return;
            var window = bubble.CurrentWindow;
            if (window == null) { status.Text = "先打开电脑版微信，再点读取。"; ShowBeside(null); return; }
            string previous = source.Text;
            SetBusy(true);
            try
            {
                Hide(); bubble.Suspend(true); NativeMethods.SetForegroundWindow(window.Handle); await Task.Delay(250);
                if (!WeChatCapture.IsUnchangedAndForeground(window)) throw new InvalidOperationException("窗口已变化，请重新点击悬浮球读取。");
                capture = WeChatCapture.Read(window);
                string target = capture.Recipient;
                if (!window.IsWebContent && String.IsNullOrWhiteSpace(target))
                {
                    // 自绘聊天标题也使用本机 OCR；不读取微信数据库。
                    var b = window.Bounds;
                    var header = Rectangle.FromLTRB(b.Left + (int)(b.Width * .33), b.Top + (int)(b.Height * .065), b.Left + (int)(b.Width * .70), b.Top + (int)(b.Height * .145));
                    try { target = (await LocalOcr.Read(WeChatCapture.CaptureArea(header))).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? ""; } catch { }
                }
                bubble.Suspend(false); ShowBeside(window);
                status.Text = "正在本机识别文字，不上传…";
                string recognized = capture.Text;
                string ocrError = "";
                try { recognized = WeChatCapture.NormalizeLines(new[] { recognized }.Concat((await LocalOcr.Read(capture.Image)).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)), 6000); }
                catch (Exception error) { ocrError = error.Message; }
                if (add && attachment != null)
                {
                    if (String.IsNullOrWhiteSpace(recognized)) throw new InvalidOperationException("本页未识别到文字，已保留原内容。请框选评论区域重试。");
                    string combined = previous + "\r\n【补充的当前可见正文 / 评论，未核实观点】\r\n" + recognized;
                    if (combined.Length > 6000) throw new InvalidOperationException("累计文字超过 6000 字，请先删减原文再追加；已有内容已保留。");
                    source.Text = combined; reply.Clear(); another.Enabled = false;
                    summary.Text = "已追加本页可见文字，生成时结合前面的素材分析。";
                }
                else
                {
                    ResetMaterial(new AttachmentData { Kind = "image", Name = capture.FileName, Mime = capture.Mime, Bytes = capture.Image }, window,
                        window.IsWebContent ? "微信文章 / 视频号可见页 · 可追加评论；未读取完整音轨。" : "真实微信当前聊天 · 仅本次可见内容；未读取后台记录。", recognized);
                    if (!String.IsNullOrWhiteSpace(target)) recipient.Text = target.Substring(0, Math.Min(40, target.Length));
                    demoCapture = bubble.IsDemo;
                }
                source.Visible = true;
                status.Text = recognized.Length > 0 ? "已识别 " + recognized.Length + " 字 · 本机处理，尚未上传" : "文字未识别，画面已保留；可生成图像分析。";
                if (ocrError.Length > 0) status.Text = "本机 OCR 不可用；画面已保留，可生成图像分析。";
            }
            catch (Exception e) { status.Text = e.Message; }
            finally { SetBusy(false); bubble.Suspend(false); ShowBeside(window); }
        }

        internal async Task SelectContent(bool video)
        {
            if (busy) return;
            var window = bubble.CurrentWindow;
            if (window == null) { status.Text = "先在微信打开消息或播放视频，再来框选。"; ShowBeside(null); return; }
            var frames = new List<Bitmap>();
            SetBusy(true);
            try
            {
                Hide(); bubble.Suspend(true); NativeMethods.SetForegroundWindow(window.Handle); await Task.Delay(220);
                if (!WeChatCapture.IsUnchangedAndForeground(window)) throw new InvalidOperationException("当前微信窗口已变化，请重试。");
                Rectangle region; byte[] image;
                using (var picker = new ScreenSelection(window.Bounds, video))
                {
                    if (picker.ShowDialog() != DialogResult.OK) { status.Text = "已取消框选，保留原来的内容。"; return; }
                    region = picker.SelectedArea; image = picker.SelectedImage();
                }
                if (video)
                {
                    NativeMethods.SetForegroundWindow(window.Handle); await Task.Delay(180);
                    var times = new List<double>(); var watch = Stopwatch.StartNew();
                    using (var progress = new CaptureProgress(region))
                    {
                        progress.Report(0); progress.Show();
                        NativeMethods.SetForegroundWindow(window.Handle); await Task.Delay(150);
                        for (int i = 0; i < 6; i++)
                        {
                            if (i > 0) { for (int tick = 0; tick < 20; tick++) { await Task.Delay(100); if (progress.Cancelled) throw new OperationCanceledException(); } }
                            if (progress.Cancelled) throw new OperationCanceledException();
                            string issue = WeChatCapture.CaptureIssue(window);
                            if (issue.Length > 0) throw new InvalidOperationException("已停止：" + issue + "（" + i + "/6 帧）。请重新框选。");
                            frames.Add(WeChatCapture.Snapshot(region)); times.Add(watch.Elapsed.TotalSeconds); progress.Report(i + 1);
                        }
                        image = FrameSheet.Compose(frames, times);
                    }
                }
                string note = video ? "约 10 秒的 6 帧画面 · 没有声音，只依据可见字幕与画面，不能当作完整视频。" : "手动框选的可见内容 · 点击图片放大核对，生成时识别文字。";
                ResetMaterial(new AttachmentData { Kind = "image", Name = video ? "wechat-visible-frames-no-audio.jpg" : "wechat-selected-region.jpg", Mime = "image/jpeg", Bytes = image }, window, note, "");
                demoCapture = bubble.IsDemo;
                capture = null;
                bubble.Suspend(false); ShowBeside(window); status.Text = "正在本机识别选区文字…";
                try { source.Text = await LocalOcr.Read(image); source.Visible = true; status.Text = "已识别 " + source.Text.Length + " 字 · 尚未上传"; }
                catch { status.Text = "本机 OCR 不可用；选区已保留，可生成图像分析。"; }
            }
            catch (OperationCanceledException) { status.Text = "已取消采样，未上传画面。"; }
            catch (Exception e) { status.Text = e.Message; }
            finally { foreach (var frame in frames) frame.Dispose(); SetBusy(false); bubble.Suspend(false); ShowBeside(window); }
        }

        private void ImportMedia(object sender, EventArgs args)
        {
            if (busy) return;
            using (var dialog = new OpenFileDialog { Filter = "微信视频或截图|*.mp4;*.webm;*.mov;*.jpg;*.jpeg;*.png;*.webp", Multiselect = false, CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var imported = ApiClient.FromFile(dialog.FileName);
                    ResetMaterial(imported, bubble.CurrentWindow, imported.Kind == "video" ? "完整视频文件 · 长视频自动分段，分析画面和语音。" : "所选图片 · 分析画面和文字。", "");
                    capture = null;
                    status.Text = "已选择“" + attachment.Name + "”，点击生成后才会上传分析。";
                    summary.Text = attachment.Kind == "video" ? "将分段分析完整视频的画面和语音。" : "将分析图片里的画面和文字。";
                    variant = 0;
                }
                catch (Exception error) { status.Text = error.Message; }
            }
        }

        internal void AcceptMediaDrop(object sender, DragEventArgs args)
        {
            args.Effect = !busy && DropInput.Accepts(args.Data) ? (args.AllowedEffect & DragDropEffects.Copy) : DragDropEffects.None;
            if (!busy && args.Effect == DragDropEffects.None) status.Text = "此卡片没有可拖入的文件或原链接，请点「选取微信视频」。";
        }

        internal void ReceiveMediaDrop(object sender, DragEventArgs args)
        {
            if (busy) return;
            try
            {
                if (!args.Data.GetDataPresent(DataFormats.FileDrop)) {
                    var text = DropInput.Text(args.Data);
                    ResetMaterial(null, bubble.CurrentWindow, "已接收拖入内容；生成后按链接读取正文、视频和评论。", text);
                    linkInput = true; ShowBeside(bubble.CurrentWindow);
                    return;
                }
                var paths = args.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null || paths.Length != 1) throw new InvalidOperationException("请一次拖入一个微信视频或图片文件。");
                var imported = ApiClient.FromFile(paths[0]);
                ResetMaterial(imported, bubble.CurrentWindow, imported.Kind == "video" ? "完整视频文件 · 长视频自动分段，分析画面和语音。" : "所选图片 · 分析画面和文字。", "");
                capture = null;
                status.Text = "已接收“" + attachment.Name + "”；点击生成后才会上传分析。";
                summary.Text = attachment.Kind == "video" ? "将分段分析完整视频的画面和语音。" : "将分析图片里的画面和文字。";
                variant = 0;
            }
            catch (Exception error) { status.Text = error.Message; }
        }

        private async Task GenerateReply()
        {
            if (busy) { if (requestCancellation != null) requestCancellation.Cancel(); return; }
            if (String.IsNullOrWhiteSpace(source.Text) && attachment == null && selectedVideo == null)
            {
                status.Text = "请先搜索并选择视频，或粘贴分享链接 / 消息。";
                return;
            }
            string target = recipient.Text.Trim();
            string extra = context.Text.Trim();
            string combined = (target.Length > 0 ? "回复给“" + target + "”。" : "") + extra;
            if (combined.Length > 200) combined = combined.Substring(0, 200);
            requestCancellation = new CancellationTokenSource();
            SetBusy(true);
            ReplyResult completed = null;
            status.Text = selectedVideo != null ? "正在后台读取视频与评论…" : attachment != null && attachment.Kind == "video" ? "正在分析视频画面和语音…" : "正在分析当前内容…";
            try
            {
                int nextVariant = String.IsNullOrWhiteSpace(reply.Text) ? variant : (variant + 1) % 3;
                var result = await api.Generate(service.Text.Trim(), source.Text.Trim(), combined, nextVariant, attachment, selectedVideo, requestCancellation.Token);
                variant = nextVariant;
                reply.Text = result.Reply;
                summary.Text = result.Summary;
                if (String.IsNullOrWhiteSpace(source.Text) && !String.IsNullOrWhiteSpace(result.Evidence)) source.Text = result.Evidence;
                readEvidence = result.Evidence;
                coverage.Text = result.Coverage.Length > 0 ? result.Coverage : captureNote;
                if (linkInput) source.Visible = false;
                another.Enabled = true;
                status.Text = "回复已生成，可直接修改；发送前由你确认。";
                completed = result;
                try { memory.Save(target, extra); if (target.Length > 0 && !recipient.Items.Contains(target)) recipient.Items.Add(target); } catch { status.Text += " 本次对象备注未能保存。"; }
            }
            catch (OperationCanceledException) { status.Text = requestCancellation.IsCancellationRequested ? "已停止生成，内容已保留。" : "服务响应超时，内容已保留，可重试。"; }
            catch (Exception error) { status.Text = error.Message; }
            finally { requestCancellation.Dispose(); requestCancellation = null; SetBusy(false); }
            if (completed != null && demoCapture) DemoWindow.SaveReport(this, completed, attachment, captureNote);
        }

        private void CopyAndReturn(object sender, EventArgs args)
        {
            if (busy || String.IsNullOrWhiteSpace(reply.Text)) { status.Text = "先生成或写好一句回复。"; return; }
            try
            {
                Clipboard.SetText(reply.Text);
                status.Text = "已复制，回到微信后粘贴并确认发送。";
                Hide();
                var window = originWindow ?? bubble.CurrentWindow;
                if (window != null) NativeMethods.SetForegroundWindow(window.Handle);
            }
            catch { status.Text = "复制失败，请选中回复后按 Ctrl+C。"; }
        }

        private void SetBusy(bool value)
        {
            busy = value;
            append.Enabled = paste.Enabled = read.Enabled = select.Enabled = clip.Enabled = import.Enabled = generate.Enabled = recipient.Enabled = source.Enabled = context.Enabled = !value;
            reply.ReadOnly = value;
            another.Enabled = copy.Enabled = !value && !String.IsNullOrWhiteSpace(reply.Text);
            generate.Enabled = !value || requestCancellation != null;
            generate.Text = value ? (requestCancellation == null ? "正在读取…" : "停止生成") : "生成回复";
        }

        private static Label SectionLabel(string text, int y)
        {
            return new Label { Text = text, AutoSize = true, Location = new Point(18, y), Font = Theme.Font(9, FontStyle.Bold), ForeColor = Theme.Ink };
        }

        private static Button FlatButton(string text, int width, int height, bool primary)
        {
            var button = new Button { Text = text, Width = width, Height = height, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Font = Theme.Font(8, FontStyle.Regular) };
            button.FlatAppearance.BorderColor = primary ? Theme.Green : Theme.Line;
            button.BackColor = primary ? Theme.Green : Theme.Paper;
            button.ForeColor = primary ? Color.White : Theme.Green;
            return button;
        }

        private static void StyleButton(Button button, string text, bool primary)
        {
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = primary ? Theme.Green : Theme.Line;
            button.BackColor = primary ? Theme.Green : Theme.Paper;
            button.ForeColor = primary ? Color.White : Theme.Green;
            button.Font = Theme.Font(9, primary ? FontStyle.Bold : FontStyle.Regular);
            button.Cursor = Cursors.Hand;
        }

        private static string ConfigPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zhiyi", "desktop-service.txt"); }
        }

        private static string LoadServiceUrl()
        {
            try { return File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath).Trim() : "http://127.0.0.1:4173"; }
            catch { return "http://127.0.0.1:4173"; }
        }

        private void SaveServiceUrl()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                File.WriteAllText(ConfigPath, service.Text.Trim());
            }
            catch { status.Text = "服务地址没有保存，但本次仍可继续使用。"; }
        }

        internal void PreparePreview()
        {
            recipient.Text = "妈妈";
            context.Text = "关系亲近，简短自然。";
            source.Text = "你最近是不是又忙到很晚？天凉了，多穿点，也要记得吃饭。";
            source.Visible = false;
            status.Text = "界面示例 · 合成文字，未调用模型";
            summary.Text = "这是一条关心近况、提醒保暖和吃饭的消息。";
            reply.Text = "你那边也降温了吧，出门多穿件，别嫌麻烦。";
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Contains("--self-test")) return SelfTest();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--preview") return Preview(args[1]);
            if (args.Contains("--demo"))
            {
                DemoWindow.ReportDirectory = args.Length == 2 ? Path.GetFullPath(args[1]) : null;
                using (var demo = new DemoWindow())
                using (var bubble = new BubbleForm(demo))
                {
                    demo.Show();
                    demo.FormClosed += delegate { bubble.Close(); };
                    bubble.Show();
                    bubble.Assistant.ShowBeside(bubble.CurrentWindow);
                    Application.Run(bubble);
                }
                return 0;
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\ZhiyiDesktopCompanion", out created))
            {
                if (!created) return 0;
                Application.Run(new BubbleForm());
            }
            return 0;
        }

        private static int SelfTest()
        {
            try
            {
                ApiClient.SelfTest();
                DropInput.SelfTest();
                CardLookup.SelfTest();
                string memoryFile = Path.Combine(Path.GetTempPath(), "zhiyi-memory-" + Guid.NewGuid().ToString("N"), "notes.json");
                try {
                    var memory = new RecipientMemory(memoryFile); memory.Save("妈妈", "关系亲近，简短自然");
                    if (new RecipientMemory(memoryFile).Notes["妈妈"] != "关系亲近，简短自然") throw new Exception("对象备注恢复失败");
                } finally { if (File.Exists(memoryFile)) File.Delete(memoryFile); if (Directory.Exists(Path.GetDirectoryName(memoryFile))) Directory.Delete(Path.GetDirectoryName(memoryFile)); }
                string normalized = WeChatCapture.NormalizeLines(new[] { " 你好  世界 ", "你好 世界", "第二句" }, 20);
                if (normalized != "你好 世界\r\n第二句") throw new Exception("文字去重失败");
                if (WeChatCapture.NormalizeLines(new[] { "Weixin", "MMUIRenderSubWindowHW", "这是一条消息" }, 6000) != "这是一条消息") throw new Exception("微信自绘控件噪声过滤失败");
                if (!WeChatCapture.IsShareLink("https://channels.weixin.qq.com/web/pages/feed?oid=test") || WeChatCapture.IsShareLink("https://channels.weixin.qq.com.evil.example/web/pages/feed") || WeChatCapture.IsShareLink("https://user@channels.weixin.qq.com/web/pages/feed")) throw new Exception("分享链接来源验证失败");
                var bounds = new Rectangle(100, 100, 1000, 700);
                var candidates = new List<TextCandidate> {
                    new TextCandidate { Text = "搜索", Bounds = new Rectangle(150, 130, 80, 30) },
                    new TextCandidate { Text = "妈妈", Selected = true, Bounds = new Rectangle(200, 240, 150, 50) },
                    new TextCandidate { Text = "错误标题", Bounds = new Rectangle(700, 140, 120, 30) }
                };
                if (WeChatCapture.PickRecipient(candidates, bounds) != "妈妈") throw new Exception("当前聊天识别失败");
                if (WeChatCapture.PickRecipient(new[] { candidates[0], candidates[2] }, bounds) != "错误标题") throw new Exception("标题回退失败");
                if (ScreenSelection.NormalizeSelection(new Point(90, 80), new Point(-10, 15), new Rectangle(0, 0, 100, 100)) != new Rectangle(0, 15, 90, 65)) throw new Exception("反向框选或边界裁剪失败");
                using (var first = new Bitmap(100, 60))
                using (var second = new Bitmap(100, 60))
                {
                    using (var g = Graphics.FromImage(first)) g.Clear(Color.Red);
                    using (var g = Graphics.FromImage(second)) g.Clear(Color.Lime);
                    byte[] sheet = FrameSheet.Compose(new[] { first, second }, new[] { 0.0, 2.0 });
                    using (var stream = new MemoryStream(sheet))
                    using (var image = new Bitmap(stream))
                    {
                        if (image.Width != 1260 || image.Height != 394 || image.GetPixel(315, 214).R < 200 || image.GetPixel(945, 214).G < 200) throw new Exception("多帧图像缺帧或顺序错误");
                    }
                    bool rejected = false;
                    try { FrameSheet.Compose(new[] { first }, new double[0]); } catch (ArgumentException) { rejected = true; }
                    if (!rejected) throw new Exception("未拒绝不完整采样时间");
                }
                Console.WriteLine("Windows client self-test passed: text filtering, recipient, selection clipping, frame order and bounds.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        }

        private static int Preview(string path)
        {
            using (var bubble = new BubbleForm())
            {
                bubble.Hide();
                var form = new AssistantForm(bubble);
                form.PreparePreview();
                form.Show();
                Application.DoEvents();
                using (var image = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                    image.Save(path, ImageFormat.Png);
                }
                form.Dispose();
            }
            return 0;
        }
    }
}
