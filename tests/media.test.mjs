import assert from 'node:assert/strict';
import test from 'node:test';
import { once } from 'node:events';
import { createServer } from '../server.mjs';
import { validateAttachment, validateVisualEvidence } from '../lib/media.mjs';
import { validateInput } from '../lib/translate.mjs';

const png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jB9kAAAAASUVORK5CYII=';
const image = { kind: 'image', name: '鸡汤.png', mime: 'image/png', data: png };
const config = { typesafeKey: 'fixture-jev-key', llmKey: 'fixture-llm-key', llmBaseUrl: 'https://llm.example.test', llmModel: 'vision-fixture', llmVision: true };
const visual = { visual_summary: '图片是一段关于知足的人生感悟。', visible_text: '做人要知足，平平淡淡才是真。' };
const generation = { content_summary: '这段分享强调知足和平常生活。', possible_meanings: ['也许是觉得这段人生感悟值得分享。'], reply_tip: '可以问问对方最有感触的是哪一句。', reply: '妈，这句你最有感触的是哪一段？我想听你说说。' };
const decision = { model: 'jev-fixture', answers: { topic: { type: 'choice', choice: 'sharing', confidence: 0.9, probabilities: { care: 0, invitation: 0, pressure: 0, sharing: 1, conflict: 0, unclear: 0 } }, health_claim: { type: 'noul', noul: 0 } } };
const json = value => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
const completion = value => json({ model: 'vision-fixture', choices: [{ message: { content: JSON.stringify(value) } }] });

async function serve(t, options = {}) {
  const server = createServer(options);
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  t.after(() => new Promise(resolve => { server.close(resolve); server.closeAllConnections(); }));
  const origin = `http://127.0.0.1:${server.address().port}`;
  return {
    getConfig: async () => (await fetch(origin + '/api/config')).json(),
    post: body => fetch(origin + '/api/reply', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
  };
}

test('attachments require bounded canonical base64 and supported actual content, not trusted extensions', () => {
  assert.equal(validateAttachment({ ...image, mime: 'text/html', name: 'wrong.jpg' }).mime, 'image/png');
  assert.equal(validateInput({ text: '', mode: 'live', attachment: image }).attachment.kind, 'image');
  assert.throws(() => validateInput({ text: '', mode: 'demo', attachment: image }), /真实 AI/);
  for (const attachment of [null, {}, { ...image, kind: 'url' }, { ...image, data: '<script>' }, { ...image, data: png.slice(1) }, { ...image, data: Buffer.from('not an image').toString('base64') }, { ...image, name: '../x'.repeat(100) }, { ...image, data: Buffer.alloc(4 * 1024 * 1024 + 1).toString('base64') }]) assert.throws(() => validateAttachment(attachment));
  assert.throws(() => validateVisualEvidence({ visual_summary: '', visible_text: '' }));
  assert.throws(() => validateVisualEvidence({ visual_summary: '图', visible_text: '字'.repeat(4001) }));
});

test('image contents are read first, then evidence reaches both Jev and reply generation', async t => {
  const calls = [];
  const app = await serve(t, { config, fetchImpl: async (url, options) => {
    const body = JSON.parse(options.body);
    calls.push({ url, body });
    if (calls.length === 1) return completion(visual);
    if (String(url).includes('typesafe.ai')) return json(decision);
    return completion(generation);
  } });
  const availability = await app.getConfig();
  assert.equal(availability.mediaAvailable, true);
  assert.equal(availability.videoAvailable, false);
  const response = await app.post({ text: '', recipient: 'mother', mode: 'live', attachment: image });
  assert.equal(response.status, 200);
  const result = await response.json();
  assert.equal(calls.length, 3);
  assert.equal(calls[0].body.temperature, 0.2);
  assert.equal(calls[2].body.temperature, 0.2);
  assert.ok(calls[0].body.messages[1].content.some(item => item.type === 'image_url' && item.image_url.url === 'data:image/png;base64,' + png));
  assert.ok(calls[1].body.state.message_text.includes(visual.visible_text));
  assert.deepEqual(JSON.parse(calls[2].body.messages[1].content).source_evidence, result.evidence);
  assert.equal(result.content_summary, generation.content_summary);
  assert.equal(result.evidence.kind, 'image');
  assert.equal(result.evidence.audio_status, 'not_applicable');
  assert.equal(result.decision_source, 'jev');
  assert.equal(result.generation_source, 'llm');
  assert.doesNotMatch(JSON.stringify(result), /fixture-jev-key|fixture-llm-key|iVBOR/);
});

test('vision errors stop analysis without pretending that the image was understood', async t => {
  let calls = 0;
  const app = await serve(t, { config, fetchImpl: async () => { calls++; return completion({ visual_summary: '缺少识别字段' }); } });
  const response = await app.post({ text: '这是什么', mode: 'live', attachment: image });
  assert.equal(response.status, 502);
  const result = await response.json();
  assert.equal(result.cause, 'MEDIA_VISION_INVALID');
  assert.equal(result.reply, undefined);
  assert.equal(calls, 1);
});

test('unsupported model configurations cannot accept media even when text AI is configured', async t => {
  let called = false;
  const app = await serve(t, { config: { ...config, llmVision: false }, fetchImpl: async () => { called = true; } });
  assert.equal((await app.getConfig()).mediaAvailable, false);
  const response = await app.post({ text: '', mode: 'live', attachment: image });
  assert.equal(response.status, 503);
  assert.equal((await response.json()).code, 'MEDIA_NOT_CONFIGURED');
  assert.equal(called, false);
});

test('a pasted WeChat article is actually extracted before classification and reply generation', async t => {
  const calls = [];
  const app = await serve(t, { config, fetchImpl: async (url, options) => {
    calls.push(String(url));
    if (String(url).startsWith('https://mp.weixin.qq.com/')) {
      assert.equal(options.redirect, 'manual');
      assert.equal(options.headers.Authorization, undefined);
      return new Response('<h1 id="activity-name">平凡的生活</h1><div id="js_content"><p>做人要知足。</p><p>平平淡淡才是真。</p></div>', { headers: { 'Content-Type': 'text/html' } });
    }
    const body = JSON.parse(options.body);
    if (String(url).includes('typesafe.ai')) {
      assert.ok(body.state.message_text.includes('平平淡淡才是真'));
      return json(decision);
    }
    assert.equal(JSON.parse(body.messages[1].content).source_evidence.kind, 'article');
    return completion(generation);
  } });
  const response = await app.post({ text: '妈妈分享了 https://mp.weixin.qq.com/s/example', mode: 'live' });
  assert.equal(response.status, 200);
  const result = await response.json();
  assert.equal(result.evidence.kind, 'article');
  assert.equal(result.evidence.visible_text, '做人要知足。\n平平淡淡才是真。');
  assert.equal(calls.length, 3);
});

test('an unreadable article returns an actionable error, not invented article analysis', async t => {
  let calls = 0;
  const app = await serve(t, { config, fetchImpl: async () => { calls++; return new Response('<div>环境异常，请完成验证</div>', { headers: { 'Content-Type': 'text/html' } }); } });
  const response = await app.post({ text: 'https://mp.weixin.qq.com/s/example', mode: 'live' });
  assert.equal(response.status, 502);
  const result = await response.json();
  assert.equal(result.cause, 'ARTICLE_RESTRICTED');
  assert.equal(result.reply, undefined);
  assert.equal(calls, 1);
});
