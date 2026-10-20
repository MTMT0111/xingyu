using System;
using System.Collections.Generic;
using System.Drawing;
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
        internal static Font Font(float size, FontStyle style) { return new Font("Microsoft YaHei UI", size * 1.34f, style, GraphicsUnit.Pixel); }
    }

    internal sealed class BubbleForm : Form
    {
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly AssistantForm assistant;
        private WeChatWindow currentWindow;
        private bool menuOpen;

        internal BubbleForm()
        {
            Text = "知意";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(54, 54);
            BackColor = Theme.Green;
            using (var bubblePath = new GraphicsPath())
            {
                bubblePath.AddEllipse(0, 0, 54, 54);
                Region = new Region(bubblePath);
            }
            assistant = new AssistantForm(this);
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开知意", null, delegate { OpenAssistant(false); });
            menu.Items.Add("退出", null, delegate { assistant.Close(); Close(); });
            menu.Opening += delegate { menuOpen = true; };
            menu.Closed += delegate { menuOpen = false; };
            ContextMenuStrip = menu;
            MouseClick += delegate(object sender, MouseEventArgs args) { if (args.Button == MouseButtons.Left) OpenAssistant(true); };
            Paint += DrawBubble;
            timer.Interval = 800;
            timer.Tick += delegate { AttachToWeChat(); };
            timer.Start();
            AttachToWeChat();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        internal WeChatWindow CurrentWindow { get { return currentWindow; } }

        private void DrawBubble(object sender, PaintEventArgs args)
        {
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(currentWindow == null ? Color.FromArgb(139, 154, 147) : Theme.Green)) args.Graphics.FillEllipse(brush, 0, 0, 53, 53);
            using (var pen = new Pen(Color.FromArgb(240, 255, 255, 255), 2)) args.Graphics.DrawEllipse(pen, 2, 2, 49, 49);
            using (var font = Theme.Font(16, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.White))
            {
                var size = args.Graphics.MeasureString("知", font);
                args.Graphics.DrawString("知", font, brush, (54 - size.Width) / 2, (54 - size.Height) / 2 - 1);
            }
        }

        private void AttachToWeChat()
        {
            if (menuOpen) return;
            var next = WeChatCapture.FindWindow();
            currentWindow = next;
            var area = Screen.PrimaryScreen.WorkingArea;
            if (next == null)
            {
                Location = new Point(area.Right - Width - 18, area.Top + area.Height / 2 - Height / 2);
            }
            else
            {
                int x = Math.Min(area.Right - Width - 8, Math.Max(area.Left + 8, next.Bounds.Right - Width - 10));
                int y = Math.Min(area.Bottom - Height - 8, Math.Max(area.Top + 8, next.Bounds.Top + next.Bounds.Height / 2 - Height / 2));
                Location = new Point(x, y);
            }
            Invalidate();
        }

        private void OpenAssistant(bool capture)
        {
            if (capture) assistant.CaptureCurrentWindow();
            assistant.ShowBeside(currentWindow);
        }
    }

    internal sealed class AssistantForm : Form
    {
        private readonly BubbleForm bubble;
        private readonly ApiClient api = new ApiClient();
        private readonly ComboBox recipient = new ComboBox();
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
            ClientSize = new Size(500, 820);
            MinimumSize = new Size(500, 680);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Theme.Canvas;
            Font = Theme.Font(9, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.None;
            AllowDrop = true;
            DragEnter += AcceptMediaDrop;
            DragDrop += ReceiveMediaDrop;
            BuildUi();
        }

        private void BuildUi()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 68, BackColor = Theme.Paper, Padding = new Padding(18, 12, 12, 8) };
            var title = new Label { Text = "知意", AutoSize = true, Location = new Point(18, 11), Font = Theme.Font(16, FontStyle.Bold), ForeColor = Theme.Ink };
            var sub = new Label { Text = "当前微信，一处完成", AutoSize = true, Location = new Point(19, 39), Font = Theme.Font(8, FontStyle.Regular), ForeColor = Theme.Muted };
            var settingsButton = FlatButton("服务", 58, 34, false);
            settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            settingsButton.Location = new Point(424, 17);
            settingsButton.Click += delegate { settings.Visible = !settings.Visible; };
            var hideButton = FlatButton("收起", 58, 34, false);
            hideButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            hideButton.Location = new Point(360, 17);
            hideButton.Click += delegate { Hide(); };
            header.Controls.AddRange(new Control[] { title, sub, hideButton, settingsButton });

            settings.Dock = DockStyle.Top;
            settings.Height = 82;
            settings.Visible = false;
            settings.Padding = new Padding(16, 10, 16, 8);
            settings.BackColor = Theme.PaleGreen;
            settings.Controls.Add(new Label { Text = "本机服务地址", AutoSize = true, Location = new Point(17, 9), ForeColor = Theme.Muted });
            service.Text = LoadServiceUrl();
            service.Location = new Point(17, 31);
            service.Width = 448;
            service.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            service.Height = 28;
            service.BorderStyle = BorderStyle.FixedSingle;
            service.Leave += delegate { SaveServiceUrl(); };
            settings.Controls.Add(service);

            var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            body.AllowDrop = true;
            body.DragEnter += AcceptMediaDrop;
            body.DragDrop += ReceiveMediaDrop;
            int y = 10;
            body.Controls.Add(SectionLabel("当前对象", y));
            recipient.DropDownStyle = ComboBoxStyle.DropDown;
            recipient.Location = new Point(18, y + 24);
            recipient.Size = new Size(448, 36);
            recipient.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            recipient.Font = Theme.Font(10, FontStyle.Regular);
            recipient.Text = "先在微信中打开一个聊天";
            body.Controls.Add(recipient);
            y += 68;

            read.SetBounds(18, y, 218, 46);
            StyleButton(read, "读取当前微信", true);
            read.Click += delegate { CaptureCurrentWindow(); };
            import.SetBounds(248, y, 218, 46);
            StyleButton(import, "选择聊天视频文件", false);
            import.Click += ImportMedia;
            body.Controls.AddRange(new Control[] { read, import });
            y += 55;

            status.Text = "点悬浮球，只读取这一次当前可见内容。";
            status.SetBounds(18, y, 448, 42);
            status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            status.ForeColor = Theme.Muted;
            status.Font = Theme.Font(8, FontStyle.Regular);
            body.Controls.Add(status);
            y += 38;

            body.Controls.Add(SectionLabel("读取到的内容", y));
            source.Multiline = true;
            source.ScrollBars = ScrollBars.Vertical;
            source.MaxLength = 6000;
            source.SetBounds(18, y + 24, 448, 126);
            source.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            source.BorderStyle = BorderStyle.FixedSingle;
            source.Font = Theme.Font(10, FontStyle.Regular);
            body.Controls.Add(source);
            y += 162;

            body.Controls.Add(SectionLabel("补充一句（可选）", y));
            context.SetBounds(18, y + 24, 448, 64);
            context.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            context.Multiline = true;
            context.MaxLength = 160;
            context.BorderStyle = BorderStyle.FixedSingle;
            context.Font = Theme.Font(9, FontStyle.Regular);
            context.Text = "回复自然一点，不用复述原文。";
            body.Controls.Add(context);
            y += 100;

            generate.SetBounds(18, y, 448, 50);
            generate.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            StyleButton(generate, "帮我回一句", true);
            generate.Click += async delegate { await GenerateReply(); };
            body.Controls.Add(generate);
            y += 62;

            summary.SetBounds(18, y, 448, 50);
            summary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            summary.Text = "视频卡片可分析封面与可见评论；完整视频请从聊天拖入或先保存再选择。";
            summary.ForeColor = Theme.Muted;
            summary.Font = Theme.Font(8, FontStyle.Regular);
            body.Controls.Add(summary);
            y += 48;

            body.Controls.Add(SectionLabel("回复建议", y));
            reply.Multiline = true;
            reply.ScrollBars = ScrollBars.Vertical;
            reply.MaxLength = 1000;
            reply.SetBounds(18, y + 24, 448, 140);
            reply.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            reply.BorderStyle = BorderStyle.FixedSingle;
            reply.Font = Theme.Font(10, FontStyle.Regular);
            body.Controls.Add(reply);
            y += 144;

            var another = FlatButton("换一句", 96, 42, false);
            another.Location = new Point(18, y);
            another.Click += async delegate { await GenerateReply(); };
            copy.SetBounds(124, y, 342, 46);
            StyleButton(copy, "复制并回微信", true);
            copy.Click += CopyAndReturn;
            body.Controls.AddRange(new Control[] { another, copy });
            y += 58;

            var note = new Label {
                Text = "不会自动发送。公众号文章会读取当前窗口暴露的正文和画面；完整视频请使用上方文件入口。",
                ForeColor = Theme.Muted, Font = Theme.Font(8, FontStyle.Regular), AutoSize = false
            };
            note.SetBounds(18, y, 448, 58);
            note.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            body.Controls.Add(note);
            body.AutoScrollMinSize = new Size(0, y + 58);

            Controls.Add(body);
            Controls.Add(settings);
            Controls.Add(header);
        }

        internal void ShowBeside(WeChatWindow window)
        {
            var area = Screen.PrimaryScreen.WorkingArea;
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

        internal void CaptureCurrentWindow()
        {
            if (busy) return;
            var window = bubble.CurrentWindow ?? WeChatCapture.FindWindow();
            if (window == null)
            {
                status.Text = "没有找到电脑版微信，请先打开微信聊天或文章窗口。";
                ShowBeside(null);
                return;
            }
            try
            {
                Hide();
                bubble.Hide();
                NativeMethods.SetForegroundWindow(window.Handle);
                Thread.Sleep(160);
                capture = WeChatCapture.Read(window);
                attachment = new AttachmentData { Kind = "image", Name = capture.FileName, Mime = capture.Mime, Bytes = capture.Image };
                source.Text = capture.Text;
                if (!String.IsNullOrWhiteSpace(capture.Recipient)) AddRecipient(capture.Recipient);
                status.Text = capture.Text.Length > 0
                    ? "已读取可见文字并截取聊天区域；点击生成后才上传。"
                    : "微信没有开放聊天文字，已截取聊天区域供画面分析；尚未上传。";
                summary.Text = "视频卡片可分析封面与可见评论；完整视频请从聊天拖入，或先保存再选择。";
                variant = 0;
            }
            catch (Exception error) { status.Text = error.Message; }
            finally
            {
                bubble.Show();
                ShowBeside(window);
            }
        }

        private void ImportMedia(object sender, EventArgs args)
        {
            if (busy) return;
            using (var dialog = new OpenFileDialog { Filter = "微信视频或截图|*.mp4;*.webm;*.mov;*.jpg;*.jpeg;*.png;*.webp", Multiselect = false, CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    attachment = ApiClient.FromFile(dialog.FileName);
                    capture = null;
                    source.Text = "";
                    status.Text = "已选择“" + attachment.Name + "”，点击生成后才会上传分析。";
                    summary.Text = attachment.Kind == "video" ? "将分析完整视频画面和语音（最多 60 秒）。" : "将分析图片里的画面和文字。";
                    variant = 0;
                }
                catch (Exception error) { status.Text = error.Message; }
            }
        }

        private void AcceptMediaDrop(object sender, DragEventArgs args)
        {
            args.Effect = args.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void ReceiveMediaDrop(object sender, DragEventArgs args)
        {
            if (busy || !args.Data.GetDataPresent(DataFormats.FileDrop)) return;
            try
            {
                var paths = args.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null || paths.Length != 1) throw new InvalidOperationException("请一次拖入一个微信视频或图片文件。");
                attachment = ApiClient.FromFile(paths[0]);
                capture = null;
                source.Text = "";
                status.Text = "已从微信窗口接收“" + attachment.Name + "”；点击生成后才会上传分析。";
                summary.Text = attachment.Kind == "video" ? "将分析视频画面和语音（最多 60 秒）。" : "将分析图片里的画面和文字。";
                variant = 0;
            }
            catch (Exception error) { status.Text = error.Message; }
        }

        private async Task GenerateReply()
        {
            if (busy) return;
            if (String.IsNullOrWhiteSpace(source.Text) && attachment == null)
            {
                status.Text = "请先读取当前微信，或选择一张图片 / 完整视频。";
                return;
            }
            string target = recipient.Text.Trim();
            string extra = context.Text.Trim();
            string combined = (target.Length > 0 && target != "先在微信中打开一个聊天" ? "当前聊天对象可能是“" + target + "”。" : "") + extra;
            if (combined.Length > 200) combined = combined.Substring(0, 200);
            SetBusy(true);
            status.Text = attachment != null && attachment.Kind == "video" ? "正在读取视频画面和语音…" : "正在分析当前内容…";
            try
            {
                int nextVariant = String.IsNullOrWhiteSpace(reply.Text) ? variant : (variant + 1) % 3;
                var result = await api.Generate(service.Text.Trim(), source.Text.Trim(), combined, nextVariant, attachment);
                variant = nextVariant;
                reply.Text = result.Reply;
                summary.Text = result.Summary;
                status.Text = "回复已生成，可直接修改；发送前由你确认。";
            }
            catch (Exception error) { status.Text = error.Message; }
            finally { SetBusy(false); }
        }

        private void CopyAndReturn(object sender, EventArgs args)
        {
            if (busy || String.IsNullOrWhiteSpace(reply.Text)) { status.Text = "先生成或写好一句回复。"; return; }
            try
            {
                Clipboard.SetText(reply.Text);
                status.Text = "已复制，回到微信后粘贴并确认发送。";
                Hide();
                var window = bubble.CurrentWindow ?? WeChatCapture.FindWindow();
                if (window != null) NativeMethods.SetForegroundWindow(window.Handle);
            }
            catch { status.Text = "复制失败，请选中回复后按 Ctrl+C。"; }
        }

        private void AddRecipient(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return;
            var existing = recipient.Items.Cast<object>().Select(item => item.ToString()).ToList();
            if (!existing.Contains(value)) recipient.Items.Insert(0, value);
            recipient.SelectedItem = value;
            recipient.Text = value;
        }

        private void SetBusy(bool value)
        {
            busy = value;
            read.Enabled = import.Enabled = generate.Enabled = copy.Enabled = recipient.Enabled = source.Enabled = context.Enabled = !value;
            generate.Text = value ? "正在准备回复…" : "帮我回一句";
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
            AddRecipient("妈妈");
            source.Text = "你最近是不是又忙到很晚？天凉了，多穿点，也要记得吃饭。";
            status.Text = "已读取当前可见文字和一帧画面；尚未上传。";
            summary.Text = "这是一条关心近况、提醒保暖和吃饭的消息。";
            reply.Text = "妈，关心收到啦，谢谢你惦记。你那边也降温了吗？";
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
                string normalized = WeChatCapture.NormalizeLines(new[] { " 你好  世界 ", "你好 世界", "第二句" }, 20);
                if (normalized != "你好 世界\r\n第二句") throw new Exception("文字去重失败");
                if (WeChatCapture.NormalizeLines(new[] { "Weixin", "MMUIRenderSubWindowHW", "这是一条消息" }, 6000) != "这是一条消息") throw new Exception("微信自绘控件噪声过滤失败");
                var bounds = new Rectangle(100, 100, 1000, 700);
                var candidates = new List<TextCandidate> {
                    new TextCandidate { Text = "搜索", Bounds = new Rectangle(150, 130, 80, 30) },
                    new TextCandidate { Text = "妈妈", Selected = true, Bounds = new Rectangle(200, 240, 150, 50) },
                    new TextCandidate { Text = "错误标题", Bounds = new Rectangle(700, 140, 120, 30) }
                };
                if (WeChatCapture.PickRecipient(candidates, bounds) != "妈妈") throw new Exception("当前聊天识别失败");
                if (WeChatCapture.PickRecipient(new[] { candidates[0], candidates[2] }, bounds) != "错误标题") throw new Exception("标题回退失败");
                Console.WriteLine("Windows client self-test passed: text normalization and current-chat selection.");
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
                form.Close();
            }
            return 0;
        }
    }
}
