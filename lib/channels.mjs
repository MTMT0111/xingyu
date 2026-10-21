import { setTimeout as delay } from 'node:timers/promises';
import { findWechatArticleUrl } from './article.mjs';
import { Parser } from 'htmlparser2';
import { storeVideoStream, VIDEO_TIMEOUT_MS } from './video-file.mjs';

const fail = (code, message) => Object.assign(new Error(message), { channelsCode: code });
const clean = (text, max) => typeof text === 'string' ? [...text.trim()].slice(0, max).join('') : '';

export function classifyShare(text) {
  const links = new Map();
  const article = findWechatArticleUrl(text);
  if (article) links.set(article, 'article');
  for (const match of text.matchAll(/https?:\/\/[^\s<>"'`]+/gi)) {
    const raw = match[0].replace(/[，。！？、；：）】》」』,.!?;:)\]}]+$/u, '');
    let url;
    try { url = new URL(raw); } catch { continue; }
    if (!['weixin.qq.com', 'channels.weixin.qq.com'].includes(url.hostname)) continue;
    const share = url.hostname === 'weixin.qq.com' && /^\/sph\/[\w-]+\/?$/.test(url.pathname);
    const page = url.hostname === 'channels.weixin.qq.com' &&
      ((url.pathname === '/web/pages/feed' && url.searchParams.has('oid')) ||
       (url.pathname === '/finder-preview/pages/sph' && url.searchParams.has('id')));
    if (url.protocol !== 'https:' || url.port || url.username || url.password || (!share && !page))
      throw fail('INVALID_LINK', '未识别到有效的视频号分享链接，请复制原始分享链接。');
    url.hash = '';
    links.set(url.href, 'channels');
  }
  if (links.size > 1) throw fail('MULTIPLE_LINKS', '一次请只分析一条文章或视频链接，避免把不同内容混在一起。');
  const entry = [...links.entries()][0];
  return entry ? { kind: entry[1], url: entry[0] } : { kind: 'text' };
}

function bridgeOrigin(value) {
  if (!value) throw fail('NOT_CONFIGURED', '已识别视频号链接；本机微信内容连接器尚未接通，暂时不能读取视频和评论。没有根据标题生成回复。');
  let url;
  try { url = new URL(value); } catch { /* validate below */ }
  if (!url || url.protocol !== 'http:' || url.hostname !== '127.0.0.1' || url.username || url.password || url.pathname !== '/' || url.search || url.hash)
    throw fail('CONFIG', '微信内容连接器必须是本机 http://127.0.0.1:端口 地址。');
  return url;
}

async function boundedResponse(response, maxBytes, signal) {
  const reader = response.body?.getReader();
  const cancel = () => { if (reader) void reader.cancel().catch(() => {}); };
  signal.addEventListener('abort', cancel, { once: true });
  try {
    signal.throwIfAborted();
    if (!response.ok || response.redirected || !reader) throw fail('UNAVAILABLE', '微信内容连接器未就绪或会话已失效，请检查连接状态。');
    if (Number(response.headers.get('content-length')) > maxBytes) throw fail('TOO_LARGE', '内容超过本次读取上限，未生成回复。');
    const chunks = [];
    let size = 0;
    while (true) {
      const { value, done } = await reader.read();
      signal.throwIfAborted();
      if (done) break;
      size += value.byteLength;
      if (size > maxBytes) throw fail('TOO_LARGE', '内容超过本次读取上限，未生成回复。');
      chunks.push(value);
    }
    return Buffer.concat(chunks);
  } finally { signal.removeEventListener('abort', cancel); if (reader) await reader.cancel().catch(() => {}); }
}

function channelClient({ config, fetchImpl = fetch, signal = new AbortController().signal }) {
  const origin = bridgeOrigin(config.channelsBridgeUrl);
  if (config.publicOrigin) throw fail('LOCAL_ONLY', '当前为公网演示服务，不能使用你电脑里的私人微信会话。请在本机助手分析。');
  const combinedSignal = AbortSignal.any([signal, AbortSignal.timeout(VIDEO_TIMEOUT_MS)]);
  async function request(path, params, binary = false) {
    const requestSignal = binary ? combinedSignal : AbortSignal.any([combinedSignal, AbortSignal.timeout(90000)]);
    const target = new URL(path, origin);
    target.search = new URLSearchParams(params).toString();
    let data;
    try {
      const response = await fetchImpl(target.href, {
        signal: requestSignal, redirect: 'error',
        headers: config.channelsBridgeToken ? { 'X-Local-Auth': config.channelsBridgeToken } : {}
      });
      if (binary) {
        if (!response.ok || response.redirected || !response.body) throw fail('UNAVAILABLE', '视频下载失败，未生成回复。');
        return await storeVideoStream(response.body, combinedSignal);
      }
      const bytes = await boundedResponse(response, 2 * 1024 * 1024, requestSignal);
      // Video IDs and decode keys can exceed JS's safe integer precision.
      data = JSON.parse(bytes.toString('utf8'), (key, value, context) => typeof value === 'number' && !Number.isSafeInteger(value) ? context.source : value);
    } catch (error) {
      if (requestSignal.aborted) throw fail('CANCELLED', '视频读取已取消或超时，已有内容保留。');
      if (error.channelsCode || error.mediaCode) throw error;
      throw fail('UNAVAILABLE', '未能连接本机微信内容服务，或返回内容无法读取。');
    }
    if (data.code !== 0) throw fail('UNAVAILABLE', '微信内容连接器未返回可用内容；请在电脑版微信打开视频号首页后重试。');
    let result = data.data;
    if (result?.errCode !== undefined) {
      if (result.errCode !== 0) throw fail('RESTRICTED', '微信未允许读取这条内容，无法根据链接完成分析。');
      result = result.data;
    }
    if (!result || typeof result !== 'object') throw fail('INVALID_RESPONSE', '微信内容服务返回的资料不完整。');
    return result;
  }
  return { request, signal: combinedSignal };
}

export function validateChannelSelection(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value) ||
      typeof value.object_id !== 'string' || !/^\d{1,20}$/.test(value.object_id) || BigInt(value.object_id) > 18446744073709551615n ||
      typeof value.nonce_id !== 'string' || !/^[A-Za-z0-9_+\-=/]{1,256}$/.test(value.nonce_id))
    throw fail('INVALID_SELECTION', '视频选择已失效，请重新搜索并选择。');
  return { object_id: value.object_id, nonce_id: value.nonce_id };
}

export async function searchChannels(keyword, options) {
  if (typeof keyword !== 'string' || !keyword.trim() || Buffer.byteLength(keyword.trim()) > 100)
    throw fail('INVALID_QUERY', '请输入简短的视频标题或关键词（中文最多约 33 字）。');
  const { request } = channelClient(options);
  const result = await request('/api/channels/feed/search', { keyword: keyword.trim() });
  if (!Array.isArray(result.objectList)) throw fail('INVALID_RESPONSE', '未取得视频搜索结果，请确认视频号首页已连接。');
  const items = [];
  for (const video of result.objectList.slice(0, 12)) {
    let selection;
    try { selection = validateChannelSelection({ object_id: video.id, nonce_id: video.objectNonceId }); } catch { continue; }
    let title = '';
    new Parser({ ontext: text => { title += text; } }).end(clean(video.objectDesc?.description, 2000));
    items.push({ ...selection, title: clean(title, 400), author: clean(video.contact?.nickname, 60), duration: Number(video.objectDesc?.media?.[0]?.videoPlayLen) || 0 });
  }
  return { items };
}

// These are wx_channel's local APIs, not unauthenticated Tencent APIs.
export async function readChannels(urlOrSelection, options) {
  const { request, signal: combinedSignal } = channelClient(options);
  let params;
  if (typeof urlOrSelection === 'string') {
    const requested = classifyShare(urlOrSelection);
    if (requested.kind !== 'channels') throw fail('INVALID_LINK', '需要有效的视频号链接。');
    params = { url: requested.url };
    const page = new URL(requested.url);
    if (page.hostname === 'channels.weixin.qq.com' && page.pathname === '/web/pages/feed') {
      const decode = name => {
        const value = page.searchParams.get(name) ?? '';
        const bytes = Buffer.from(value, 'base64url');
        if (!/^[A-Za-z0-9_+-/]{11}=?$/.test(value) || bytes.length !== 8) throw fail('INVALID_LINK', '视频链接参数不完整，请按标题搜索视频号。');
        return bytes.readBigUInt64BE().toString();
      };
      // wx_channel's page helper is missing this decoder; keep 64-bit precision locally.
      params = { object_id: decode('oid'), nonce_id: decode('nid') };
    }
  } else params = validateChannelSelection(urlOrSelection);

  const profile = await request('/api/channels/feed/profile', params);
  const video = profile.object;
  if (!video || !video.objectDesc) throw fail('NO_VIDEO', '分享链接未返回视频内容，未生成回复。');
  const id = String(video.id ?? ''), nonce = String(video.objectNonceId ?? '');
  const media = video.objectDesc.media?.[0];
  if (!id || !media?.url) throw fail('NO_VIDEO', '未取得视频媒体地址，不能把封面或标题当成视频正文。');
  const token = String(media.urlToken ?? '').replace(/^[?&]+/, '');
  const mediaUrl = new URL(media.url + (token ? (media.url.includes('?') ? '&' : '?') + token : ''));
  if (mediaUrl.protocol !== 'https:' || mediaUrl.username || mediaUrl.password || mediaUrl.port ||
      !['qq.com', 'qpic.cn'].some(domain => mediaUrl.hostname.endsWith('.' + domain)))
    throw fail('MEDIA_URL', '视频服务返回了不受信任的媒体地址，已停止读取。');
  // Use an advertised rendition: short source videos can still exceed the 20 MB limit.
  const rendition = media.spec?.filter(item => item.codingFormat === 'h264' && /^[A-Za-z0-9]+$/.test(item.fileFormat))
    .sort((a, b) => a.videoBitrate - b.videoBitrate)[0];
  if (rendition) mediaUrl.searchParams.set('X-snsvideoflag', rendition.fileFormat);
  const key = String(media.decodeKey ?? '');
  if (key && !/^\d{1,20}$/.test(key)) throw fail('DECODE_KEY', '视频解码参数不完整。');
  const attachment = await request('/api/video/play', { url: mediaUrl.href, ...(key ? { key } : {}) }, true);

  const comments = [], seen = new Set();
  let commentStatus = 'sample', requests = 0;
  const addComment = (item, parent = '') => {
    const id = String(item?.commentId ?? '');
    const text = clean(item?.content, 400);
    if (!text || comments.length >= 80 || seen.has(id || parent + ':' + text)) return;
    seen.add(id || parent + ':' + text);
    // Do not send avatars, account IDs or location data to the model.
    const likes = Number.isSafeInteger(item.likeCount) && item.likeCount >= 0 ? item.likeCount : null;
    comments.push({ text, likes, ...(parent ? { reply_to: parent } : {}) });
  };
  if (nonce) try {
    let marker = '';
    const parents = [];
    do {
      await delay(500, undefined, { signal: combinedSignal });
      const page = await request('/api/channels/feed/comment/list', { object_id: id, nonce_id: nonce, next_marker: marker });
      requests++;
      if (!Array.isArray(page.commentInfo)) throw fail('COMMENTS_INVALID', '评论数据不可读。');
      for (const item of page.commentInfo) {
        addComment(item);
        if (Array.isArray(item.levelTwoComment)) for (const reply of item.levelTwoComment) addComment(reply, clean(item.content, 120));
        if (item.expandCommentCount > 0 || item.continueFlag > 0) parents.push(item);
      }
      const next = typeof page.lastBuffer === 'string' ? page.lastBuffer : '';
      if (!next || next === marker) break;
      marker = next;
    } while (requests < 2 && comments.length < 60);
    for (const parent of parents.slice(0, 3)) {
      if (comments.length >= 80) break;
      await delay(500, undefined, { signal: combinedSignal });
      const page = await request('/api/channels/feed/comment/list', { object_id: id, nonce_id: nonce, comment_id: String(parent.commentId), next_marker: '' });
      if (!Array.isArray(page.commentInfo)) throw fail('COMMENTS_INVALID', '评论数据不可读。');
      for (const item of page.commentInfo) addComment(item, clean(parent.content, 120));
    }
  } catch (error) {
    if (combinedSignal.aborted) { await attachment.dispose(); throw error; }
    commentStatus = comments.length ? 'partial_error' : 'unavailable';
  }
  else commentStatus = 'unavailable';
  comments.sort((a, b) => (b.likes ?? -1) - (a.likes ?? -1));
  return { attachment, description: clean(video.objectDesc.description, 2000), comments, commentStatus };
}
