using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace ZhiyiDesktop
{
    internal sealed class WeChatWindow
    {
        public IntPtr Handle;
        public Rectangle Bounds;
        public string Title;
        public bool IsWebContent;
    }

    internal sealed class CaptureResult
    {
        public string Text;
        public string Recipient;
        public byte[] Image;
        public string Mime;
        public string FileName;
        public Rectangle WindowBounds;
    }

    internal sealed class TextCandidate
    {
        public string Text;
        public bool Selected;
        public Rectangle Bounds;
    }

    internal static class NativeMethods
    {
        internal delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")]
        internal static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);
        [DllImport("user32.dll")]
        internal static extern int GetWindowTextLength(IntPtr handle);
        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr handle, out RECT rect);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr handle);
        [DllImport("user32.dll")]
        internal static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        internal static extern bool UnregisterHotKey(IntPtr handle, int id);
    }

    internal static class WeChatCapture
    {
        private static readonly string[] IgnoredNames = {
            "微信", "WeChat", "Weixin", "搜索", "聊天", "通讯录", "收藏", "朋友圈", "小程序", "视频号",
            "发送", "语音输入", "表情", "文件", "截图", "更多", "关闭", "最小化", "最大化"
        };

        internal static WeChatWindow FindWindow(IntPtr preferred)
        {
            var windows = new List<WeChatWindow>();
            NativeMethods.EnumWindows(delegate(IntPtr handle, IntPtr parameter)
            {
                if (!NativeMethods.IsWindowVisible(handle)) return true;
                uint processId;
                NativeMethods.GetWindowThreadProcessId(handle, out processId);
                try
                {
                    var process = Process.GetProcessById((int)processId);
                    var name = process.ProcessName.ToLowerInvariant();
                    if (name != "weixin" && name != "wechat" && name != "wechatappex") return true;
                    NativeMethods.RECT raw;
                    if (!NativeMethods.GetWindowRect(handle, out raw)) return true;
                    var bounds = Rectangle.FromLTRB(raw.Left, raw.Top, raw.Right, raw.Bottom);
                    if (bounds.Width < 400 || bounds.Height < 300) return true;
                    int length = NativeMethods.GetWindowTextLength(handle);
                    var title = new StringBuilder(Math.Max(1, length + 1));
                    NativeMethods.GetWindowText(handle, title, title.Capacity);
                    windows.Add(new WeChatWindow { Handle = handle, Bounds = bounds, Title = title.ToString(), IsWebContent = name == "wechatappex" });
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            if (windows.Count == 0) return null;
            var foreground = NativeMethods.GetForegroundWindow();
            return windows.FirstOrDefault(item => item.Handle == foreground)
                ?? windows.FirstOrDefault(item => item.Handle == preferred)
                ?? windows.OrderByDescending(item => item.Bounds.Width * item.Bounds.Height).First();
        }

        internal static CaptureResult Read(WeChatWindow window)
        {
            if (window == null || window.Handle == IntPtr.Zero) throw new InvalidOperationException("没有找到已打开的电脑版微信窗口。");
            var candidates = ReadAccessibleText(window);
            if (!IsUnchangedAndForeground(window)) throw new InvalidOperationException("窗口已变化，请重新读取。");
            return new CaptureResult {
                Text = NormalizeLines(candidates.Where(item => window.IsWebContent || item.Bounds.Left >= window.Bounds.Left + window.Bounds.Width * 0.32).Select(item => item.Text), 6000),
                Recipient = window.IsWebContent ? "" : PickRecipient(candidates, window.Bounds),
                Image = CaptureArea(window.IsWebContent ? window.Bounds : ConversationBounds(window.Bounds)),
                Mime = "image/jpeg",
                FileName = "wechat-current-window.jpg",
                WindowBounds = window.Bounds
            };
        }

        internal static string ReadShareLink(WeChatWindow window)
        {
            if (window == null || !window.IsWebContent) return "";
            var root = AutomationElement.FromHandle(window.Handle);
            var documents = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            var links = new HashSet<string>();
            foreach (AutomationElement document in documents)
            {
                try
                {
                    if (document.Current.IsOffscreen) continue;
                    object pattern;
                    if (document.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
                    {
                        string value = ((ValuePattern)pattern).Current.Value;
                        if (IsShareLink(value)) links.Add(value);
                    }
                }
                catch (ElementNotAvailableException) { }
            }
            // Multiple visible documents cannot safely identify the user's intended share.
            return links.Count == 1 ? links.First() : "";
        }

        internal static bool IsShareLink(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0) return false;
            return uri.Host == "mp.weixin.qq.com" && (uri.AbsolutePath == "/s" || uri.AbsolutePath.StartsWith("/s/"))
                || uri.Host == "channels.weixin.qq.com" && (uri.AbsolutePath == "/web/pages/feed" || uri.AbsolutePath == "/finder-preview/pages/sph")
                || uri.Host == "weixin.qq.com" && uri.AbsolutePath.StartsWith("/sph/");
        }

        internal static string NormalizeLines(IEnumerable<string> values, int maxCharacters)
        {
            var seen = new HashSet<string>();
            var result = new StringBuilder();
            foreach (var raw in values)
            {
                if (String.IsNullOrWhiteSpace(raw)) continue;
                var value = String.Join(" ", raw.Replace('\0', ' ').Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
                if (value.IndexOf("MMUIRender", StringComparison.OrdinalIgnoreCase) >= 0 || value.Equals("Weixin", StringComparison.OrdinalIgnoreCase) || value.Equals("WeChat", StringComparison.OrdinalIgnoreCase)) continue;
                if (value.Length > 500 || value.Length < 1 || !seen.Add(value)) continue;
                if (result.Length + value.Length + 1 > maxCharacters) break;
                if (result.Length > 0) result.AppendLine();
                result.Append(value);
            }
            return result.ToString();
        }

        internal static string PickRecipient(IEnumerable<TextCandidate> candidates, Rectangle windowBounds)
        {
            Func<string, bool> useful = delegate(string value) {
                if (String.IsNullOrWhiteSpace(value) || value.Length > 40 || value.IndexOf("MMUIRender", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                return !IgnoredNames.Any(item => String.Equals(item, value.Trim(), StringComparison.OrdinalIgnoreCase));
            };
            var selected = candidates.FirstOrDefault(item => item.Selected && useful(item.Text));
            if (selected != null) return FirstLine(selected.Text);
            int topLimit = windowBounds.Top + Math.Max(160, windowBounds.Height / 4);
            var header = candidates.Where(item => useful(item.Text) && item.Bounds.Top >= windowBounds.Top && item.Bounds.Top < topLimit && item.Bounds.Left > windowBounds.Left + windowBounds.Width / 3)
                .OrderBy(item => item.Bounds.Top).ThenBy(item => item.Bounds.Left).FirstOrDefault();
            return header == null ? "" : FirstLine(header.Text);
        }

        private static string FirstLine(string value)
        {
            return value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value.Trim();
        }

        private static List<TextCandidate> ReadAccessibleText(WeChatWindow window)
        {
            var result = new List<TextCandidate>();
            try
            {
                var root = AutomationElement.FromHandle(window.Handle);
                var elements = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                int limit = Math.Min(elements.Count, 800);
                for (int index = 0; index < limit; index++)
                {
                    try
                    {
                        var element = elements[index];
                        if (element.Current.IsPassword || element.Current.IsOffscreen) continue;
                        // 文档范围可能含未显示的下一条推荐视频；只取当前可见叶子文字。
                        if (element.Current.ControlType != ControlType.Text && element.Current.ControlType != ControlType.ListItem) continue;
                        string text = element.Current.Name;
                        object pattern;
                        bool selected = false;
                        if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern)) selected = ((SelectionItemPattern)pattern).Current.IsSelected;
                        var raw = element.Current.BoundingRectangle;
                        var bounds = Rectangle.FromLTRB((int)raw.Left, (int)raw.Top, (int)raw.Right, (int)raw.Bottom);
                        if (Rectangle.Intersect(bounds, window.Bounds).IsEmpty) continue;
                        result.Add(new TextCandidate { Text = text, Selected = selected, Bounds = bounds });
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }

        internal static Rectangle ConversationBounds(Rectangle bounds)
        {
            // shortcut: 自动读取按主聊天窗口估算；文章和视频弹窗用框选指定真实范围。
            return Rectangle.FromLTRB(bounds.Left + (int)(bounds.Width * 0.32), bounds.Top + (int)(bounds.Height * 0.06), bounds.Right - 8, bounds.Bottom - (int)(bounds.Height * 0.12));
        }

        internal static bool IsUnchangedAndForeground(WeChatWindow window)
        {
            return CaptureIssue(window).Length == 0;
        }

        internal static string CaptureIssue(WeChatWindow window)
        {
            NativeMethods.RECT rect;
            if (window == null || !NativeMethods.IsWindowVisible(window.Handle)) return "原窗口不可见";
            if (NativeMethods.GetForegroundWindow() != window.Handle) return "原窗口已不在前台";
            if (!NativeMethods.GetWindowRect(window.Handle, out rect) || Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom) != window.Bounds) return "窗口位置或大小已变化";
            return "";
        }

        internal static byte[] CaptureArea(Rectangle captureBounds)
        {
            using (var source = Snapshot(captureBounds)) return EncodeImage(source);
        }

        internal static Bitmap Snapshot(Rectangle captureBounds)
        {
            captureBounds = Rectangle.Intersect(captureBounds, System.Windows.Forms.SystemInformation.VirtualScreen);
            if (captureBounds.Width < 16 || captureBounds.Height < 16) throw new InvalidOperationException("选区太小或已移出屏幕，请重新框选。");
            var image = new Bitmap(captureBounds.Width, captureBounds.Height, PixelFormat.Format24bppRgb);
            try
            {
                using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(captureBounds.Location, Point.Empty, captureBounds.Size, CopyPixelOperation.SourceCopy);
                return image;
            }
            catch { image.Dispose(); throw; }
        }

        internal static byte[] EncodeImage(Bitmap source)
        {
            {
                int width = source.Width, height = source.Height;
                const int maxSide = 1920;
                if (Math.Max(width, height) > maxSide)
                {
                    double scale = (double)maxSide / Math.Max(width, height);
                    width = (int)Math.Round(width * scale);
                    height = (int)Math.Round(height * scale);
                }
                using (var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                using (var graphics = Graphics.FromImage(resized))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source, 0, 0, width, height);
                    return EncodeJpeg(resized, 82L);
                }
            }
        }

        private static byte[] EncodeJpeg(Bitmap image, long quality)
        {
            using (var stream = new MemoryStream())
            {
                var codec = ImageCodecInfo.GetImageEncoders().First(item => item.MimeType == "image/jpeg");
                using (var parameters = new EncoderParameters(1))
                {
                    parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                    image.Save(stream, codec, parameters);
                }
                if (stream.Length > 4 * 1024 * 1024) throw new InvalidOperationException("当前微信窗口截图过大，请缩小窗口后重试。");
                return stream.ToArray();
            }
        }
    }
}
