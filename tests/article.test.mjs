import test from 'node:test';
import assert from 'node:assert/strict';
import { findWechatArticleUrl, readWechatArticle } from '../lib/article.mjs';

const url = 'https://mp.weixin.qq.com/s/Article_123';
const htmlResponse = html => new Response(html, { headers: { 'content-type': 'text/html; charset=utf-8' } });
const expectCode = code => error => error.articleCode === code && !error.message.includes('secret');

test('article link detection finds a unique canonical article in shared text', () => {
  assert.equal(findWechatArticleUrl(`妈妈分享《一起吃饭》：${url}。`), url);
  assert.equal(findWechatArticleUrl(`${url}#wechat_redirect\n${url}`), url);
  assert.equal(findWechatArticleUrl('没有链接'), null);
  assert.equal(findWechatArticleUrl('https://example.com/s/one'), null);
  assert.equal(findWechatArticleUrl('https://mp.weixin.qq.com.evil.test/s/one'), null);
  assert.equal(findWechatArticleUrl('https://mp.weixin.qq.com/s?__biz=abc&mid=123#wechat_redirect'), 'https://mp.weixin.qq.com/s?__biz=abc&mid=123');
  assert.throws(() => findWechatArticleUrl(`${url} https://mp.weixin.qq.com/s/Other`), expectCode('multiple_links'));
  assert.throws(() => findWechatArticleUrl('http://mp.weixin.qq.com/s/one'), expectCode('invalid_url'));
});

test('article reader rejects every non-allowlisted destination before fetching', async () => {
  const rejected = [
    'http://mp.weixin.qq.com/s/one', 'https://example.com/s/one', 'https://127.0.0.1/s/one',
    'https://mp.weixin.qq.com:443/s/one', 'https://user:secret@mp.weixin.qq.com/s/one',
    'https://mp.weixin.qq.com@evil.test/s/one', 'https://mp.weixin.qq.com.evil.test/s/one',
    'https://mp.weixin.qq.com/s/one/extra', 'https://mp.weixin.qq.com/s/', 'https://mp.weixin.qq.com/mp/profile',
    'https://mp.weixin.qq.com/s/%2e%2e/admin', 'https://mp.weixin.qq.com./s/one',
    'https://mp.weixin.qq.com\\@evil.test/s/one'
  ];
  let calls = 0;
  for (const value of rejected) await assert.rejects(readWechatArticle(value, { fetchImpl: () => { calls++; } }), expectCode('invalid_url'));
  assert.equal(calls, 0);
});

test('real HTML parser preserves nested article paragraphs and decodes entities', async () => {
  const result = await readWechatArticle(url, { fetchImpl: async (actualUrl, options) => {
    assert.equal(actualUrl, url);
    assert.equal(options.redirect, 'manual');
    assert.ok(options.signal instanceof AbortSignal);
    return htmlResponse('<html><h1 id="activity-name"> 妈妈的 <span>分享 &amp; 关心</span> </h1><div id="js_content"><section><p>第一段&nbsp;<strong>别熬夜</strong>。</p><div><p>第二段 &#x1F31F; &lt;温柔&gt;</p></div>第三段<br>第四段<script>secret()</script><style>.secret{}</style><template>secret</template></section></div><footer>不属于正文</footer></html>');
  }});
  assert.deepEqual(result, { title: '妈妈的 分享 & 关心', text: '第一段 别熬夜。\n第二段 🌟 <温柔>\n第三段\n第四段', url });
});

test('missing body and verification/login/deleted pages fail with safe actionable errors', async () => {
  for (const page of ['<p>请完成验证</p>', '<h1>环境异常</h1>', '<div>请先登录</div>', '<p>内容已被删除</p>']) {
    await assert.rejects(readWechatArticle(url, { fetchImpl: async () => htmlResponse(page) }), expectCode('restricted'));
  }
  await assert.rejects(readWechatArticle(url, { fetchImpl: async () => htmlResponse('<title>secret</title><div id="js_content"><script>secret</script></div>') }), expectCode('no_content'));
  await assert.rejects(readWechatArticle(url, { fetchImpl: async () => new Response('secret', { status: 404 }) }), expectCode('not_found'));
  await assert.rejects(readWechatArticle(url, { fetchImpl: async () => new Response('secret', { status: 403 }) }), expectCode('unavailable'));
});

test('long Unicode articles fail instead of silently truncating; exactly 6000 characters pass', async () => {
  const text = '🌟'.repeat(6000);
  const result = await readWechatArticle(url, { fetchImpl: async () => htmlResponse(`<div id="js_content">${text}</div>`) });
  assert.equal(result.text, text);
  await assert.rejects(readWechatArticle(url, { fetchImpl: async () => htmlResponse(`<div id="js_content">${text}多</div>`) }), expectCode('too_long'));
});

test('redirects are never followed and fetch failures do not leak URLs or upstream responses', async () => {
  await assert.rejects(readWechatArticle(url, { fetchImpl: async (_, options) => {
    assert.equal(options.redirect, 'manual');
    return new Response('', { status: 302, headers: { location: 'http://127.0.0.1/?secret=yes' } });
  }}), expectCode('redirect'));
  await assert.rejects(readWechatArticle(url, { fetchImpl: async () => ({ redirected: true, status: 200 }) }), expectCode('redirect'));
  await assert.rejects(readWechatArticle(`${url}?secret=yes`, { fetchImpl: async () => { throw new Error('redirect to secret'); } }), expectCode('unavailable'));
  await assert.rejects(readWechatArticle(url, { fetchImpl: async () => new Response('secret', { headers: { 'content-type': 'application/json' } }) }), expectCode('not_html'));
});

test('captcha redirects are recognized without following captcha, ordinary or hostile destinations', async () => {
  const destinations = [
    ['/mp/wappoc_appmsgcaptcha?ticket=secret', 'restricted'],
    ['https://mp.weixin.qq.com/mp/wappoc_appmsgcaptcha?ticket=secret', 'restricted'],
    ['https://mp.weixin.qq.com/s/AnotherArticle', 'redirect'],
    ['https://mp.weixin.qq.com.evil.test/mp/wappoc_appmsgcaptcha', 'redirect'],
    ['https://mp.weixin.qq.com@evil.test/mp/wappoc_appmsgcaptcha', 'redirect'],
    ['https://user:secret@mp.weixin.qq.com/mp/wappoc_appmsgcaptcha', 'redirect'],
    ['http://mp.weixin.qq.com/mp/wappoc_appmsgcaptcha', 'redirect'],
    ['http://127.0.0.1/private', 'redirect'],
    ['https://[', 'redirect']
  ];
  for (const [destination, code] of destinations) {
    let calls = 0;
    await assert.rejects(readWechatArticle(url, { fetchImpl: async (actualUrl, options) => {
      calls++;
      assert.equal(actualUrl, url);
      assert.equal(options.redirect, 'manual');
      return new Response('', { status: 302, headers: { location: destination } });
    } }), expectCode(code));
    assert.equal(calls, 1, 'a redirect must never trigger a second request');
  }
});

test('response limits check declared and streamed bytes and cancel oversized streams', async () => {
  let cancelled = 0;
  const oversized = () => new ReadableStream({ start(controller) { controller.enqueue(new Uint8Array(2 * 1024 * 1024 + 1)); }, cancel() { cancelled++; } });
  await assert.rejects(readWechatArticle(url, { fetchImpl: async () => new Response(oversized(), { headers: { 'content-length': String(3 * 1024 * 1024) } }) }), expectCode('too_large'));
  await assert.rejects(readWechatArticle(url, { fetchImpl: async () => new Response(oversized()) }), expectCode('too_large'));
  assert.equal(cancelled, 2);
});

test('UTF-8 split across network chunks is decoded without corrupting Chinese', async () => {
  const bytes = new TextEncoder().encode('<div id="js_content"><p>妈，收到啦。</p></div>');
  const stream = new ReadableStream({ start(controller) { for (const byte of bytes) controller.enqueue(new Uint8Array([byte])); controller.close(); } });
  const result = await readWechatArticle(url, { fetchImpl: async () => new Response(stream) });
  assert.equal(result.text, '妈，收到啦。');
});

test('caller cancellation interrupts a stalled stream even with a fetch mock that ignores signals', async () => {
  const controller = new AbortController();
  let cancelled = false;
  const result = readWechatArticle(url, { signal: controller.signal, fetchImpl: async () => new Response(new ReadableStream({ cancel() { cancelled = true; } })) });
  setTimeout(() => controller.abort(), 10);
  await assert.rejects(result, expectCode('aborted'));
  assert.equal(cancelled, true);
});

test('the eight-second timeout also bounds stalled body reads', async () => {
  let guard;
  let cancelled = false;
  const start = Date.now();
  const result = readWechatArticle(url, { fetchImpl: async () => new Response(new ReadableStream({ cancel() { cancelled = true; } })) });
  try {
    await assert.rejects(Promise.race([
      result,
      new Promise((_, reject) => { guard = setTimeout(() => reject(new Error('article timeout did not fire')), 9500); })
    ]), expectCode('timeout'));
    assert.ok(Date.now() - start >= 7800);
    assert.equal(cancelled, true);
  } finally { clearTimeout(guard); }
});
