using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ZhiyiDesktop
{
    internal sealed class VideoSearchForm : Form
    {
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        internal VideoChoice Selection;

        internal VideoSearchForm(ApiClient api, string serviceUrl)
        {
            Text = "搜索视频号"; ClientSize = new Size(490, 440); MinimumSize = new Size(430, 400);
            StartPosition = FormStartPosition.CenterParent; TopMost = true; BackColor = Theme.Canvas;
            Font = Theme.Font(10, FontStyle.Regular); MinimizeBox = MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            var keyword = new TextBox { Dock = DockStyle.Fill, MaxLength = 33 };
            var search = new Button { Text = "搜索", Dock = DockStyle.Right, Width = 80 };
            var top = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(16) };
            top.Controls.Add(keyword); top.Controls.Add(search);
            var notice = new Label { Dock = DockStyle.Top, Height = 48, Padding = new Padding(16, 4, 16, 4), Text = "输入视频标题或关键词。搜索完成后核对作者，选择对应视频。", ForeColor = Theme.Muted };
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
                    notice.Text = items.Length == 0 ? "没有匹配结果，试试标题中的其他关键词。" : "找到 " + items.Length + " 条候选。核对作者和标题后选择，不会自动播放。";
                } catch (OperationCanceledException) { if (!IsDisposed) notice.Text = "搜索已取消或超时。"; }
                catch (Exception error) { if (!IsDisposed) notice.Text = error.Message; }
                finally { if (!IsDisposed) search.Enabled = true; }
            };
            Controls.Add(list); Controls.Add(notice); Controls.Add(top); Controls.Add(choose);
            AcceptButton = search;
            FormClosing += delegate { cancellation.Cancel(); };
            FormClosed += delegate { cancellation.Dispose(); };
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) Scale(new SizeF(graphics.DpiX / 96f, graphics.DpiY / 96f));
        }
    }
}
