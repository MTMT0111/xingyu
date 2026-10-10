import { Parser } from 'htmlparser2';

const maxBytes = 2 * 1024 * 1024;
const blocks = new Set(['address', 'article', 'blockquote', 'br', 'div', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'hr', 'li', 'p', 'section', 'tr']);
const hidden = new Set(['script', 'style', 'template', 'noscript']);

function articleError(articleCode, message) {
  return Object.assign(new Error(message), { articleCode });
}

function checkedUrl(value) {
  let url;
  try { url = new URL(value); } catch { /* Report one safe error below. */ }
  const authority = typeof value === 'string' ? /^https:\/\/([^/?#]+)/i.exec(value)?.[1] : undefined;
  if (!url || authority?.toLowerCase() !== 'mp.weixin.qq.com' || url.protocol !== 'https:' ||
      url.username || url.password || url.port || !/^\/s(?:\/[A-Za-z0-9_-]+)?$/.test(url.pathname)) {
    throw articleError('invalid_url', '仅支持微信公众号的 HTTPS 文章链接，请粘贴文章正文或有效文章链接。');
  }
  url.hash = '';
  return url.href;
}

export function findWechatArticleUrl(text) {
  if (typeof text !== 'string') return null;
  const links = new Set();
  for (const match of text.matchAll(/https?:\/\/[^\s<>"'`]+/gi)) {
    const candidate = match[0].replace(/[，。！？、；：）】》」』,.!?;:)\]}]+$/u, '');
    let parsed;
    try { parsed = new URL(candidate); } catch { continue; }
    if (parsed.hostname.toLowerCase() !== 'mp.weixin.qq.com') continue;
    links.add(checkedUrl(candidate));
  }
  if (links.size > 1) throw articleError('multiple_links', '一次请只分享一篇公众号文章，或直接粘贴想回复的片段。');
  return links.values().next().value ?? null;
}

function abortable(promise, signal) {
  return new Promise((resolve, reject) => {
    const abort = () => reject(signal.reason);
    if (signal.aborted) { promise.catch(() => {}); reject(signal.reason); return; }
    signal.addEventListener('abort', abort, { once: true });
    promise.then(resolve, reject).finally(() => signal.removeEventListener('abort', abort));
  });
}

function parseArticle(html) {
  const stack = [];
  const content = [];
  const heading = [];
  const page = [];
  let pageLength = 0;
  const parser = new Parser({
    onopentag(name, attrs) {
      const parent = stack.at(-1);
      const frame = { name, skip: parent?.skip || hidden.has(name), body: parent?.body || attrs.id === 'js_content', title: parent?.title || attrs.id === 'activity-name' };
      stack.push(frame);
      if (!frame.skip && frame.body && blocks.has(name)) content.push('\n');
    },
    ontext(value) {
      const frame = stack.at(-1);
      if (frame?.skip) return;
      if (frame?.body) content.push(value);
      if (frame?.title) heading.push(value);
      if (pageLength < 12000) { page.push(value); pageLength += value.length; }
    },
    onclosetag() {
      const frame = stack.pop();
      if (frame?.body && !frame.skip && blocks.has(frame.name)) content.push('\n');
    }
  }, { decodeEntities: true });
  parser.end(html);
  const normalize = value => value.replace(/[ \t\r\f\v\u00a0]+/g, ' ').split('\n').map(line => line.trim()).filter(Boolean).join('\n').trim();
  const text = normalize(content.join(''));
  if (!text) {
    const unavailable = /环境异常|完成验证|请输入验证码|访问过于频繁|请先登录|登录后查看|内容已被删除|该内容已被发布者删除|页面不存在|内容因违规无法查看/.test(page.join(''));
    throw articleError(unavailable ? 'restricted' : 'no_content', '这篇文章暂时无法读取，可能需要验证、登录或已失效。请在微信打开后复制正文或想聊的片段。');
  }
  if ([...text].length > 6000) throw articleError('too_long', '文章超过 6000 字，请复制其中想聊的片段；没有自动截断文章。');
  return { title: normalize(heading.join('')).replace(/\n/g, ' ') || '微信公众号文章', text };
}

export async function readWechatArticle(value, { fetchImpl = globalThis.fetch, signal } = {}) {
  const url = checkedUrl(value);
  const timeout = AbortSignal.timeout(8000);
  const requestSignal = signal ? AbortSignal.any([signal, timeout]) : timeout;
  let reader;
  let response;
  try {
    if (requestSignal.aborted) throw requestSignal.reason;
    response = await abortable(fetchImpl(url, {
      redirect: 'manual', signal: requestSignal,
      headers: { Accept: 'text/html,application/xhtml+xml', 'User-Agent': 'Xingyu-ArticleReader/1.0' }
    }), requestSignal);
    if (response.redirected || (response.status >= 300 && response.status < 400)) {
      let destination;
      try { destination = new URL(response.headers.get('location'), url); } catch { /* Unknown redirects remain blocked. */ }
      if (destination?.origin === 'https://mp.weixin.qq.com' && !destination.username && !destination.password && destination.pathname === '/mp/wappoc_appmsgcaptcha') {
        throw articleError('restricted', '微信要求验证，暂时无法读取这篇文章。请在微信打开后复制正文，或上传文章截图。');
      }
      throw articleError('redirect', '文章链接发生跳转，暂时无法读取。请直接粘贴正文或想聊的片段。');
    }
    if (!response.ok) throw articleError(response.status === 404 ? 'not_found' : 'unavailable', '文章暂时无法访问，请在微信打开后复制正文或想聊的片段。');
    const type = response.headers.get('content-type');
    if (type && !/^text\/html\b|^application\/xhtml\+xml\b/i.test(type)) throw articleError('not_html', '这个链接没有返回可读取的文章，请粘贴正文或想聊的片段。');
    if (Number(response.headers.get('content-length')) > maxBytes) {
      throw articleError('too_large', '文章页面过大，暂时无法读取。请粘贴正文或想聊的片段。');
    }
    reader = response.body?.getReader();
    if (!reader) throw articleError('no_content', '文章没有可读取的正文，请直接粘贴想聊的片段。');
    const decoder = new TextDecoder('utf-8');
    const chunks = [];
    let bytes = 0;
    while (true) {
      const { done, value: chunk } = await abortable(reader.read(), requestSignal);
      if (done) break;
      bytes += chunk.byteLength;
      if (bytes > maxBytes) throw articleError('too_large', '文章页面过大，暂时无法读取。请粘贴正文或想聊的片段。');
      chunks.push(decoder.decode(chunk, { stream: true }));
    }
    chunks.push(decoder.decode());
    return { ...parseArticle(chunks.join('')), url };
  } catch (error) {
    if (error?.articleCode) throw error;
    if (requestSignal.aborted) throw articleError(signal?.aborted ? 'aborted' : 'timeout', signal?.aborted ? '已取消文章读取。' : '文章读取超时，请稍后重试，或直接粘贴正文。');
    throw articleError('unavailable', '文章暂时无法读取，请在微信打开后复制正文或想聊的片段。');
  } finally {
    // Cancelling also stops a response stream after size limits or caller cancellation.
    if (reader) { void reader.cancel().catch(() => {}); }
    else if (response?.body) { void response.body.cancel().catch(() => {}); }
  }
}
