using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ZhiyiDesktop
{
    // 合成素材用于展示与回归，不读取或冒充真实微信聊天。
    internal sealed class DemoWindow : Form
    {
        internal static string ReportDirectory;
        internal static void SaveReport(Form form, ReplyResult result, AttachmentData attachment, string coverage)
        {
            if (String.IsNullOrWhiteSpace(ReportDirectory)) return;
            try
            {
                Directory.CreateDirectory(ReportDirectory);
                string name = attachment.Name.Contains("frames") ? "clip" : "text";
                File.WriteAllBytes(Path.Combine(ReportDirectory, name + "-evidence.jpg"), attachment.Bytes);
                using (var image = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                    image.Save(Path.Combine(ReportDirectory, name + "-result.png"), ImageFormat.Png);
                }
                File.WriteAllText(Path.Combine(ReportDirectory, name + "-result.json"), new JavaScriptSerializer().Serialize(new { generatedAt = DateTime.UtcNow.ToString("o"), source = "synthetic demo screen, manually selected through native UI", realApiCall = true, capturedAudio = false, coverage = coverage, evidence = result.Evidence, summary = result.Summary, reply = result.Reply }));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private int frame;
        private readonly string[] captions = { "周末别总坐在电脑前", "找一条喜欢的小路，出去走走", "不用赶路，慢慢散步就很好", "看看树，吹吹风，换个心情", "也可以约朋友一起走走", "照顾好自己，记得常联系" };
        internal DemoWindow()
        {
            Text = "知意功能演示 · 合成聊天与视频字幕";
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(70, 90);
            ClientSize = new Size(850, 650);
            Font = Theme.Font(11, FontStyle.Regular);
            BackColor = Theme.Canvas;
            Controls.Add(new Label { Text = "功能演示", Bounds = new Rectangle(32, 25, 600, 40), Font = Theme.Font(22, FontStyle.Bold), ForeColor = Theme.Ink });
            Controls.Add(new Label { Text = "以下均为合成样例，不是真实微信消息。框选它们，试试生成回复。", Bounds = new Rectangle(34, 78, 780, 30), ForeColor = Theme.Muted });
            Controls.Add(new Label { Text = "演示对象：妈妈", Bounds = new Rectangle(34, 136, 360, 32), Font = Theme.Font(14, FontStyle.Bold) });
            Controls.Add(new Label { Text = "最近总在忙吧？周末记得休息，\n也出去走走，别老对着电脑。", Bounds = new Rectangle(34, 185, 340, 150), Padding = new Padding(16), BackColor = Color.White, Font = Theme.Font(14, FontStyle.Regular) });
            Controls.Add(new Label { Text = "一条收到的视频分享", Bounds = new Rectangle(416, 136, 390, 30), Font = Theme.Font(14, FontStyle.Bold) });
            var video = new Panel { Bounds = new Rectangle(416, 185, 390, 370), BackColor = Theme.Green };
            video.Paint += delegate(object sender, PaintEventArgs e) {
                e.Graphics.Clear(Theme.Green);
                e.Graphics.ScaleTransform(video.Width / 390f, video.Height / 370f);
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var small = new Font("Microsoft YaHei UI", 16, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var large = new Font("Microsoft YaHei UI", 31, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    e.Graphics.DrawString("播放中的合成视频画面", small, Brushes.White, new RectangleF(15, 20, 360, 36), format);
                    e.Graphics.DrawString(captions[frame % captions.Length], large, Brushes.White, new RectangleF(25, 95, 340, 185), format);
                    e.Graphics.DrawString("字幕每 2 秒变化 · 无声音 · 演示素材", small, Brushes.White, new RectangleF(20, 335, 350, 25), format);
                }
                e.Graphics.FillRectangle(Brushes.White, 25, 320, 340 * ((frame % 6) + 1) / 6, 4);
            };
            Controls.Add(video);
            Controls.Add(new Label { Text = "点绿色悬浮球选择操作：\n\n① 框选左侧文字 → 生成回复\n\n② 读播放画面 → 框住右侧视频\n\n选区仅在点击生成后上传。", Bounds = new Rectangle(34, 352, 350, 240), ForeColor = Theme.Muted });
            timer.Interval = 2000; timer.Tick += delegate { frame++; video.Invalidate(); }; timer.Start();
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) Scale(new SizeF(graphics.DpiX / 96f, graphics.DpiY / 96f));
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
