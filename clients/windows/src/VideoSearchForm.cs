using System;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ZhiyiDesktop
{
    internal static class CardLookup
    {
        // Only search hints: a cover is never evidence of the video's contents or identity.
        internal static string[] Queries(string recognized)
        {
            return (recognized ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => Regex.Replace(line.Trim(), @"(?<=[\u3400-\u9fff])\s+(?=[\u3400-\u9fff])", ""))
                .Select(line => Regex.Replace(line, @"^[^\p{L}\p{N}]+", "").Trim())
                .Where(line => line.Length >= 2 && Regex.IsMatch(line, @"\p{L}") && line != "视频号" && line != "微信")
                .Select(line => line.Substring(0, Math.Min(33, line.Length))).Distinct().Take(8).ToArray();
        }

        internal static void SelfTest()
        {
            var hints = Queries("  利益 即 感情\r\n∞ 老朱别胡说\r\n利益即感情\r\n14:05\r\n视频号");
            if (hints.Length != 2 || hints[0] != "利益即感情" || hints[1] != "老朱别胡说") throw new Exception("卡片检索提示清理失败");
            if (Queries(null).Length != 0 || Queries(new string('长', 80))[0].Length != 33 || Queries("Hello world")[0] != "Hello world") throw new Exception("卡片检索提示边界失败");
        }
    }

    internal enum VideoListMode { Search, Recent, Related }

    internal sealed class VideoSearchForm : Form
    {
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        internal VideoChoice Selection;

        internal VideoSearchForm(ApiClient api, string serviceUrl, string[] hints = null, VideoListMode mode = VideoListMode.Search, string excludeId = null)
        {
            bool recent = mode == VideoListMode.Recent, related = mode == VideoListMode.Related;
            Text = recent ? "从微信接入 · 原视频记录" : related ? "相关视频 · 延伸候选" : "搜索视频号"; ClientSize = new Size(560, 520); MinimumSize = new Size(480, 440);
            StartPosition = FormStartPosition.CenterParent; TopMost = true; BackColor = Theme.Canvas;
            Font = Theme.Font(10, FontStyle.Regular); MinimizeBox = MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            var keyword = new ComboBox { Dock = DockStyle.Fill, MaxLength = 33, DropDownStyle = ComboBoxStyle.DropDown };
            keyword.Visible = !recent;
            if (hints != null) { keyword.Items.AddRange(hints); if (hints.Length > 0) keyword.SelectedIndex = 0; }
            var search = new Button { Text = recent ? "刷新" : "搜索", Dock = DockStyle.Right, Width = 80 };
            var top = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(16) };
            top.Controls.Add(keyword); top.Controls.Add(search);
            var notice = new Label { Dock = DockStyle.Top, Height = 60, Padding = new Padding(16, 4, 16, 4), Text = "输入视频标题或关键词。搜索完成后核对作者，选择对应视频。", ForeColor = Theme.Muted };
            var observed = new Label { Dock = DockStyle.Top, Height = 72, Padding = new Padding(16, 4, 16, 4), Text = recent ? "在微信点开目标卡片一次，回来点刷新。不用看完。\r\n这里显示连接器实际捕获的原视频；请核对标题和时间。" : related ? "根据本次内容搜索相关话题，候选尚未阅读。\r\n先分析再判断是否适合发给对方，原回复保留到你选择新视频。" : hints == null ? "" : "卡片识别：" + String.Join(" / ", hints) + "\r\n仅用于查找；核对作者、标题，找不到原视频就取消。", ForeColor = Theme.Green, Visible = recent || related || hints != null };
            var list = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 82 };
            list.DrawItem += delegate(object sender, DrawItemEventArgs e) {
                if (e.Index < 0) return;
                e.DrawBackground();
                TextRenderer.DrawText(e.Graphics, list.Items[e.Index].ToString(), Font,
                    new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 8, e.Bounds.Width - 24, e.Bounds.Height - 16),
                    e.ForeColor, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
            var choose = new Button { Text = related ? "分析这条，再决定怎么回复" : "使用这条原视频", Dock = DockStyle.Bottom, Height = 48, Enabled = false, BackColor = Theme.Green, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            var copyLink = new Button { Text = "复制候选标题与原链接", Dock = DockStyle.Bottom, Height = 40, Enabled = false, Visible = related, FlatStyle = FlatStyle.Flat, ForeColor = Theme.Green };
            list.SelectedIndexChanged += delegate { var video = list.SelectedItem as VideoChoice; choose.Enabled = copyLink.Enabled = video != null; copyLink.Text = video != null && WeChatCapture.IsShareLink(video.share_url ?? "") ? "复制候选标题与原链接" : "复制作者与标题，在微信查找"; };
            copyLink.Click += delegate {
                var video = list.SelectedItem as VideoChoice;
                if (video == null) return;
                bool hasLink = WeChatCapture.IsShareLink(video.share_url ?? "");
                try { Clipboard.SetText(video.title + "\r\n" + video.author + (hasLink ? "\r\n" + video.share_url : "")); notice.Text = hasLink ? "已复制标题和原链接。尚未分析，请先核对内容再发给对方。" : "微信未提供可分享原链接。已复制作者和标题，可在微信搜索后转发原卡片。"; }
                catch { notice.Text = "剪贴板暂不可用，请重试。"; }
            };
            choose.Click += delegate { Selection = list.SelectedItem as VideoChoice; if (Selection != null) { DialogResult = DialogResult.OK; Close(); } };
            search.Click += async delegate {
                if (!recent && String.IsNullOrWhiteSpace(keyword.Text)) { notice.Text = "请输入视频标题中的几个关键词。"; return; }
                search.Enabled = false; list.Items.Clear(); choose.Enabled = copyLink.Enabled = false; notice.Text = recent ? "正在读取本机连接器捕获的原视频…" : "正在使用已登录的微信会话搜索…";
                try {
                    var items = recent ? await api.Recent(serviceUrl, cancellation.Token) : await api.Search(serviceUrl, keyword.Text.Trim(), cancellation.Token);
                    if (excludeId != null) items = items.Where(item => item.object_id != excludeId).ToArray();
                    if (IsDisposed) return;
                    list.Items.AddRange(items);
                    notice.Text = items.Length == 0 ? (recent ? "暂未捕获原视频。请先打开卡片详情，再刷新；需使用新版连接器。不会退回封面猜测。" : "没有匹配结果。可换用上方的另一个话题再搜索。") : "取得 " + items.Length + " 条" + (recent ? "原视频记录，核对标题和时间后选择。" : "候选。请核对作者、标题；尚未阅读候选内容。");
                } catch (OperationCanceledException) { if (!IsDisposed) notice.Text = "搜索已取消或超时。"; }
                catch (Exception error) { if (!IsDisposed) notice.Text = error.Message; }
                finally { if (!IsDisposed) search.Enabled = true; }
            };
            Controls.Add(list); Controls.Add(notice); Controls.Add(observed); Controls.Add(top); Controls.Add(copyLink); Controls.Add(choose);
            AcceptButton = search;
            Shown += delegate {
                using (var graphics = CreateGraphics()) list.ItemHeight = (int)(82 * graphics.DpiY / 96f);
                if (recent || hints != null && hints.Length > 0) search.PerformClick();
            };
            FormClosing += delegate { cancellation.Cancel(); };
            FormClosed += delegate { cancellation.Dispose(); };
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) Scale(new SizeF(graphics.DpiX / 96f, graphics.DpiY / 96f));
        }
    }
}
