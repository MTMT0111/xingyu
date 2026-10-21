using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZhiyiDesktop
{
    internal sealed class EvidenceForm : Form
    {
        private readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(18, 10) };
        private readonly Font heading = Theme.Font(14, FontStyle.Bold);
        private readonly Font body = Theme.Font(11, FontStyle.Regular);
        private readonly Font small = Theme.Font(10, FontStyle.Regular);

        internal EvidenceForm(ReplyResult result)
        {
            Text = "知意 · 回复依据"; ClientSize = new Size(740, 620); MinimumSize = new Size(570, 460);
            BackColor = Theme.Paper; Font = body; TopMost = true; StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            var header = new Panel { Dock = DockStyle.Top, Height = 88, Padding = new Padding(24, 16, 24, 8) };
            header.Controls.Add(new Label { Text = "原文优先，评论提供另一种视角。点赞表示热度，不代表事实正确。", Dock = DockStyle.Bottom, Height = 24, ForeColor = Theme.Muted, Font = small });
            header.Controls.Add(new Label { Text = "这句回复，从哪里来", Dock = DockStyle.Top, Height = 32, Font = Theme.Font(18, FontStyle.Bold), ForeColor = Theme.Ink });
            var close = new Button { Text = "返回编辑回复", Dock = DockStyle.Bottom, Height = 46, FlatStyle = FlatStyle.Flat, BackColor = Theme.Green, ForeColor = Color.White };
            close.Click += delegate { Close(); };
            Controls.Add(tabs); Controls.Add(close); Controls.Add(header);
            var overview = Page("概要与判断");
            Section(overview, "内容在说什么", result.Summary);
            Section(overview, "进一步分析 · 对照依据", result.Analysis);
            Section(overview, "评论里的分歧", result.CommentAnalysis);
            Section(overview, "尚不能下结论的地方", result.Caveats);
            Section(overview, "对方可能想表达", String.Join("\r\n\r\n", result.Meanings));
            Section(overview, "回复时留意", result.Tip);
            Section(overview, "实际读取范围", !String.IsNullOrWhiteSpace(result.Coverage) ? result.Coverage : "仅依据本次输入内容。", true);
            Section(overview, "本次处理", result.Timing, true);
            var original = Page("原文与转写");
            Section(original, "视频讲述 · 语音转写", result.Transcript, false, "本次没有取得音轨转写，不推断未听到的内容。");
            Section(original, "正文 / 画面文字", result.Visual);
            if (!String.IsNullOrWhiteSpace(result.Segments)) Section(Page("分段摘要"), "按视频时间顺序", result.Segments);
            var comments = Page("评论样本 · " + result.Comments.Count);
            Section(comments, "评论的分量", "按返回样本中的点赞热度排列。评论和评论回复均是用户观点；这不是全部评论，也不是支持率。", true);
            foreach (var comment in result.Comments)
            {
                Section(comments, comment.Reference + " · " + comment.Likes + (String.IsNullOrWhiteSpace(comment.Parent) ? " · 评论" : " · 评论回复"), comment.Text);
                if (!String.IsNullOrWhiteSpace(comment.Parent)) Section(comments, "回应的是", comment.Parent, true);
            }
            if (result.Comments.Count == 0) Section(comments, "暂无评论依据", "本次没有取得可展示的评论。回复只依据已读取的正文或视频内容。", true);
            foreach (TabPage page in tabs.TabPages) { var box = (RichTextBox)page.Controls[0]; box.SelectionStart = 0; box.ScrollToCaret(); }
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) Scale(new SizeF(graphics.DpiX / 96f, graphics.DpiY / 96f));
        }

        private RichTextBox Page(string title)
        {
            var page = new TabPage(title) { BackColor = Theme.Paper, Padding = new Padding(24, 22, 24, 16) };
            var box = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Paper, ForeColor = Theme.Ink, Font = body, DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical };
            page.Controls.Add(box); tabs.TabPages.Add(page); return box;
        }
        private void Section(RichTextBox box, string title, string text, bool muted = false, string empty = "")
        {
            text = (text ?? "").Trim(); if (text.Length == 0) text = empty; if (text.Length == 0) return;
            box.SelectionFont = heading; box.SelectionColor = Theme.Ink; box.AppendText(title + "\r\n\r\n");
            box.SelectionFont = muted ? small : body; box.SelectionColor = muted ? Theme.Muted : Theme.Ink;
            box.AppendText(text + "\r\n\r\n\r\n");
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) { heading.Dispose(); body.Dispose(); small.Dispose(); } }
    }
}
