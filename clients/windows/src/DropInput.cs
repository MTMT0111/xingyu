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
            }
            throw new InvalidOperationException("微信没有交出卡片的原链接。请在微信点开卡片一次，再点「从微信接入」；不用等视频播完。");
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
            data.SetData(DataFormats.UnicodeText, "只有视频标题，没有原链接");
            bool refused = false; try { Text(data); } catch (InvalidOperationException) { refused = true; }
            if (!refused) throw new Exception("不能把拖入卡片的标题当成已读取的视频；普通消息使用粘贴入口");
        }
    }
}
