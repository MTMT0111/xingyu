const MIME_BY_EXTENSION = {
  jpg: ['image', 'image/jpeg'],
  jpeg: ['image', 'image/jpeg'],
  png: ['image', 'image/png'],
  webp: ['image', 'image/webp'],
  mp4: ['video', 'video/mp4'],
  webm: ['video', 'video/webm'],
  mov: ['video', 'video/quicktime']
};

function codepointLength(value) {
  return Array.from(String(value || '')).length;
}

function normalizeBaseUrl(value) {
  const url = String(value || '').trim().replace(/\/+$/, '');
  const local = /^http:\/\/(?:127\.0\.0\.1|localhost)(?::[1-9]\d{0,4})?$/.test(url);
  const secure = /^https:\/\/[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?(?::[1-9]\d{0,4})?$/i.test(url);
  if (!local && !secure) throw new Error('服务地址需为 HTTPS 根地址；开发者工具可用 http://127.0.0.1:4173。');
  return url;
}

function base64Ascii(value) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
  let output = '';
  for (let index = 0; index < value.length; index += 3) {
    const a = value.charCodeAt(index);
    const b = index + 1 < value.length ? value.charCodeAt(index + 1) : 0;
    const c = index + 2 < value.length ? value.charCodeAt(index + 2) : 0;
    if (a > 127 || b > 127 || c > 127) throw new Error('访问口令只能使用无空格 ASCII 字符。');
    const bits = (a << 16) | (b << 8) | c;
    output += alphabet[(bits >> 18) & 63] + alphabet[(bits >> 12) & 63]
      + (index + 1 < value.length ? alphabet[(bits >> 6) & 63] : '=')
      + (index + 2 < value.length ? alphabet[bits & 63] : '=');
  }
  return output;
}

function authorizationHeaders(password) {
  if (!password) return {};
  if (!/^[\x21-\x7E]{16,128}$/.test(password)) throw new Error('访问口令需为 16–128 位无空格 ASCII 字符。');
  return { Authorization: `Basic ${base64Ascii(`trial:${password}`)}` };
}

function classifyFile(file) {
  const path = String(file.path || file.tempFilePath || '');
  if (!path) throw new Error('没有读取到文件路径，请重新选择。');
  const fallbackName = path.split(/[\\/]/).pop() || '';
  const name = String(file.name || fallbackName || '微信文件').slice(0, 120);
  const extension = name.includes('.') ? name.split('.').pop().toLowerCase() : '';
  const definition = MIME_BY_EXTENSION[extension];
  if (!definition) throw new Error('请选择 JPG、PNG、WebP、MP4、WebM 或 MOV 文件。');
  const size = Number(file.size);
  if (!Number.isFinite(size) || size <= 0) throw new Error('没有读取到有效文件。');
  const [kind, mime] = definition;
  const expectedKind = file.fileType || file.type;
  if (['image', 'video'].includes(expectedKind) && expectedKind !== kind) throw new Error('文件类型与扩展名不一致。');
  const maxBytes = (kind === 'image' ? 4 : 20) * 1024 * 1024;
  if (size > maxBytes) throw new Error(kind === 'image' ? '图片不能超过 4 MB。' : '视频不能超过 20 MB。');
  return { path, name, size, kind, mime };
}

function validateMessage(text, hasAttachment) {
  const clean = String(text || '').trim();
  if (!clean && !hasAttachment) throw new Error('请粘贴文字、公众号链接，或选择图片和视频。');
  if (codepointLength(clean) > 6000) throw new Error('文字不能超过 6000 字。');
  return clean;
}

function validateResult(value, evidenceKind) {
  const validMeanings = Array.isArray(value && value.possible_meanings)
    && value.possible_meanings.length >= 1
    && value.possible_meanings.length <= 2
    && value.possible_meanings.every(item => typeof item === 'string' && item.trim());
  if (!value || typeof value.content_summary !== 'string' || !value.content_summary.trim()
    || !validMeanings || typeof value.reply_tip !== 'string' || typeof value.reply !== 'string' || !value.reply.trim()) {
    throw new Error('这次返回的内容不完整，请重试。');
  }
  if (evidenceKind && (value.generation_source !== 'llm' || !value.evidence || value.evidence.kind !== evidenceKind
    || evidenceKind === 'video' && !['transcribed', 'silent'].includes(value.evidence.audio_status))) {
    throw new Error(evidenceKind === 'article' ? '这次没有读取到公众号正文，请粘贴正文或重试。' : '这次没有完成媒体分析，请保留文件后重试。');
  }
  return value;
}

module.exports = { authorizationHeaders, classifyFile, codepointLength, normalizeBaseUrl, validateMessage, validateResult };
