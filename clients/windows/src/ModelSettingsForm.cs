using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ZhiyiDesktop
{
    internal sealed class ModelSettingsForm : Form
    {
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        internal ModelSettingsForm(ApiClient api, string serviceUrl)
        {
            Text = "知意 · DeepSeek 设置"; ClientSize = new Size(480, 310); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; TopMost = true; StartPosition = FormStartPosition.CenterParent;
            Font = Theme.Font(10.5f, FontStyle.Regular); BackColor = Theme.Paper; AutoScaleMode = AutoScaleMode.None;
            Controls.Add(new Label { Text = "连接你的 DeepSeek Flash", Bounds = new Rectangle(24, 22, 430, 34), Font = Theme.Font(16, FontStyle.Bold), ForeColor = Theme.Ink });
            var state = new Label { Text = "正在检查本机配置…", Bounds = new Rectangle(24, 66, 432, 48), ForeColor = Theme.Muted };
            var key = new TextBox { Bounds = new Rectangle(24, 130, 432, 32), UseSystemPasswordChar = true, MaxLength = 256, AccessibleName = "DeepSeek API 密钥" };
            Controls.Add(new Label { Text = "API 密钥（更换时填入，不回显已保存的密钥）", Bounds = new Rectangle(24, 105, 432, 24), ForeColor = Theme.Ink });
            Controls.Add(new Label { Text = "保存时进行一次小额验证调用。密钥仅存本机服务的 .env；后续分析立即生效。", Bounds = new Rectangle(24, 175, 432, 54), ForeColor = Theme.Muted });
            var save = new Button { Text = "验证并保存", Bounds = new Rectangle(24, 245, 432, 42), FlatStyle = FlatStyle.Flat, BackColor = Theme.Green, ForeColor = Color.White };
            Controls.Add(state); Controls.Add(key); Controls.Add(save);
            Shown += async delegate {
                try { state.Text = await api.ModelSettings(serviceUrl, null, cancellation.Token); }
                catch (Exception error) { if (!IsDisposed) state.Text = error.Message; }
            };
            save.Click += async delegate {
                if (String.IsNullOrWhiteSpace(key.Text)) { state.Text = "填入密钥后再保存；留空不会清除原配置。"; return; }
                save.Enabled = false; state.Text = "正在验证密钥，请稍候…";
                try { state.Text = await api.ModelSettings(serviceUrl, key.Text.Trim(), cancellation.Token); key.Clear(); }
                catch (Exception error) { if (!IsDisposed) state.Text = error.Message; }
                finally { if (!IsDisposed) save.Enabled = true; }
            };
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) Scale(new SizeF(graphics.DpiX / 96f, graphics.DpiY / 96f));
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { cancellation.Cancel(); base.OnFormClosed(e); }
    }
}
