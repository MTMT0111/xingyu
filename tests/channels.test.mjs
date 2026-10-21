import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { once } from 'node:events';
import { classifyShare, readChannels, searchChannels, validateChannelSelection } from '../lib/channels.mjs';
import { validateInput } from '../lib/translate.mjs';
import { createServer } from '../server.mjs';

// Contract fixtures derived from wx_channel's API source; not a live WeChat test.
const bridge = { channelsBridgeUrl: 'http://127.0.0.1:2026', channelsBridgeToken: 'local-test-secret' };
const share = 'https://weixin.qq.com/sph/fixture';
const video = await readFile(new URL('./fixtures/parent-message.mp4', import.meta.url));
const envelope = data => new Response(JSON.stringify({ code: 0, data: { errCode: 0, data } }));
const profile = { object: { id: '18446744073709551615', objectNonceId: 'nonce', objectDesc: { description: '散步时别只顾着赶路', media: [{ url: 'https://finder.video.qq.com/test.mp4', urlToken: '&token=a+b', decodeKey: '18446744073709551615' }] } } };
const calls = [];
function upstream(url, options) {
  const u = new URL(url); calls.push(u);
  assert.equal(u.origin, bridge.channelsBridgeUrl);
  assert.equal(options.headers['X-Local-Auth'], bridge.channelsBridgeToken);
  assert.equal(options.redirect, 'error');
  if (u.pathname.endsWith('/profile')) return envelope(profile);
  if (u.pathname === '/api/video/play') {
    assert.equal(u.searchParams.get('key'), '18446744073709551615');
    assert.ok(u.searchParams.get('url').endsWith('token=a+b'));
    return new Response(video);
  }
  if (u.searchParams.get('comment_id')) return envelope({ commentInfo: [{ commentId: 'reply', content: '慢慢走才有意思', likeCount: 79 }], lastBuffer: '' });
  return envelope({ commentInfo: [{ commentId: 'first', content: '不是打卡越多越好', likeCount: 444, expandCommentCount: 1, nickname: '不传模型的昵称', headUrl: 'private' }], lastBuffer: '' });
}

test('share routing accepts exact WeChat links and rejects ambiguity and unsafe URLs', () => {
  assert.equal(classifyShare('你好').kind, 'text');
  assert.equal(classifyShare('看看 https://mp.weixin.qq.com/s/abc。').kind, 'article');
  assert.deepEqual(classifyShare(share + '。'), { kind: 'channels', url: share });
  assert.equal(classifyShare('https://channels.weixin.qq.com/web/pages/feed?oid=123&nid=x').kind, 'channels');
  for (const text of [share + ' https://mp.weixin.qq.com/s/abc', 'https://secret@weixin.qq.com/sph/a', 'http://weixin.qq.com/sph/a', 'https://weixin.qq.com:99/sph/a']) assert.throws(() => classifyShare(text));
  assert.equal(classifyShare('https://weixin.qq.com.attacker.test/sph/a').kind, 'text');
});

test('search preserves exact IDs, strips title markup and exposes no media credentials', async () => {
  const result = await searchChannels('散步', { config: bridge, fetchImpl: () => new Response('{"code":0,"data":{"errCode":0,"data":{"objectList":[{"id":18446744073709551615,"objectNonceId":"nonce","objectDesc":{"description":"<em>散步</em> &amp; 旅行","media":[{"url":"secret","decodeKey":"private","videoPlayLen":38}]},"contact":{"nickname":"作者","username":"private"}}]}}}') });
  assert.deepEqual(result.items, [{ object_id: '18446744073709551615', nonce_id: 'nonce', title: '散步 & 旅行', author: '作者', duration: 38 }]);
  for (const q of ['', '中'.repeat(34), null]) await assert.rejects(searchChannels(q, { config: bridge }));
  for (const id of ['18446744073709551616', 'x', 100]) assert.throws(() => validateChannelSelection({ object_id: id, nonce_id: 'nonce' }));
  const selection = { object_id: '18446744073709551615', nonce_id: 'nonce' };
  assert.deepEqual(validateInput({ text: '', mode: 'live', channel: selection }).channel, selection);
  assert.throws(() => validateInput({ text: 'title', mode: 'demo', channel: selection }));
});

test('selected search result fetches media by ID without opening its page', async () => {
  const selection = { object_id: '18446744073709551615', nonce_id: 'nonce' };
  let queried = false;
  const result = await readChannels(selection, { config: bridge, fetchImpl: (url, options) => {
    const u = new URL(url);
    if (u.pathname.endsWith('/profile')) {
      queried = true;
      assert.equal(u.searchParams.get('object_id'), selection.object_id);
      assert.equal(u.searchParams.get('nonce_id'), selection.nonce_id);
      assert.equal(u.searchParams.has('url'), false);
      return envelope({ object: { ...profile.object, objectDesc: { ...profile.object.objectDesc, media: [{ ...profile.object.objectDesc.media[0], spec: [{ codingFormat: 'h264', fileFormat: 'xWT113', videoBitrate: 511 }] }] } } });
    }
    if (u.pathname === '/api/video/play') { assert.equal(new URL(u.searchParams.get('url')).searchParams.get('X-snsvideoflag'), 'xWT113'); return new Response(video); }
    return upstream(url, options);
  } });
  assert.ok(queried);
  assert.deepEqual(await readFile(result.attachment.path), video);
  await result.attachment.dispose();
});

test('feed URLs decode unsigned 64-bit IDs locally without the missing page helper', async () => {
  const page = 'https://channels.weixin.qq.com/web/pages/feed?oid=__________8&nid=gAAAAAAAAAA';
  const result = await readChannels(page, { config: bridge, fetchImpl: (url, options) => {
    const u = new URL(url);
    if (u.pathname.endsWith('/profile')) {
      assert.equal(u.searchParams.get('object_id'), '18446744073709551615');
      assert.equal(u.searchParams.get('nonce_id'), '9223372036854775808');
      assert.equal(u.searchParams.has('url'), false);
    }
    return upstream(url, options);
  } });
  await result.attachment.dispose();
});

test('local connector returns actual media plus anonymized comments and reply relationship', async () => {
  const result = await readChannels(share, { config: bridge, fetchImpl: upstream });
  assert.deepEqual(await readFile(result.attachment.path), video);
  await result.attachment.dispose();
  assert.equal(result.comments.length, 2);
  assert.deepEqual(result.comments.map(c => c.likes), [444, 79]);
  assert.equal(result.comments[1].reply_to, '不是打卡越多越好');
  assert.equal(result.commentStatus, 'sample');
  assert.doesNotMatch(JSON.stringify(result.comments), /nickname|headUrl|private|first|不传模型/);
});

test('connector must be explicitly configured and never usable from public deployment', async () => {
  let fetched = false;
  for (const config of [{}, { ...bridge, channelsBridgeUrl: 'http://192.168.1.1' }, { ...bridge, channelsBridgeUrl: 'http://127.0.0.1:2026/evil' }, { ...bridge, publicOrigin: 'https://example.test' }]) {
    await assert.rejects(readChannels(share, { config, fetchImpl: () => { fetched = true; } }));
  }
  assert.equal(fetched, false);
});

test('missing media, private targets and connector error cannot degrade to title-only replies', async () => {
  for (const object of [{ objectDesc: { description: 'only a title' } }, { ...profile.object, objectDesc: { media: [{ url: 'https://127.0.0.1/secret' }] } }]) {
    await assert.rejects(readChannels(share, { config: bridge, fetchImpl: () => envelope({ object }) }));
  }
  await assert.rejects(readChannels(share, { config: bridge, fetchImpl: () => new Response('{"code":403}') }));
});

test('failed comments are labeled unavailable while verified video remains available', async () => {
  const result = await readChannels(share, { config: bridge, fetchImpl: (url, options) => String(url).includes('comment/list') ? new Response('failed', { status: 503 }) : upstream(url, options) });
  assert.equal(result.commentStatus, 'unavailable');
  assert.equal(result.comments.length, 0);
  assert.deepEqual(await readFile(result.attachment.path), video);
  await result.attachment.dispose();
});

test('stalled response cancels promptly and oversized payloads are rejected', async () => {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 30);
  await assert.rejects(readChannels(share, { config: bridge, signal: controller.signal, fetchImpl: () => new Response(new ReadableStream({ start() {} })) }), /取消/);
  clearTimeout(timer);
  await assert.rejects(readChannels(share, { config: bridge, fetchImpl: () => new Response('{}', { headers: { 'Content-Length': 3 * 1024 * 1024 } }) }), /上限/);
});

test('backend routes link through media and comments before LLM, and keeps signed URLs local', async t => {
  const config = { ...bridge, llmKey: 'model-test-secret', llmBaseUrl: 'https://model.example.test', llmModel: 'test', llmVision: true };
  let llmCalls = 0;
  const generation = { content_summary: '视频讲散步，评论反对赶路打卡。', possible_meanings: ['可能是在分享放慢节奏的想法。', '也许想约着出去走走。'], reply_tip: '只根据已读内容接话。', reply: '赶着打卡挺累的，留点时间随便走走也挺好。' };
  const server = createServer({ config, extractVideoImpl: async attachment => {
    assert.deepEqual(await readFile(attachment.path), video);
    return { duration: 5, frames: [{ time: 0, dataUrl: 'data:image/jpeg;base64,eA==' }], transcript: '出去走走，不必急着赶路。', audioStatus: 'transcribed' };
  }, fetchImpl: (url, options) => {
    if (String(url).startsWith('http://127.0.0.1:2026/')) return upstream(url, options);
    llmCalls++;
    assert.doesNotMatch(options.body, /exportkey|pass_ticket|local-test-secret|objectNonceId|finder\.video/);
    const request = JSON.parse(options.body);
    const result = llmCalls === 1 ? { visual_summary: '户外散步的视频。', visible_text: '不必急着赶路' } : generation;
    if (llmCalls === 2) {
      const evidence = JSON.parse(request.messages[1].content).source_evidence;
      assert.equal(evidence.comments.length, 2);
      assert.ok(evidence.transcript.includes('走走'));
    }
    return new Response(JSON.stringify({ choices: [{ message: { content: JSON.stringify(result) } }] }));
  } });
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  t.after(() => { server.close(); server.closeAllConnections(); });
  const result = await fetch(`http://127.0.0.1:${server.address().port}/api/reply`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ mode: 'live', text: share }) });
  assert.equal(result.status, 200);
  const body = await result.json();
  assert.equal(body.source_kind, 'channels');
  assert.equal(body.evidence.kind, 'channels');
  assert.match(body.evidence.coverage, /非全部评论/);
  assert.equal(llmCalls, 2);
});
