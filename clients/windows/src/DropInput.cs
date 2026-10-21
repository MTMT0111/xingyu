using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ZhiyiDesktop
{
    internal static class DropInput
    {
        private static readonly string[] TextFormats = { DataFormats.UnicodeText, DataFormats.Text, "UniformResourceLocatorW", "UniformResourceLocator", DataFormats.Html };
        internal static bool Accepts(IDataObject data) {
            if (data == null) return false;
            if (data.GetDataPresent(DataFormats.FileDrop)) return true;
            foreach (var format in TextFormats) if (data.GetDataPresent(format)) return true;
            return false;
        }
        internal static string Text(IDataObject data) {
            string fallback = null;
            foreach (var format in TextFormats) {
                if (!data.GetDataPresent(format)) continue;
                object value = data.GetData(format);
                var stream = value as MemoryStream;
                string text = value as string;
                if (stream != null && stream.Length <= 128 * 1024) text = (format.EndsWith("W") ? Encoding.Unicode : Encoding.UTF8).GetString(stream.ToArray());
                if (text == null || text.Length > 128 * 1024) continue;
                text = WebUtility.HtmlDecode(text).Trim('\0', ' ', '\r', '\n');
                foreach (Match match in Regex.Matches(text, "https://[^\\s<>\"']+")) {
                    string url = match.Value.TrimEnd('。', '，', ')', '）');
                    if (WeChatCapture.IsShareLink(url)) return url;
                }
                if (fallback == null && format != DataFormats.Html && text.Length <= 6000 && !text.Contains("\0") && !text.TrimStart().StartsWith("<")) fallback = text;
            }
            if (fallback != null) return fallback;
            throw new InvalidOperationException("这个微信卡片没有交出可读的视频文件或分享链接；可复制原链接再粘贴，不能只用封面生成。");
        }
        internal static void SelfTest() {
            var data = new DataObject(); data.SetData(DataFormats.Html, "<a href=\"https://mp.weixin.qq.com/s/test?a=1&amp;b=2\">文章</a>");
            if (Text(data) != "https://mp.weixin.qq.com/s/test?a=1&b=2") throw new Exception("拖入HTML链接识别失败");
            data.SetData(DataFormats.UnicodeText, "只有标题的纯文本");
            if (Text(data) != "https://mp.weixin.qq.com/s/test?a=1&b=2") throw new Exception("真实链接必须优先于卡片标题");
            data = new DataObject(); data.SetData("UniformResourceLocatorW", new MemoryStream(Encoding.Unicode.GetBytes("https://weixin.qq.com/sph/test\0")));
            if (Text(data) != "https://weixin.qq.com/sph/test") throw new Exception("拖入URL流识别失败");
            data = new DataObject(); data.SetData("PrivateWeChatCard", "unknown");
            if (Accepts(data)) throw new Exception("不应声称能读取未知微信卡片格式");
        }
    }
}
