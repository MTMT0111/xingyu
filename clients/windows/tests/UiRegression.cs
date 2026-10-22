using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;

internal sealed class SilentHost : Form
{
    protected override bool ShowWithoutActivation { get { return true; } }
    internal SilentHost()
    {
        Opacity = 0; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(0, 0, 1900, 1200); TopMost = false;
    }
}

internal static class UiRegression
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static Assembly assembly;
    private static int assertions, failures;
    private static Type Type(string name) { return assembly.GetType("ZhiyiDesktop." + name, true); }
    private static object Get(object target, string field) { return target.GetType().GetField(field, All).GetValue(target); }
    private static void Set(object target, string field, object value) { target.GetType().GetField(field, All).SetValue(target, value); }
    private static object Call(object target, string method, params object[] args) { return target.GetType().GetMethod(method, All).Invoke(target, args); }
    private static void Check(bool passed, string description)
    {
        assertions++; if (!passed) failures++;
        Console.WriteLine((passed ? "PASS " : "FAIL ") + description);
    }
    private static void Pump(Form form)
    {
        for (int i = 0; i < 4; i++) { form.PerformLayout(); Application.DoEvents(); }
    }
    private static Form Assistant()
    {
        // Avoid the bubble constructor: it starts a timer and discovers external windows.
        object bubble = FormatterServices.GetUninitializedObject(Type("BubbleForm"));
        return (Form)Activator.CreateInstance(Type("AssistantForm"), All, null, new[] { bubble }, null);
    }
    private static object Result()
    {
        object result = Activator.CreateInstance(Type("ReplyResult"), true);
        foreach (var field in result.GetType().GetFields(All)) if (field.FieldType == typeof(string)) field.SetValue(result, "");
        Set(result, "Reply", "这是回归检查的回复。"); Set(result, "Summary", "这是摘要。");
        Set(result, "Coverage", "回归检查资料"); Set(result, "Evidence", new string('字', 7200));
        Set(result, "Transcript", new string('文', 7200));
        Set(result, "RelatedQueries", new[] { "测试建议" });
        return result;
    }
    private static Rectangle In(Control container, Control child)
    {
        return container.RectangleToClient(child.RectangleToScreen(child.ClientRectangle));
    }
    private static void Fit(Form form, Rectangle area)
    {
        Type("Theme").GetMethod("FitToScreen", All).Invoke(null, new object[] { form, area });
        Pump(form);
    }
    private static void Prepare(Form form, SilentHost host, float dpi)
    {
        float actual;
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) actual = g.DpiX;
        form.Scale(new SizeF(dpi / actual, dpi / actual));
        form.TopMost = false; form.ShowInTaskbar = false; form.TopLevel = false;
        host.Controls.Add(form); form.Show(); Pump(form);
    }
    private static void State()
    {
        using (Form form = Assistant())
        {
            var source = (TextBox)Get(form, "source"); var reply = (TextBox)Get(form, "reply");
            var context = (TextBox)Get(form, "context"); var details = (LinkLabel)Get(form, "details");
            var recommendations = (TableLayoutPanel)Get(form, "recommendations");
            var related = (Button)Get(form, "related"); var copy = (Button)Get(form, "copy");
            object result = Result();
            foreach (string input in new[] { "", "原始补充消息" })
            {
                source.Text = input; Call(form, "DisplayResult", result);
                Check(source.Text == input && ((string)Get(Get(form, "currentResult"), "Evidence")).Length == 7200,
                    "DisplayResult preserves original input and 7200-character evidence (input length " + input.Length + ")");
                Check(reply.Text == (string)Get(result, "Reply") && !(bool)Get(form, "draftChanged"), "successful display resets draft marker");
                recommendations.Controls.Add(new Label { Text = "old recommendation" }); related.Enabled = true;
                source.Text = "编辑后的消息";
                Check((bool)Get(form, "draftChanged") && reply.Text == (string)Get(result, "Reply") && Object.ReferenceEquals(Get(form, "currentResult"), result), "source edit marks old draft and preserves its text/evidence");
                Check(details.Text == "上次分析依据" && copy.Text == "复制保留草稿" && !related.Enabled && recommendations.Controls.Count == 0, "source edit labels stale evidence and clears old recommendations");
                Call(form, "DisplayResult", result); context.Text = "关系备注 " + input.Length;
                Check((bool)Get(form, "draftChanged") && reply.Text == (string)Get(result, "Reply") && Object.ReferenceEquals(Get(form, "currentResult"), result), "relationship edit marks old draft and preserves its text/evidence");
            }
            int shown = 0, activated = 0;
            form.Shown += delegate { shown++; }; form.Activated += delegate { activated++; };
            Call(form, "Reveal", reply.Parent); Call(form, "Reveal", details); Application.DoEvents();
            Check(!form.Visible && shown == 0 && activated == 0, "Reveal on a never-shown form neither shows nor activates it");
        }
    }
    private static void Layout(SilentHost host, Size areaSize, float dpi)
    {
        string name = areaSize.Width + "x" + areaSize.Height + " @ " + dpi + " dpi";
        var area = new Rectangle(Point.Empty, areaSize);
        using (Form form = Assistant())
        {
            Prepare(form, host, dpi); Fit(form, area);
            Check(area.Contains(form.Bounds), "main form fits " + name + " (" + form.Bounds + ")");
            var generate = (Button)Get(form, "generate"); var copy = (Button)Get(form, "copy");
            Check(form.ClientRectangle.Contains(In(form, generate)) && form.ClientRectangle.Contains(In(form, copy)) && generate.Height > 20 && copy.Height > 20, "both footer buttons are reachable at " + name);
            object result = Result();
            Set(result, "Summary", string.Join("\r\n", new string[] { new string('长', 50), new string('长', 50), new string('长', 50), new string('长', 50), new string('长', 50), new string('长', 50), new string('长', 50), new string('长', 50) }));
            Call(form, "DisplayResult", result); Pump(form);
            var scroll = (Panel)Get(form, "scroll"); var reply = (TextBox)Get(form, "reply"); var details = (LinkLabel)Get(form, "details");
            scroll.AutoScrollPosition = Point.Empty; Pump(form);
            Rectangle before = In(scroll, reply.Parent);
            Call(form, "Reveal", reply.Parent); Pump(form);
            Rectangle after = In(scroll, reply.Parent); Rectangle visible = Rectangle.Intersect(scroll.ClientRectangle, after);
            Console.WriteLine("DETAIL " + name + " viewport=" + scroll.ClientRectangle + " before=" + before + " after=" + after + " scroll=" + scroll.AutoScrollPosition);
            Check(before.Top >= scroll.ClientRectangle.Bottom && after.Top < scroll.ClientRectangle.Bottom && visible.Height >= Math.Min(after.Height, Math.Max(40, scroll.ClientSize.Height - 60)), "Reveal(reply panel) scrolls a long-summary result into viewport at " + name);
            Call(form, "Reveal", details); Pump(form);
            Check(scroll.ClientRectangle.Contains(In(scroll, details)), "Reveal(details) makes retained-evidence action visible at " + name);
            int shown = 0, activated = 0; form.Shown += delegate { shown++; }; form.Activated += delegate { activated++; };
            form.Hide(); Call(form, "Reveal", reply.Parent); Call(form, "Reveal", details); Pump(form);
            Check(!form.Visible && shown == 0 && activated == 0, "Reveal preserves a hidden form at " + name);
        }
        using (Form evidence = (Form)Activator.CreateInstance(Type("EvidenceForm"), All, null, new[] { Result() }, null))
        {
            Prepare(evidence, host, dpi); Fit(evidence, area);
            Check(area.Contains(evidence.Bounds), "evidence form fits " + name);
            int buttons = 0;
            foreach (Control control in evidence.Controls) if (control is Button)
            {
                buttons++; Check(evidence.ClientRectangle.Contains(In(evidence, control)) && control.Height > 20, "evidence footer " + control.Text + " reachable at " + name);
            }
            Check(buttons == 2 && ((TabControl)Get(evidence, "tabs")).Height > 60, "evidence tabs retain useful space at " + name);
        }
        // A non-local service address is rejected before any request or credentials are read.
        using (Form settings = (Form)Activator.CreateInstance(Type("ModelSettingsForm"), All, null,
            new object[] { Activator.CreateInstance(Type("ApiClient"), true), "https://example.invalid" }, null))
        {
            Prepare(settings, host, dpi); Fit(settings, area);
            Check(area.Contains(settings.Bounds), "model settings fit " + name);
            Panel viewport = null;
            foreach (Control control in settings.Controls) if (control is Panel && ((Panel)control).AutoScroll) viewport = (Panel)control;
            Check(viewport != null, "model settings have a scroll viewport at " + name);
            if (viewport == null) return;
            int targets = 0;
            foreach (Control control in viewport.Controls) if (control is TextBox || control is Button)
            {
                viewport.ScrollControlIntoView(control); Pump(settings);
                Rectangle bounds = viewport.RectangleToClient(control.Parent.RectangleToScreen(control.Bounds));
                Rectangle visible = Rectangle.Intersect(viewport.ClientRectangle, bounds);
                Check(visible.Height >= control.Height && visible.Width >= Math.Min(control.Width, viewport.ClientSize.Width - SystemInformation.VerticalScrollBarWidth),
                    "settings input/action can be scrolled into view: " + (control is TextBox ? "API key" : control.Text) + " at " + name);
                if (control is TextBox) Check(((TextBox)control).UseSystemPasswordChar && ((TextBox)control).MaxLength == 256, "key remains masked and bounded at " + name);
                if (control is Button)
                {
                    ((Button)control).PerformClick(); Pump(settings);
                    Label feedback = null;
                    foreach (Control item in viewport.Controls) if (item is Label && (item.Text.StartsWith("请填入新密钥") || item.Text.StartsWith("请连接本机服务"))) feedback = (Label)item;
                    Rectangle shown = feedback == null ? Rectangle.Empty : Rectangle.Intersect(viewport.ClientRectangle, In(viewport, feedback));
                    Check(feedback != null && shown.Height == feedback.ClientSize.Height && shown.Width >= Math.Min(feedback.ClientSize.Width, viewport.ClientSize.Width - SystemInformation.VerticalScrollBarWidth), "validation feedback scrolls into viewport after " + control.Text + " at " + name);
                }
                targets++;
            }
            Check(targets == 3, "key and both validation buttons remain present at " + name);
        }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            assembly = Assembly.LoadFrom(args[0]); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            State();
            using (var host = new SilentHost())
            {
                host.Show(); Pump(host);
                foreach (float dpi in new[] { 96f, 144f, 192f }) foreach (Size area in new[] { new Size(1366, 768), new Size(900, 600), new Size(800, 480) }) Layout(host, area, dpi);
                host.Hide();
            }
        }
        catch (Exception error) { failures++; Console.WriteLine("HARNESS ERROR " + error); }
        Console.WriteLine("ASSERTIONS " + assertions + "; FAILURES " + failures);
        return failures == 0 ? 0 : 1;
    }
}
