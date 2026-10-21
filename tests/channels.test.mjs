import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile, stat } from 'node:fs/promises';
import { once } from 'node:events';
import { classifyShare, readChannels, searchChannels, recentChannels, channelShareUrl, selectionFromFeedUrl, validateChannelSelection } from '../lib/channels.mjs';
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
  assert.deepEqual(result.items, [{ object_id: '18446744073709551615', nonce_id: 'nonce', title: '散步 & 旅行', author: '作者', duration: 38, share_url: '' }]);
  for (const q of ['', '中'.repeat(34), null]) await assert.rejects(searchChannels(q, { config: bridge }));
  for (const id of ['18446744073709551616', 'x', 100]) assert.throws(() => validateChannelSelection({ object_id: id, nonce_id: 'nonce' }));
  const selection = { object_id: '18446744073709551615', nonce_id: 'nonce' };
  assert.deepEqual(validateInput({ text: '', mode: 'live', channel: selection }).channel, selection);
  assert.throws(() => validateInput({ text: 'title', mode: 'demo', channel: selection }));
});

test('recent captured videos require matching exact identity and never expose media credentials', async () => {
  const selection = { object_id: '18446744073709551615', nonce_id: '9223372036854775808' };
  const pageUrl = channelShareUrl(selection);
  assert.deepEqual(selectionFromFeedUrl(pageUrl), selection);
  assert.equal(channelShareUrl({ ...selection, nonce_id: 'non-numeric-nonce' }), '');
  const record = { ...selection, nonce_id: 'opaque-nonce=', title: '原视频', author: '作者', duration: 23.961, captured_at: '2026-10-21T07:00:00Z', videoUrl: 'private-url', decryptKey: 'private-key' };
  const result = await recentChannels({ config: bridge, fetchImpl: (url, options) => {
    assert.equal(new URL(url).pathname, '/api/local/captured-feeds');
    assert.equal(options.headers['X-Local-Auth'], bridge.channelsBridgeToken);
    return envelope({ items: [record, { ...record, object_id: 123 }, { ...record, nonce_id: '<unsafe>' }, { ...record, captured_at: '' }] });
  } });
  assert.equal(result.items.length, 1);
  assert.equal(result.items[0].duration, 23.961);
  assert.equal(result.items[0].nonce_id, 'opaque-nonce=');
  assert.doesNotMatch(JSON.stringify(result), /private-|decryptKey|videoUrl/);
  await assert.rejects(recentChannels({ config: { ...bridge, publicOrigin: 'https://example.test' } }));
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
  await assert.rejects(readChannels({ ...selection, object_id: '123' }, { config: bridge, fetchImpl: upstream }), /不一致/);
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

test('explicit end-of-comments flag stops pagination even when WeChat retains a cursor', async () => {
  let pages = 0;
  const result = await readChannels(share, { config: bridge, fetchImpl: (url, options) => {
    if (!String(url).includes('/comment/')) return upstream(url, options);
    pages++;
    return envelope({ commentInfo: [{ commentId: 'one', content: '唯一评论', likeCount: 2 }], downContinueFlag: 0, lastBuffer: 'retained-cursor', countInfo: { commentCount: 1 } });
  } });
  try { assert.equal(pages, 1); assert.equal(result.commentStatus, 'sample'); assert.equal(result.commentStats.reported, 1); }
  finally { await result.attachment.dispose(); }
});

test('transcription starts while comments are pending and failed processing cleans its video', async () => {
  let release, path;
  const gate = new Promise(resolve => { release = resolve; });
  const result = await readChannels(share, { config: bridge, signal: AbortSignal.timeout(2000), prepareVideo: async attachment => {
    assert.deepEqual(await readFile(attachment.path), video); release(); return { transcript: '实际转写' };
  }, fetchImpl: async (url, options) => {
    if (String(url).includes('/comment/')) { await gate; return envelope({ commentInfo: [], lastBuffer: '' }); }
    return upstream(url, options);
  } });
  assert.equal(result.preparedVideo.transcript, '实际转写'); await result.attachment.dispose();
  await assert.rejects(readChannels(share, { config: bridge, fetchImpl: upstream, prepareVideo: async attachment => { path = attachment.path; throw new Error('transcription failed'); } }), /transcription failed/);
  await assert.rejects(stat(path), { code: 'ENOENT' });
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
  let llmCalls = 0, mediaCalls = 0;
  const generation = { content_summary: '视频讲散步，评论反对赶路打卡。', possible_meanings: ['可能是在分享放慢节奏的想法。', '也许想约着出去走走。'], reply_tip: '只根据已读内容接话。', reply: '赶着打卡挺累的，留点时间随便走走也挺好。' };
  const server = createServer({ config, extractVideoImpl: async attachment => {
    mediaCalls++;
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
  const second = await fetch(`http://127.0.0.1:${server.address().port}/api/reply`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Request-Id': 'a'.repeat(32) }, body: JSON.stringify({ mode: 'live', text: share, context: '换个接话角度', variant: 1 }) });
  assert.equal(second.status, 200);
  assert.equal((await second.json()).meta.evidence_cache_hit, true);
  assert.equal(mediaCalls, 1); assert.equal(llmCalls, 3);
  const progress = await (await fetch(`http://127.0.0.1:${server.address().port}/api/local/progress?id=${'a'.repeat(32)}`)).json();
  assert.equal(progress.done, true); assert.ok(progress.elapsed_ms >= 0);
  assert.doesNotMatch(JSON.stringify(progress), /走走|nonce|secret/);
});

test('comment pagination includes more pages and threaded replies with stable references', async () => {
  let mainPages = 0, replyPages = 0;
  const result = await readChannels(share, { config: bridge, fetchImpl: (url, options) => {
    const target = new URL(url);
    if (!target.pathname.includes('/comment/')) return upstream(url, options);
    const parent = target.searchParams.get('comment_id');
    if (parent) {
      replyPages++;
      return envelope({ commentInfo: Array.from({ length: 20 }, (_, i) => ({ commentId: `reply-${parent}-${replyPages}-${i}`, content: `回复 ${replyPages}/${i}`, likeCount: i })), lastBuffer: target.searchParams.get('next_marker') ? '' : 'reply-next' });
    }
    mainPages++;
    return envelope({ commentInfo: Array.from({ length: 10 }, (_, i) => ({ commentId: `main-${mainPages}-${i}`, content: `主评论 ${mainPages}/${i}`, likeCount: i, expandCommentCount: 40 })), lastBuffer: mainPages < 10 ? 'page-' + mainPages : '' });
  } });
  try {
    assert.equal(mainPages, 8); assert.ok(replyPages >= 6);
    assert.equal(result.comments.length, 200); assert.equal(result.commentStatus, 'limited');
    assert.equal(result.comments[199].ref, 'c200');
    assert.ok(result.comments.some(c => c.reply_to));
    assert.ok(result.timings.comments_ms >= 0); assert.equal(result.commentStats.fetched, 200);
  } finally { await result.attachment.dispose(); }
});
