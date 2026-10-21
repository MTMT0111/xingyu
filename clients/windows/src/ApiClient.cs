using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ZhiyiDesktop
{
    internal sealed class ReplyResult
    {
        public string Summary;
        public string Reply;
        public string Note;
        public string Evidence;
        public string Coverage;
    }

    internal sealed class AttachmentData
    {
        public string Kind;
        public string Name;
        public string Mime;
        public byte[] Bytes;
        public string FilePath;
    }

    internal sealed class VideoChoice
    {
        public string object_id { get; set; }
        public string nonce_id { get; set; }
        public string title { get; set; }
        public string author { get; set; }
        public double duration { get; set; }
        public override string ToString() { return author + " · " + duration + " 秒\r\n" + title; }
    }

    internal sealed class ApiClient
    {
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };

        private static Uri ServiceRoot(string serviceUrl)
        {
            Uri root;
            if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out root) || (root.Scheme != Uri.UriSchemeHttp && root.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("服务地址需要以 http:// 或 https:// 开头。");
            if (root.Host != "127.0.0.1" && root.Host != "localhost" && root.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("非本机服务必须使用 HTTPS。");
            return root;
        }

        internal async Task<VideoChoice[]> Search(string serviceUrl, string keyword, CancellationToken cancellation)
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(95) })
            using (var response = await client.GetAsync(new Uri(ServiceRoot(serviceUrl), "/api/channels/search?q=" + Uri.EscapeDataString(keyword)), cancellation))
            {
                string raw = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException(GetString(json.Deserialize<Dictionary<string, object>>(raw), "error", "视频号搜索暂不可用。"));
                return json.Deserialize<SearchResponse>(raw).items ?? new VideoChoice[0];
            }
        }
        private sealed class SearchResponse { public VideoChoice[] items { get; set; } }
        internal static void SelfTest()
        {
            var parsed = new JavaScriptSerializer().Deserialize<SearchResponse>("{\"items\":[{\"object_id\":\"18446744073709551615\",\"nonce_id\":\"test\",\"title\":\"散步\",\"author\":\"作者\",\"duration\":38}]}");
            if (parsed.items.Length != 1 || parsed.items[0].object_id != "18446744073709551615" || !parsed.items[0].ToString().Contains("作者")) throw new Exception("搜索结果解析失败");
        }

        internal async Task<ReplyResult> Generate(string serviceUrl, string text, string context, int variant, AttachmentData attachment, VideoChoice channel, CancellationToken cancellation)
        {
            Uri root = ServiceRoot(serviceUrl);
            var payload = new Dictionary<string, object> {
                { "text", text ?? "" }, { "recipient", "auto" }, { "tone", "warm" }, { "mode", "live" }, { "variant", variant }
            };
            if (!String.IsNullOrWhiteSpace(context)) payload["context"] = context;
            if (channel != null) payload["channel"] = new Dictionary<string, object> { { "object_id", channel.object_id }, { "nonce_id", channel.nonce_id } };
            if (attachment != null && attachment.FilePath != null)
            {
                using (var upload = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
                using (var file = File.OpenRead(attachment.FilePath))
                using (var content = new StreamContent(file))
                using (var response = await upload.PostAsync(new Uri(root, "/api/video?name=" + Uri.EscapeDataString(attachment.Name)), content, cancellation))
                {
                    var uploaded = json.Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync());
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException(GetString(uploaded, "error", "视频上传失败。"));
                    payload["videoUpload"] = GetString(uploaded, "id", "");
                }
            }
            else if (attachment != null)
            {
                payload["attachment"] = new Dictionary<string, object> {
                    { "kind", attachment.Kind }, { "name", attachment.Name }, { "mime", attachment.Mime }, { "data", Convert.ToBase64String(attachment.Bytes) }
                };
            }
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(95) })
            using (var body = new StringContent(json.Serialize(payload), Encoding.UTF8, "application/json"))
            using (var response = await client.PostAsync(new Uri(root, "/api/reply"), body, cancellation))
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
                object evidenceValue;
                var evidence = value.TryGetValue("evidence", out evidenceValue) ? evidenceValue as Dictionary<string, object> : null;
                result.Evidence = GetString(evidence, "visible_text", "");
                result.Coverage = GetString(evidence, "coverage", "");
                string transcript = GetString(evidence, "transcript", "");
                if (transcript.Length > 0) result.Evidence += "\r\n【视频讲述】\r\n" + transcript;
                object commentValue;
                var comments = evidence != null && evidence.TryGetValue("comments", out commentValue) ? commentValue as System.Collections.ArrayList : null;
                if (comments != null) foreach (var item in comments)
                {
                    var comment = item as Dictionary<string, object>;
                    object likes; string count = comment != null && comment.TryGetValue("likes", out likes) && likes != null ? Convert.ToString(likes) + " 赞" : "未提供点赞数";
                    string parent = GetString(comment, "reply_to", "");
                    result.Evidence += "\r\n【" + count + "】" + (parent.Length > 0 ? "回复「" + parent + "」：" : "") + GetString(comment, "text", "");
                }
                object parts;
                var segments = evidence != null && evidence.TryGetValue("segments", out parts) ? parts as System.Collections.ArrayList : null;
                if (segments != null) foreach (var item in segments) {
                    var part = item as Dictionary<string, object>;
                    if (part == null) continue;
                    result.Evidence += "\r\n\r\n【分段 " + part["start"] + "–" + part["end"] + " 秒】\r\n" + GetString(part, "summary", "");
                }
                result.Evidence = "依据优先级：原文和视频讲述为主，评论辅助理解。点赞是热度，不是可信度；下面按取得的样本排序。\r\n\r\n" + result.Evidence;
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
            long limit = video ? 512L * 1024 * 1024 : 4L * 1024 * 1024;
            if (info.Length < 1 || info.Length > limit) throw new InvalidOperationException(video ? "视频需小于 512 MB，长视频会分段处理。" : "图片需小于 4 MB。");
            return new AttachmentData { Kind = video ? "video" : "image", Name = info.Name, Mime = mime, FilePath = video ? path : null, Bytes = video ? null : File.ReadAllBytes(path) };
        }

        private static string GetString(Dictionary<string, object> data, string key, string fallback)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) && value is string ? (string)value : fallback;
        }
    }
}
