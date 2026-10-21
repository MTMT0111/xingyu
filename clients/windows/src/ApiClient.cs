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
        public string Transcript;
        public string Visual;
        public string Tip;
        public string Segments;
        public string Analysis, CommentAnalysis, Caveats, Timing;
        public string[] Meanings = new string[0];
        public string[] RelatedQueries = new string[0];
        public readonly List<CommentEvidence> Comments = new List<CommentEvidence>();
    }

    internal sealed class CommentEvidence
    {
        public string Text, Parent, Likes, Reference;
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
        public string share_url { get; set; }
        public string captured_at { get; set; }
        public override string ToString() {
            DateTime time;
            string recent = DateTime.TryParse(captured_at, out time) ? " · " + time.ToLocalTime().ToString("MM-dd HH:mm") : "";
            return author + " · " + duration + " 秒" + recent + "\r\n" + title;
        }
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
            return await Videos(serviceUrl, "/api/channels/search?q=" + Uri.EscapeDataString(keyword), cancellation);
        }

        internal async Task<VideoChoice[]> Recent(string serviceUrl, CancellationToken cancellation)
        {
            return await Videos(serviceUrl, "/api/channels/recent", cancellation);
        }

        private async Task<VideoChoice[]> Videos(string serviceUrl, string path, CancellationToken cancellation)
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(95) })
            using (var response = await client.GetAsync(new Uri(ServiceRoot(serviceUrl), path), cancellation))
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

        internal async Task<string> ModelSettings(string serviceUrl, string key, CancellationToken cancellation)
        {
            var root = ServiceRoot(serviceUrl);
            if (root.Host != "127.0.0.1" && root.Host != "localhost") throw new InvalidOperationException("请连接本机服务后设置密钥。");
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(22) })
            using (var request = new HttpRequestMessage(key == null ? HttpMethod.Get : HttpMethod.Post, new Uri(root, "/api/local/model")))
            {
                if (key != null) {
                    request.Headers.Add("X-Zhiyi-Local-Config", "1");
                    request.Content = new StringContent(json.Serialize(new { apiKey = key }), Encoding.UTF8, "application/json");
                }
                using (var response = await client.SendAsync(request, cancellation)) {
                    var data = json.Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync());
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException(GetString(data, "error", "设置失败，原配置保留。"));
                    return key == null ? (data.ContainsKey("configured") && Object.Equals(data["configured"], true) ? "已配置 · " : "未配置 · ") + GetString(data, "model", "deepseek-flash") : GetString(data, "message", "已保存。");
                }
            }
        }

        private async Task Monitor(string serviceUrl, string id, IProgress<string> progress, CancellationToken cancellation)
        {
            if (progress == null) return;
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) }) while (!cancellation.IsCancellationRequested) {
                try {
                    await Task.Delay(1200, cancellation);
                    using (var response = await client.GetAsync(new Uri(ServiceRoot(serviceUrl), "/api/local/progress?id=" + id), cancellation)) {
                        if (!response.IsSuccessStatusCode) continue;
                        var data = json.Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync());
                        if (Object.Equals(data["done"], true)) return;
                        if (!cancellation.IsCancellationRequested) progress.Report(GetString(data, "stage", "正在分析") + " · " + Math.Round(Convert.ToDouble(data["elapsed_ms"]) / 1000) + " 秒");
                    }
                } catch (OperationCanceledException) { if (cancellation.IsCancellationRequested) return; }
                catch { /* Progress is optional; never interrupt the actual request. */ }
            }
        }

        internal async Task<ReplyResult> Generate(string serviceUrl, string text, string context, int variant, AttachmentData attachment, VideoChoice channel, CancellationToken cancellation, IProgress<string> progress = null, bool refresh = false)
        {
            string id = Guid.NewGuid().ToString("N");
            using (var monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation)) {
                var monitor = Monitor(serviceUrl, id, progress, monitorCancellation.Token);
                try { return await GenerateCore(serviceUrl, text, context, variant, attachment, channel, cancellation, id, refresh); }
                finally { monitorCancellation.Cancel(); }
            }
        }

        private async Task<ReplyResult> GenerateCore(string serviceUrl, string text, string context, int variant, AttachmentData attachment, VideoChoice channel, CancellationToken cancellation, string id, bool refresh)
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
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(95), DefaultRequestHeaders = { { "X-Request-Id", id }, { "X-Refresh-Evidence", refresh ? "1" : "0" } } })
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
                result.Tip = GetString(value, "reply_tip", "");
                result.Meanings = Strings(value, "possible_meanings");
                result.RelatedQueries = Strings(value, "related_queries");
                result.CommentAnalysis = GetString(value, "comment_analysis", "");
                result.Caveats = GetString(value, "caveats", "");
                object analysisValue;
                var analysis = value.TryGetValue("analysis", out analysisValue) ? analysisValue as System.Collections.ArrayList : null;
                if (analysis != null) foreach (var item in analysis) {
                    var point = item as Dictionary<string, object>;
                    string reference = GetString(point, "source", "");
                    result.Analysis += GetString(point, "point", "") + "\r\n依据：" + (reference == "video" ? "视频讲述与采样画面" : reference == "article" ? "公众号正文" : reference == "message" ? "输入的消息" : "评论 " + reference) + "\r\n\r\n";
                }
                object metaValue;
                var meta = value.TryGetValue("meta", out metaValue) ? metaValue as Dictionary<string, object> : null;
                if (meta != null && meta.ContainsKey("total_ms")) result.Timing = Math.Round(Convert.ToDouble(meta["total_ms"]) / 1000, 1) + " 秒" + (meta.ContainsKey("evidence_cache_hit") && Object.Equals(meta["evidence_cache_hit"], true) ? " · 已复用本次视频资料" : "");
                object evidenceValue;
                var evidence = value.TryGetValue("evidence", out evidenceValue) ? evidenceValue as Dictionary<string, object> : null;
                result.Evidence = GetString(evidence, "visible_text", "");
                result.Coverage = GetString(evidence, "coverage", "");
                string transcript = GetString(evidence, "transcript", "");
                result.Transcript = transcript;
                result.Visual = GetString(evidence, "visual_summary", "") + "\r\n" + GetString(evidence, "visible_text", "");
                if (transcript.Length > 0) result.Evidence += "\r\n【视频讲述】\r\n" + transcript;
                object commentValue;
                var comments = evidence != null && evidence.TryGetValue("comments", out commentValue) ? commentValue as System.Collections.ArrayList : null;
                if (comments != null) foreach (var item in comments)
                {
                    var comment = item as Dictionary<string, object>;
                    object likes; string count = comment != null && comment.TryGetValue("likes", out likes) && likes != null ? Convert.ToString(likes) + " 赞" : "未提供点赞数";
                    string parent = GetString(comment, "reply_to", "");
                    result.Comments.Add(new CommentEvidence { Text = GetString(comment, "text", ""), Parent = parent, Likes = count, Reference = GetString(comment, "ref", "") });
                    result.Evidence += "\r\n【" + count + "】" + (parent.Length > 0 ? "回复「" + parent + "」：" : "") + GetString(comment, "text", "");
                }
                object parts;
                var segments = evidence != null && evidence.TryGetValue("segments", out parts) ? parts as System.Collections.ArrayList : null;
                if (segments != null) foreach (var item in segments) {
                    var part = item as Dictionary<string, object>;
                    if (part == null) continue;
                    result.Evidence += "\r\n\r\n【分段 " + part["start"] + "–" + part["end"] + " 秒】\r\n" + GetString(part, "summary", "");
                    result.Segments += part["start"] + "–" + part["end"] + " 秒\r\n" + GetString(part, "summary", "") + "\r\n\r\n";
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

        private static string[] Strings(Dictionary<string, object> data, string key)
        {
            object value;
            var list = new List<string>();
            var values = data.TryGetValue(key, out value) ? value as System.Collections.ArrayList : null;
            if (values != null) foreach (var item in values) if (item is string && !String.IsNullOrWhiteSpace((string)item)) list.Add((string)item);
            return list.ToArray();
        }
    }
}
