using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ZhiyiDesktop
{
    internal sealed class ReplyResult
    {
        public string Summary;
        public string Reply;
        public string Note;
    }

    internal sealed class AttachmentData
    {
        public string Kind;
        public string Name;
        public string Mime;
        public byte[] Bytes;
    }

    internal sealed class ApiClient
    {
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };

        internal async Task<ReplyResult> Generate(string serviceUrl, string text, string context, int variant, AttachmentData attachment)
        {
            Uri root;
            if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out root) || (root.Scheme != Uri.UriSchemeHttp && root.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("服务地址需要以 http:// 或 https:// 开头。");
            if (root.Host != "127.0.0.1" && root.Host != "localhost" && root.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("非本机服务必须使用 HTTPS。");
            var payload = new Dictionary<string, object> {
                { "text", text ?? "" }, { "recipient", "auto" }, { "tone", "warm" }, { "mode", "live" }, { "variant", variant }
            };
            if (!String.IsNullOrWhiteSpace(context)) payload["context"] = context;
            if (attachment != null)
            {
                payload["attachment"] = new Dictionary<string, object> {
                    { "kind", attachment.Kind }, { "name", attachment.Name }, { "mime", attachment.Mime }, { "data", Convert.ToBase64String(attachment.Bytes) }
                };
            }
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
            using (var body = new StringContent(json.Serialize(payload), Encoding.UTF8, "application/json"))
            using (var response = await client.PostAsync(new Uri(root, "/api/reply"), body))
            {
                string raw = await response.Content.ReadAsStringAsync();
                Dictionary<string, object> value;
                try { value = json.Deserialize<Dictionary<string, object>>(raw); }
                catch { throw new InvalidOperationException("服务返回的内容无法读取。"); }
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException(GetString(value, "error", "生成失败，请检查本机服务。"));
                var result = new ReplyResult {
                    Summary = GetString(value, "content_summary", ""),
                    Reply = GetString(value, "reply", ""),
                    Note = GetString(value, "note", "发送前请确认内容。")
                };
                if (String.IsNullOrWhiteSpace(result.Reply)) throw new InvalidOperationException("服务没有返回可用回复。");
                return result;
            }
        }

        internal static AttachmentData FromFile(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var types = new Dictionary<string, string> {
                { ".jpg", "image/jpeg" }, { ".jpeg", "image/jpeg" }, { ".png", "image/png" }, { ".webp", "image/webp" },
                { ".mp4", "video/mp4" }, { ".webm", "video/webm" }, { ".mov", "video/quicktime" }
            };
            string mime;
            if (!types.TryGetValue(extension, out mime)) throw new InvalidOperationException("请选择 JPG、PNG、WebP、MP4、WebM 或 MOV 文件。");
            var info = new FileInfo(path);
            bool video = mime.StartsWith("video/", StringComparison.Ordinal);
            long limit = video ? 20L * 1024 * 1024 : 4L * 1024 * 1024;
            if (info.Length < 1 || info.Length > limit) throw new InvalidOperationException(video ? "视频需小于 20 MB，且不超过 60 秒。" : "图片需小于 4 MB。");
            return new AttachmentData { Kind = video ? "video" : "image", Name = info.Name, Mime = mime, Bytes = File.ReadAllBytes(path) };
        }

        private static string GetString(Dictionary<string, object> data, string key, string fallback)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) && value is string ? (string)value : fallback;
        }
    }
}
