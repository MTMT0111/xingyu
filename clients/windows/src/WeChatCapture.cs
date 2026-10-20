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
    }

    internal static class WeChatCapture
    {
        private static readonly string[] IgnoredNames = {
            "微信", "WeChat", "Weixin", "搜索", "聊天", "通讯录", "收藏", "朋友圈", "小程序", "视频号",
            "发送", "语音输入", "表情", "文件", "截图", "更多", "关闭", "最小化", "最大化"
        };

        internal static WeChatWindow FindWindow()
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
                    if (name != "weixin" && name != "wechat") return true;
                    NativeMethods.RECT raw;
                    if (!NativeMethods.GetWindowRect(handle, out raw)) return true;
                    var bounds = Rectangle.FromLTRB(raw.Left, raw.Top, raw.Right, raw.Bottom);
                    if (bounds.Width < 400 || bounds.Height < 300) return true;
                    int length = NativeMethods.GetWindowTextLength(handle);
                    var title = new StringBuilder(Math.Max(1, length + 1));
                    NativeMethods.GetWindowText(handle, title, title.Capacity);
                    windows.Add(new WeChatWindow { Handle = handle, Bounds = bounds, Title = title.ToString() });
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            if (windows.Count == 0) return null;
            var foreground = NativeMethods.GetForegroundWindow();
            return windows.FirstOrDefault(item => item.Handle == foreground)
                ?? windows.OrderByDescending(item => item.Bounds.Width * item.Bounds.Height).First();
        }

        internal static CaptureResult Read(WeChatWindow window)
        {
            if (window == null || window.Handle == IntPtr.Zero) throw new InvalidOperationException("没有找到已打开的电脑版微信窗口。");
            var candidates = ReadAccessibleText(window);
            return new CaptureResult {
                Text = NormalizeLines(candidates.Select(item => item.Text), 6000),
                Recipient = PickRecipient(candidates, window.Bounds),
                Image = CaptureWindow(window.Bounds),
                Mime = "image/jpeg",
                FileName = "wechat-current-window.jpg",
                WindowBounds = window.Bounds
            };
        }

        internal static string NormalizeLines(IEnumerable<string> values, int maxCharacters)
        {
            var seen = new HashSet<string>();
            var result = new StringBuilder();
            foreach (var raw in values)
            {
                if (String.IsNullOrWhiteSpace(raw)) continue;
                var value = String.Join(" ", raw.Replace('\0', ' ').Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
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
                if (String.IsNullOrWhiteSpace(value) || value.Length > 40) return false;
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
                        string text = element.Current.Name;
                        object pattern;
                        if (element.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
                        {
                            string documentText = ((TextPattern)pattern).DocumentRange.GetText(6500);
                            if (!String.IsNullOrWhiteSpace(documentText)) text = documentText;
                        }
                        bool selected = false;
                        if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern)) selected = ((SelectionItemPattern)pattern).Current.IsSelected;
                        var raw = element.Current.BoundingRectangle;
                        result.Add(new TextCandidate { Text = text, Selected = selected, Bounds = Rectangle.FromLTRB((int)raw.Left, (int)raw.Top, (int)raw.Right, (int)raw.Bottom) });
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }

        private static byte[] CaptureWindow(Rectangle bounds)
        {
            using (var source = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb))
            {
                using (var graphics = Graphics.FromImage(source)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
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
