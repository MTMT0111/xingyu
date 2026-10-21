using System;
using System.Drawing;
using System.Net.Http;
using System.Threading;
using System.Windows.Forms;

namespace ZhiyiDesktop
{
    internal sealed class ModelSettingsForm : Form
    {
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        internal string LastStatus;
        internal ModelSettingsForm(ApiClient api, string serviceUrl)
        {
            Text = "知意 · DeepSeek 设置"; ClientSize = new Size(480, 464); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; TopMost = true; StartPosition = FormStartPosition.CenterParent;
            Font = Theme.Font(10.5f, FontStyle.Regular); BackColor = Theme.Paper; AutoScaleMode = AutoScaleMode.None;
            Controls.Add(new Label { Text = "连接你的 DeepSeek Flash", Bounds = new Rectangle(24, 22, 430, 34), Font = Theme.Font(16, FontStyle.Bold), ForeColor = Theme.Ink });
            var state = new Label { Text = "正在检查本机配置…", Bounds = new Rectangle(24, 68, 432, 110), Padding = new Padding(12), BackColor = Theme.PaleGreen, ForeColor = Theme.Ink, Font = Theme.Font(11, FontStyle.Bold) };
            var key = new TextBox { Bounds = new Rectangle(24, 228, 432, 32), UseSystemPasswordChar = true, MaxLength = 256, AccessibleName = "DeepSeek API 密钥" };
            Controls.Add(new Label { Text = "新 API 密钥（已有密钥不会回显）", Bounds = new Rectangle(24, 196, 432, 26), ForeColor = Theme.Ink });
            Controls.Add(new Label { Text = "两种验证都会实际调用 DeepSeek。新密钥验证通过后才写入本机 .env；失败不会覆盖原配置。", Bounds = new Rectangle(24, 276, 432, 52), ForeColor = Theme.Muted });
            var save = new Button { Text = "验证并保存新密钥", Bounds = new Rectangle(24, 342, 432, 42), FlatStyle = FlatStyle.Flat, BackColor = Theme.Green, ForeColor = Color.White, Enabled = false };
            var verify = new Button { Text = "验证已保存密钥", Bounds = new Rectangle(24, 396, 432, 42), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Theme.Green, Enabled = false };
            Controls.Add(state); Controls.Add(key); Controls.Add(save); Controls.Add(verify);
            Shown += async delegate {
                try {
                    string message = await api.ModelSettings(serviceUrl, null, cancellation.Token);
                    if (!IsDisposed) { state.Text = message; state.ForeColor = message.StartsWith("验证成功") ? Theme.Green : Theme.Ink; }
                } catch (Exception error) { if (!IsDisposed) { state.Text = Describe(error); state.ForeColor = Color.Firebrick; } }
                finally { if (!IsDisposed) save.Enabled = verify.Enabled = true; }
            };
            EventHandler validate = async delegate(object sender, EventArgs args) {
                bool saved = sender == verify;
                if (!saved && String.IsNullOrWhiteSpace(key.Text)) { state.Text = "请填入新密钥，或点击「验证已保存密钥」。"; state.ForeColor = Color.Firebrick; return; }
                save.Enabled = verify.Enabled = false; key.ReadOnly = true;
                state.ForeColor = Theme.Ink; state.Text = saved ? "正在验证已保存密钥…" : "正在验证新密钥，通过后保存…";
                try {
                    string message = await api.ModelSettings(serviceUrl, saved ? null : key.Text.Trim(), cancellation.Token, saved);
                    if (!IsDisposed) { LastStatus = message; state.Text = message; state.ForeColor = Theme.Green; if (!saved) key.Clear(); }
                } catch (Exception error) { if (!IsDisposed) { LastStatus = Describe(error); state.Text = LastStatus; state.ForeColor = Color.Firebrick; } }
                finally { if (!IsDisposed) { save.Enabled = verify.Enabled = true; key.ReadOnly = false; } }
            };
            save.Click += validate; verify.Click += validate;
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) Scale(new SizeF(graphics.DpiX / 96f, graphics.DpiY / 96f));
        }
        private static string Describe(Exception error)
        {
            if (error is OperationCanceledException) return "验证等待超时。请重新打开设置检查状态，或再验证一次。";
            if (error is HttpRequestException) return "无法连接本机分析服务，请确认服务已启动及地址正确。";
            return error.Message;
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { cancellation.Cancel(); base.OnFormClosed(e); }
    }
}
