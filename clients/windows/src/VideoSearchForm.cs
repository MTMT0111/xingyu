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

    internal sealed class VideoSearchForm : Form
    {
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        internal VideoChoice Selection;

        internal VideoSearchForm(ApiClient api, string serviceUrl, string[] hints = null)
        {
            Text = "搜索视频号"; ClientSize = new Size(490, 440); MinimumSize = new Size(430, 400);
            StartPosition = FormStartPosition.CenterParent; TopMost = true; BackColor = Theme.Canvas;
            Font = Theme.Font(10, FontStyle.Regular); MinimizeBox = MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            var keyword = new ComboBox { Dock = DockStyle.Fill, MaxLength = 33, DropDownStyle = ComboBoxStyle.DropDown };
            if (hints != null) { keyword.Items.AddRange(hints); if (hints.Length > 0) keyword.SelectedIndex = 0; }
            var search = new Button { Text = "搜索", Dock = DockStyle.Right, Width = 80 };
            var top = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(16) };
            top.Controls.Add(keyword); top.Controls.Add(search);
            var notice = new Label { Dock = DockStyle.Top, Height = 60, Padding = new Padding(16, 4, 16, 4), Text = "输入视频标题或关键词。搜索完成后核对作者，选择对应视频。", ForeColor = Theme.Muted };
            var observed = new Label { Dock = DockStyle.Top, Height = 62, Padding = new Padding(16, 4, 16, 4), Text = hints == null ? "" : "卡片识别：" + String.Join(" / ", hints) + "\r\n仅用于查找；核对作者、标题，找不到原视频就取消。", ForeColor = Theme.Green, Visible = hints != null };
            var list = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 82 };
            list.DrawItem += delegate(object sender, DrawItemEventArgs e) {
                if (e.Index < 0) return;
                e.DrawBackground();
                TextRenderer.DrawText(e.Graphics, list.Items[e.Index].ToString(), Font,
                    new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 8, e.Bounds.Width - 24, e.Bounds.Height - 16),
                    e.ForeColor, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
            var choose = new Button { Text = "使用这条视频", Dock = DockStyle.Bottom, Height = 48, Enabled = false, BackColor = Theme.Green, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            list.SelectedIndexChanged += delegate { choose.Enabled = list.SelectedItem != null; };
            choose.Click += delegate { Selection = list.SelectedItem as VideoChoice; if (Selection != null) { DialogResult = DialogResult.OK; Close(); } };
            search.Click += async delegate {
                if (String.IsNullOrWhiteSpace(keyword.Text)) { notice.Text = "请输入视频标题中的几个关键词。"; return; }
                search.Enabled = false; list.Items.Clear(); choose.Enabled = false; notice.Text = "正在使用已登录的微信会话搜索…";
                try {
                    var items = await api.Search(serviceUrl, keyword.Text.Trim(), cancellation.Token);
                    if (IsDisposed) return;
                    list.Items.AddRange(items);
                    notice.Text = items.Length == 0 ? "没有匹配结果。可从上方下拉框换用识别到的作者/标题，再搜索；不要选相似但不同的视频。" : "找到 " + items.Length + " 条候选。核对作者和标题后选择，不会自动播放。";
                } catch (OperationCanceledException) { if (!IsDisposed) notice.Text = "搜索已取消或超时。"; }
                catch (Exception error) { if (!IsDisposed) notice.Text = error.Message; }
                finally { if (!IsDisposed) search.Enabled = true; }
            };
            Controls.Add(list); Controls.Add(notice); Controls.Add(observed); Controls.Add(top); Controls.Add(choose);
            AcceptButton = search;
            Shown += delegate { if (hints != null && hints.Length > 0) search.PerformClick(); };
            FormClosing += delegate { cancellation.Cancel(); };
            FormClosed += delegate { cancellation.Dispose(); };
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) Scale(new SizeF(graphics.DpiX / 96f, graphics.DpiY / 96f));
        }
    }
}
