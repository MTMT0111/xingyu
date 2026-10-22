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
        internal static Rectangle FitWindow(Rectangle bounds, Rectangle area)
        {
            int width = Math.Min(bounds.Width, area.Width), height = Math.Min(bounds.Height, area.Height);
            return new Rectangle(Math.Max(area.Left, Math.Min(bounds.Left, area.Right - width)), Math.Max(area.Top, Math.Min(bounds.Top, area.Bottom - height)), width, height);
        }
        internal static void FitToScreen(Form form, Rectangle area)
        {
            var bounds = FitWindow(form.Bounds, area);
            form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, bounds.Width), Math.Min(form.MinimumSize.Height, bounds.Height));
            form.Bounds = bounds;
        }
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

        internal BubbleForm(Form demonstration = null, EventWaitHandle activation = null)
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
            menu.Items.Add("从微信接入原视频", null, delegate { assistant.SearchVideo(null, null, VideoListMode.Recent); });
            menu.Items.Add("框选卡片查找（备用）", null, async delegate { await assistant.SelectVideoCard(); });
            menu.Items.Add("搜索视频号", null, delegate { assistant.SearchVideo(); });
            menu.Items.Add("读取已打开的分享链接", null, delegate { assistant.ReadCurrentShare(); });
            menu.Items.Add("框选一条文字消息", null, async delegate { await assistant.SelectContent(false); });
            menu.Items.Add("读取当前窗口", null, async delegate { await assistant.CaptureCurrentWindow(false); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("查看回复 / 导入视频", null, delegate { assistant.ShowBeside(currentWindow); });
            menu.Items.Add("恢复跟随微信", null, delegate { pinned = false; AttachToWeChat(); });
            menu.Items.Add("退出", null, delegate { Close(); });
            ContextMenuStrip = menu;
            tip.SetToolTip(this, "Ctrl+Alt+R 唤起或收起 · Ctrl+Alt+V 接入原视频");
            Paint += delegate(object sender, PaintEventArgs e) {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var bg = new SolidBrush(currentWindow == null ? Theme.Muted : Theme.Green)) e.Graphics.FillEllipse(bg, 0, 0, 57, 57);
                using (var pen = new Pen(Color.FromArgb(180, 255, 255, 255), 2)) e.Graphics.DrawEllipse(pen, 2, 2, 53, 53);
                using (var font = Theme.Font(17, FontStyle.Bold))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    e.Graphics.DrawString("回", font, Brushes.White, ClientRectangle, format);
            };
            MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button != MouseButtons.Left) return; moving = true; dragged = false; mouseOrigin = Cursor.Position; startLocation = Location; Capture = true; };
            MouseMove += delegate { if (!moving) return; var delta = new Size(Cursor.Position.X - mouseOrigin.X, Cursor.Position.Y - mouseOrigin.Y); if (Math.Abs(delta.Width) + Math.Abs(delta.Height) > 5) { dragged = true; pinned = true; Location = Point.Add(startLocation, delta); } };
            MouseUp += delegate(object sender, MouseEventArgs e) { if (!moving) return; moving = false; Capture = false; if (!dragged && e.Button == MouseButtons.Left) assistant.ShowBeside(currentWindow); };
            timer.Interval = 1000;
            timer.Tick += delegate {
                AttachToWeChat();
                if (activation != null && activation.WaitOne(0)) assistant.ShowBeside(currentWindow);
            };
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
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 2) { BeginInvoke(new Action(delegate { assistant.SearchVideo(null, null, VideoListMode.Recent); })); return; }
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
        private ReplyResult currentResult;
        private readonly LinkLabel details = new LinkLabel();
        private readonly Button related = new Button();
        private readonly TableLayoutPanel recommendations = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, Visible = false };
        private CancellationTokenSource relatedCancellation;
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
        private readonly Panel scroll = new Panel();
        private readonly Button generate = new Button();
        private readonly Button copy = new Button();
        private readonly Button read = new Button();
        private readonly Button import = new Button();
        private CaptureResult capture;
        private AttachmentData attachment;
        private int variant;
        private bool busy;
        private bool draftChanged;

        internal AssistantForm(BubbleForm owner)
        {
            bubble = owner;
            Text = "知意 · 微信旁边的回复助手";
            ClientSize = new Size(500, 710);
            MinimumSize = new Size(460, 580);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            BackColor = Theme.Canvas;
            Font = Theme.Font(10.5f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.None;
            AllowDrop = true;
            DragEnter += AcceptMediaDrop;
            DragDrop += ReceiveMediaDrop;
            BuildUi();
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) Scale(new SizeF(graphics.DpiX / 96f, graphics.DpiY / 96f));
        }

        private void BuildUi()
        {
            var header = new Panel { Dock = DockStyle.Top, Width = ClientSize.Width, Height = 86, BackColor = Theme.Paper };
            header.Controls.Add(new Label { Text = "知意", Bounds = new Rectangle(28, 18, 160, 30), Font = Theme.Font(17, FontStyle.Bold), ForeColor = Theme.Ink });
            header.Controls.Add(new Label { Text = bubble.IsDemo ? "演示模式 · 合成聊天素材" : "在当前对话旁，把话回好", Bounds = new Rectangle(29, 51, 280, 20), Font = Theme.Font(9, FontStyle.Regular), ForeColor = Theme.Muted });
            var settingsButton = FlatButton("服务", 54, 32, false);
            settingsButton.Location = new Point(ClientSize.Width - 82, 26); settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            settingsButton.Click += delegate { settings.Visible = !settings.Visible; };
            var hide = FlatButton("收起", 54, 32, false);
            hide.Location = new Point(ClientSize.Width - 146, 26); hide.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            hide.Click += delegate { Hide(); };
            header.Controls.AddRange(new Control[] { hide, settingsButton });
            settings.Width = ClientSize.Width; settings.Dock = DockStyle.Top; settings.Height = 116; settings.Visible = false; settings.BackColor = Theme.PaleGreen;
            settings.Controls.Add(new Label { Text = "分析服务（密钥保存在服务端）", Bounds = new Rectangle(22, 7, 400, 22), ForeColor = Theme.Muted });
            service.Text = LoadServiceUrl(); service.SetBounds(22, 32, 436, 28); service.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            service.Leave += delegate { SaveServiceUrl(); }; settings.Controls.Add(service);
            var modelSettings = FlatButton("DeepSeek 密钥 · 配置 / 更换", 274, 32, false);
            modelSettings.Location = new Point(22, 73);
            modelSettings.Click += delegate { using (var dialog = new ModelSettingsForm(api, service.Text.Trim())) { dialog.ShowDialog(this); if (!String.IsNullOrWhiteSpace(dialog.LastStatus)) status.Text = dialog.LastStatus; } };
            settings.Controls.Add(modelSettings);

            var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 82, Padding = new Padding(28, 16, 28, 16), ColumnCount = 2, BackColor = Theme.Paper };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            StyleButton(generate, "生成回复", true); generate.Dock = DockStyle.Fill; generate.Margin = new Padding(0, 0, 8, 0);
            generate.Click += async delegate { await GenerateReply(); };
            StyleButton(copy, "复制 · 回到微信", false); copy.Dock = DockStyle.Fill; copy.Margin = Padding.Empty; copy.Enabled = false;
            copy.Click += CopyAndReturn; footer.Controls.Add(generate, 0, 0); footer.Controls.Add(copy, 1, 0);

            scroll.Dock = DockStyle.Fill; scroll.AutoScroll = true; scroll.Padding = new Padding(28, 24, 28, 24);
            var body = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = Padding.Empty, Margin = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.AllowDrop = true; body.DragEnter += AcceptMediaDrop; body.DragDrop += ReceiveMediaDrop;
            body.Controls.Add(new Label { Text = "分享内容", AutoSize = true, Font = Theme.Font(11, FontStyle.Bold), ForeColor = Theme.Ink, Margin = new Padding(0, 0, 0, 12) });
            var actions = new TableLayoutPanel { Height = 42, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 0, 0, 12) };
            for (int i = 0; i < 2; i++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            StyleButton(paste, "从微信接入", false); paste.Click += delegate { SearchVideo(null, null, VideoListMode.Recent); };
            StyleButton(read, "粘贴链接 / 消息", false); read.Click += delegate { PasteShare(); };
            var actionButtons = new[] { paste, read }; for (int i = 0; i < 2; i++) { actionButtons[i].Dock = DockStyle.Fill; actionButtons[i].Margin = new Padding(i == 0 ? 0 : 7, 0, 0, 0); actions.Controls.Add(actionButtons[i], i, 0); }
            body.Controls.Add(actions);
            var drop = new Label { Text = "先在微信点开卡片一次，再接入原视频\n也可拖入视频文件或分享链接", Font = Theme.Font(9, FontStyle.Regular), Dock = DockStyle.Top, Height = 60, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted, BackColor = Theme.PaleGreen, AllowDrop = true, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 0, 16) };
            drop.Click += delegate { SearchVideo(null, null, VideoListMode.Recent); };
            drop.DragEnter += AcceptMediaDrop; drop.DragDrop += ReceiveMediaDrop; body.Controls.Add(drop);
            source.TextChanged += delegate { drop.Visible = String.IsNullOrWhiteSpace(source.Text) && attachment == null; MarkDraftChanged(); };
            status.Text = "接入原视频，或粘贴消息 / 链接。"; status.ForeColor = Theme.Green; status.Font = Theme.Font(10, FontStyle.Regular); status.AutoSize = true; status.Dock = DockStyle.Top; status.Margin = new Padding(0, 0, 0, 10); body.Controls.Add(status);
            preview.Height = 76; preview.Dock = DockStyle.Top; preview.BackColor = Theme.PaleGreen; preview.SizeMode = PictureBoxSizeMode.Zoom; preview.Cursor = Cursors.Hand; preview.Margin = new Padding(0, 0, 0, 6);
            preview.Paint += delegate(object sender, PaintEventArgs e) { if (preview.Image == null) using (var font = Theme.Font(10, FontStyle.Regular)) TextRenderer.DrawText(e.Graphics, "链接读取正文 · 视频文件分析语音\n也可框选消息或拖入媒体文件", font, preview.ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); };
            preview.Click += delegate { ShowEvidenceImage(); }; preview.AllowDrop = true; preview.DragEnter += AcceptMediaDrop; preview.DragDrop += ReceiveMediaDrop;
            preview.Visible = false; body.Controls.Add(preview);
            coverage.Text = "点击生成后，读取内容并整理回复。"; coverage.ForeColor = Theme.Muted; coverage.Font = Theme.Font(9, FontStyle.Regular); coverage.AutoSize = true; coverage.Dock = DockStyle.Top; coverage.Margin = new Padding(0, 0, 0, 12); body.Controls.Add(coverage);
            var tools = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 22) };
            details.Text = "查看依据"; details.AutoSize = true; details.LinkColor = Theme.Green; details.Margin = new Padding(0, 3, 18, 0);
            details.Click += delegate { if (currentResult != null) ShowReadEvidence(); else source.Visible = !source.Visible; }; tools.Controls.Add(details);
            details.LinkBehavior = LinkBehavior.HoverUnderline;
            var extraTools = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top, Visible = false, Margin = new Padding(0, 0, 0, 16) };
            var moreTools = new LinkLabel { Text = "导入与搜索", AutoSize = true, LinkColor = Theme.Green, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 3, 0, 0) };
            moreTools.Click += delegate { extraTools.Visible = !extraTools.Visible; moreTools.Text = extraTools.Visible ? "收起其他方式" : "导入与搜索"; }; tools.Controls.Add(moreTools);
            StyleButton(import, "导入视频", false); import.Width = 90; import.Height = 32; import.Font = Theme.Font(9, FontStyle.Regular); import.Click += ImportMedia; extraTools.Controls.Add(import);
            StyleButton(append, "搜索视频号", false); append.Width = 112; append.Height = 28; append.Font = Theme.Font(9, FontStyle.Regular);
            append.Click += delegate { SearchVideo(); }; extraTools.Controls.Add(append); body.Controls.Add(tools); body.Controls.Add(extraTools);
            var refresh = new LinkLabel { Text = "刷新依据", AutoSize = true, LinkColor = Theme.Green, Margin = new Padding(10, 3, 0, 0) };
            refresh.Click += async delegate { if (!busy) await GenerateReply(true); }; extraTools.Controls.Add(refresh);
            source.Multiline = true; source.ScrollBars = ScrollBars.Vertical; source.MaxLength = 6000; source.Height = 68; source.Dock = DockStyle.Top; source.Visible = false; source.Margin = new Padding(0, 0, 0, 12); body.Controls.Add(source);
            var targetRow = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 22) };
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            targetRow.Controls.Add(new Label { Text = "回复给谁", AutoSize = true, Font = Theme.Font(11, FontStyle.Bold), ForeColor = Theme.Ink, Margin = new Padding(0, 0, 0, 10) }, 0, 0);
            var noteToggle = new LinkLabel { Text = "关系备注（可选）", AutoSize = true, LinkColor = Theme.Green, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(12, 2, 0, 10) };
            targetRow.Controls.Add(noteToggle, 1, 0);
            noteToggle.Click += delegate { context.Visible = !context.Visible; noteToggle.Text = context.Visible ? "收起备注" : String.IsNullOrWhiteSpace(context.Text) ? "关系备注（可选）" : "已有关系备注 · 展开"; };
            foreach (var name in memory.Notes.Keys) recipient.Items.Add(name);
            recipient.TextChanged += delegate { string note; context.Text = memory.Notes.TryGetValue(recipient.Text, out note) ? note : ""; MarkDraftChanged(); };
            recipient.Dock = DockStyle.Top; recipient.MaxLength = 40; recipient.Margin = Padding.Empty;
            context.Dock = DockStyle.Top; context.Height = 62; context.Multiline = true; context.ScrollBars = ScrollBars.Vertical; context.MaxLength = 100; context.Margin = new Padding(0, 12, 0, 0); context.Visible = false;
            context.TextChanged += delegate { if (!context.Visible) noteToggle.Text = String.IsNullOrWhiteSpace(context.Text) ? "关系备注（可选）" : "已有关系备注 · 展开"; MarkDraftChanged(); };
            targetRow.Controls.Add(recipient, 0, 1); targetRow.SetColumnSpan(recipient, 2); targetRow.Controls.Add(context, 0, 2); targetRow.SetColumnSpan(context, 2); body.Controls.Add(targetRow);
            summary.Text = "读懂内容，再给你一句自然的回复。"; summary.ForeColor = Theme.Ink; summary.Font = Theme.Font(10, FontStyle.Regular); summary.Height = 64; summary.Dock = DockStyle.Top; summary.Margin = new Padding(0, 8, 0, 8); body.Controls.Add(summary);
            summary.AutoSize = true;
            body.SizeChanged += delegate { foreach (var label in new[] { summary, status, coverage }) label.MaximumSize = new Size(Math.Max(200, body.ClientSize.Width), 0); };
            var resultHeader = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0, 12, 0, 12) };
            resultHeader.Controls.Add(new Label { Text = "回复草稿", AutoSize = true, Font = Theme.Font(11, FontStyle.Bold), Margin = new Padding(0, 4, 20, 0) });
            StyleButton(another, "换一句", false); another.AutoSize = false; another.Size = new Size(94, 34); another.Font = Theme.Font(9, FontStyle.Regular); another.Enabled = false; another.Click += async delegate { await GenerateReply(); }; resultHeader.Controls.Add(another); body.Controls.Add(resultHeader);
            StyleButton(related, "找相关视频", false); related.Size = new Size(112, 34); related.Enabled = false;
            related.Click += delegate { if (currentResult != null) SearchVideo(currentResult.RelatedQueries, null, VideoListMode.Related); }; resultHeader.Controls.Add(related);
            reply.Multiline = true; reply.ScrollBars = ScrollBars.Vertical; reply.MaxLength = 1000; reply.Height = 112; reply.Dock = DockStyle.Fill; reply.Font = Theme.Font(11, FontStyle.Regular); reply.BackColor = Theme.Paper; reply.BorderStyle = BorderStyle.None; reply.Margin = Padding.Empty; reply.TextChanged += delegate { copy.Enabled = !busy && !String.IsNullOrWhiteSpace(reply.Text); };
            var replyPaper = new Panel { Dock = DockStyle.Top, Height = 140, BackColor = Theme.Paper, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 12) }; replyPaper.Controls.Add(reply); body.Controls.Add(replyPaper);
            body.Controls.Add(new Label { Text = "可直接编辑。复制后回到原窗口，由你粘贴、确认发送。", AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Font(9, FontStyle.Regular), Margin = new Padding(0, 0, 0, 8) });
            recommendations.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.Controls.Add(recommendations);
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
            if (disposing) { if (relatedCancellation != null) relatedCancellation.Cancel(); if (requestCancellation != null) requestCancellation.Cancel(); if (preview.Image != null) preview.Image.Dispose(); tips.Dispose(); }
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
            if (relatedCancellation != null) relatedCancellation.Cancel();
            ClearRecommendations();
            attachment = value; selectedVideo = null; originWindow = window; captureNote = note; demoCapture = false; linkInput = false; currentResult = null; details.Text = "查看依据"; related.Enabled = false;
            draftChanged = false; another.Text = "换一句"; copy.Text = "复制 · 回到微信";
            reply.Clear(); summary.Text = "核对内容与对象，再点「生成回复」。"; source.Text = text ?? "";
            variant = 0; another.Enabled = false;
            preview.Visible = value != null;
            SetPreview(value != null && value.Kind == "image" ? value.Bytes : null);
            coverage.Text = note; status.Text = bubble.IsDemo ? "演示素材已读取 · 尚未上传" : "内容已读取 · 尚未上传";
            scroll.AutoScrollPosition = Point.Empty;
        }

        internal void PasteShare()
        {
            if (busy) return;
            ShowBeside(bubble.CurrentWindow);
            try
            {
                string text = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
                if (text.Length == 0 || text.Length > 6000) { source.Visible = true; source.Focus(); throw new InvalidOperationException("请在输入框填写消息，或先复制分享链接（最多 6000 字）。视频卡片请用「从微信接入」。"); }
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

        internal void SearchVideo(string[] hints = null, WeChatWindow origin = null, VideoListMode mode = VideoListMode.Search)
        {
            if (busy) return;
            var window = origin ?? bubble.CurrentWindow;
            ShowBeside(window);
            using (var dialog = new VideoSearchForm(api, service.Text.Trim(), hints, mode, mode == VideoListMode.Related && selectedVideo != null ? selectedVideo.object_id : null))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var video = dialog.Selection;
                SelectVideo(video, window);
            }
        }

        private void SelectVideo(VideoChoice video, WeChatWindow window)
        {
            ResetMaterial(null, window, "已选：" + video.author + " · " + video.duration + " 秒", video.title);
            selectedVideo = video; linkInput = true; source.Visible = false;
            summary.Text = video.author + " · " + video.title;
            status.Text = "视频已选好 · 生成后后台读取讲述与评论";
        }

        private void ClearRecommendations()
        {
            while (recommendations.Controls.Count > 0) { var control = recommendations.Controls[0]; recommendations.Controls.Remove(control); control.Dispose(); }
            recommendations.Visible = false;
        }

        private async Task LoadRecommendations(ReplyResult result, string originalId)
        {
            if (result.RelatedQueries.Length == 0) return;
            if (relatedCancellation != null) relatedCancellation.Cancel();
            var cancellation = new CancellationTokenSource(); relatedCancellation = cancellation;
            ClearRecommendations(); recommendations.Visible = true;
            var label = new Label { Text = "相关视频 · 正在微信中查找…", AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Green, Margin = new Padding(0, 14, 0, 12) };
            recommendations.Controls.Add(label);
            var seen = new HashSet<string>(); if (originalId != null) seen.Add(originalId);
            int count = 0; string failure = "";
            try {
                foreach (var query in result.RelatedQueries.Take(2)) {
                    VideoChoice[] candidates;
                    try { candidates = await api.Search(service.Text.Trim(), query, cancellation.Token); }
                    catch (OperationCanceledException) { return; }
                    catch (Exception error) { failure = error.Message; continue; }
                    if (IsDisposed || cancellation.IsCancellationRequested || currentResult != result) return;
                    foreach (var video in candidates.Where(v => seen.Add(v.object_id)).Take(3)) {
                        count++;
                        var card = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, BackColor = Theme.Paper, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 10) };
                        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                        card.Controls.Add(new Label { Text = video.author + " · " + video.duration + " 秒", AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Green });
                        card.Controls.Add(new Label { Text = video.title, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 8, 0, 8), MaximumSize = new Size(Math.Max(200, recommendations.Width - 36), 0) });
                        card.Controls.Add(new Label { Text = "检索词：" + query + " · 此候选尚未分析", AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Muted });
                        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
                        var analyze = new Button { Text = "分析这条", AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Theme.Green };
                        analyze.Click += async delegate {
                            if (busy || MessageBox.Show(this, "分析这条视频会替换当前草稿，继续吗？", "分析相关视频", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                            SelectVideo(video, originWindow ?? bubble.CurrentWindow); await GenerateReply();
                        };
                        var share = new Button { Text = String.IsNullOrWhiteSpace(video.share_url) ? "复制标题找原视频" : "复制分享链接", AutoSize = true, FlatStyle = FlatStyle.Flat };
                        share.Click += delegate {
                            try { Clipboard.SetText(String.IsNullOrWhiteSpace(video.share_url) ? video.author + " " + video.title : video.share_url); status.Text = String.IsNullOrWhiteSpace(video.share_url) ? "已复制作者和标题，可在微信搜索原视频再分享。" : "已复制原视频链接，可回到微信粘贴。"; }
                            catch { status.Text = "复制失败，请稍后再试。"; }
                        };
                        actions.Controls.Add(analyze); actions.Controls.Add(share); card.Controls.Add(actions); recommendations.Controls.Add(card);
                    }
                    label.Text = "相关视频 · 已找到 " + count + " 条，下面可直接选取";
                }
                if (!IsDisposed && currentResult == result && !cancellation.IsCancellationRequested) label.Text = count > 0 ? "相关视频 · " + count + " 条候选（未核查内容）" + (failure.Length > 0 ? " · 部分检索未成功" : "") : "相关视频暂未取得 · " + (failure.Length > 0 ? failure : "可点「找相关视频」换一个词重试。");
            } finally { if (relatedCancellation == cancellation) relatedCancellation = null; cancellation.Dispose(); }
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
            if (currentResult != null) using (var dialog = new EvidenceForm(currentResult)) dialog.ShowDialog(this);
        }

        internal void ShowBeside(WeChatWindow window)
        {
            bool wasVisible = Visible;
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
            Theme.FitToScreen(this, area);
            Show();
            if (!wasVisible && !busy) Reveal(currentResult != null && currentResult.Partial ? details : !String.IsNullOrWhiteSpace(reply.Text) ? reply.Parent : status);
            Activate();
        }

        private void Reveal(Control control)
        {
            if (Visible && !IsDisposed) { scroll.PerformLayout(); scroll.ScrollControlIntoView(control); }
        }

        private void DisplayResult(ReplyResult result)
        {
            draftChanged = false; another.Text = "换一句"; copy.Text = "复制 · 回到微信";
            reply.Text = result.Reply;
            summary.Text = result.Summary;
            currentResult = result;
            details.Text = "查看依据";
            coverage.Text = result.Coverage.Length > 0 ? "读取范围、原文和 " + result.Comments.Count + " 条评论样本见「查看依据」。" : captureNote;
            if (linkInput) source.Visible = false;
        }

        private void MarkDraftChanged()
        {
            if (busy || currentResult == null) return;
            draftChanged = true;
            if (relatedCancellation != null) relatedCancellation.Cancel();
            ClearRecommendations(); related.Enabled = false;
            details.Text = "上次分析依据"; another.Text = "重新生成"; copy.Text = "复制保留草稿";
            status.Text = "内容或对象已修改；下方保留上次草稿，请重新生成后核对。";
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
            if (!busy && args.Effect == DragDropEffects.None) status.Text = "微信未交出原链接。请在微信点开卡片一次，再点「从微信接入」。";
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

        private async Task GenerateReply(bool refresh = false)
        {
            if (busy) { if (requestCancellation != null) requestCancellation.Cancel(); return; }
            if (String.IsNullOrWhiteSpace(source.Text) && attachment == null && selectedVideo == null)
            {
                status.Text = "请先从微信接入视频，或粘贴分享链接 / 消息。";
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
            Reveal(status);
            try
            {
                int nextVariant = String.IsNullOrWhiteSpace(reply.Text) ? variant : (variant + 1) % 3;
                var progress = new Progress<string>(message => { if (!IsDisposed && busy) status.Text = message; });
                var result = await api.Generate(service.Text.Trim(), source.Text.Trim(), combined, nextVariant, attachment, selectedVideo, requestCancellation.Token, progress, refresh);
                variant = nextVariant;
                DisplayResult(result);
                another.Enabled = true;
                status.Text = "回复已生成 · " + result.Timing + "。详细分析见「查看依据」。";
                completed = result;
                try { memory.Save(target, extra); if (target.Length > 0 && !recipient.Items.Contains(target)) recipient.Items.Add(target); } catch { status.Text += " 本次对象备注未能保存。"; }
            }
            catch (GenerationException error) {
                status.Text = error.Message;
                if (error.Retained != null) {
                    currentResult = error.Retained;
                    details.Text = "查看已读资料";
                    coverage.Text = "已保留 " + currentResult.Transcript.Length + " 字转写、" + currentResult.Comments.Count + " 条评论。点「查看已读资料」。";
                    summary.Text = "回复尚未生成，已读资料可查看；原有回复草稿保留。";
                }
            }
            catch (OperationCanceledException) { status.Text = requestCancellation.IsCancellationRequested ? "已停止生成，原输入和回复草稿保留。" : "服务响应超时，原输入和回复草稿保留，可重试。"; }
            catch (Exception error) { status.Text = error.Message; }
            finally { requestCancellation.Dispose(); requestCancellation = null; SetBusy(false); }
            Reveal(completed != null ? reply.Parent : currentResult != null && currentResult.Partial ? details : status);
            if (completed != null && demoCapture) DemoWindow.SaveReport(this, completed, attachment, captureNote);
            if (completed != null) await LoadRecommendations(completed, selectedVideo == null ? null : selectedVideo.object_id);
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
            if (value && relatedCancellation != null) relatedCancellation.Cancel();
            recommendations.Enabled = !value;
            busy = value;
            append.Enabled = paste.Enabled = read.Enabled = select.Enabled = clip.Enabled = import.Enabled = generate.Enabled = recipient.Enabled = source.Enabled = context.Enabled = !value;
            reply.ReadOnly = value;
            another.Enabled = copy.Enabled = !value && !String.IsNullOrWhiteSpace(reply.Text);
            related.Enabled = !value && !draftChanged && currentResult != null && currentResult.RelatedQueries.Length > 0;
            generate.Enabled = !value || requestCancellation != null;
            generate.Text = value ? (requestCancellation == null ? "正在读取…" : "停止生成") : currentResult != null && currentResult.Partial ? "重试生成回复" : "生成回复";
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
            button.Font = Theme.Font(10, primary ? FontStyle.Bold : FontStyle.Regular);
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

        internal void CheckResultState()
        {
            var material = new AttachmentData { Kind = "video", Name = "regression.mp4", FilePath = "regression.mp4" };
            var result = new ReplyResult { Reply = "回复", Summary = "概要", Coverage = "完整转写", Evidence = new String('字', 7000) };
            foreach (string input in new[] { "", "结合我补充的这句话" }) {
                ResetMaterial(material, null, "回归检查", input);
                DisplayResult(result);
                if (source.Text != input || attachment != material || currentResult.Evidence.Length != 7000 || reply.Text != "回复") throw new Exception("生成结果改变了原始输入或丢失资料，长视频换一句可能失败");
                source.Text = "修改后的原文";
                if (!draftChanged || reply.Text != "回复" || currentResult != result || details.Text != "上次分析依据") throw new Exception("修改输入后没有标明旧草稿，或丢失了原依据");
                DisplayResult(result);
                context.Text = "新的关系备注";
                if (!draftChanged || copy.Text != "复制保留草稿") throw new Exception("更改关系备注后未提示重新生成");
                context.Clear();
            }
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
            using (var activation = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\ZhiyiDesktopCompanion.Activate"))
            using (var mutex = new Mutex(true, "Local\\ZhiyiDesktopCompanion", out created))
            {
                // Keep the signal until the first instance's UI timer is ready.
                if (!created) { activation.Set(); return 0; }
                Application.Run(new BubbleForm(null, activation));
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
                using (var bubble = new BubbleForm()) bubble.Assistant.CheckResultState();
                var smallScreen = new Rectangle(-1280, 0, 1280, 680);
                var fitted = Theme.FitWindow(new Rectangle(100, -20, 1480, 900), smallScreen);
                if (fitted != smallScreen) throw new Exception("大窗口没有完整适配小屏幕");
                var normalWindow = new Rectangle(100, 100, 500, 600);
                if (Theme.FitWindow(normalWindow, new Rectangle(0, 0, 1920, 1080)) != normalWindow) throw new Exception("正常窗口被意外移动");
                string signalName = "Local\\ZhiyiActivationTest." + Guid.NewGuid().ToString("N");
                using (var listener = new EventWaitHandle(false, EventResetMode.AutoReset, signalName))
                using (var launcher = EventWaitHandle.OpenExisting(signalName)) {
                    launcher.Set();
                    if (!listener.WaitOne(0) || listener.WaitOne(0)) throw new Exception("重复启动唤醒信号未保留或重复消费");
                }
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
