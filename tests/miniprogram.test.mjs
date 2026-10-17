import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import test from 'node:test';

const require = createRequire(import.meta.url);
const { authorizationHeaders, classifyFile, codepointLength, generateLocalReply, initialBaseUrl, normalizeBaseUrl, validateMessage, validateResult } = require('../clients/wechat-miniprogram/utils.js');

test('mini program accepts HTTPS roots and localhost development only', () => {
  assert.equal(normalizeBaseUrl('https://reply.example.com/'), 'https://reply.example.com');
  assert.equal(normalizeBaseUrl('http://127.0.0.1:4173'), 'http://127.0.0.1:4173');
  for (const value of ['http://reply.example.com', 'https://reply.example.com/api', 'https://user@reply.example.com', '']) {
    assert.throws(() => normalizeBaseUrl(value));
  }
});

test('mini program never carries a computer loopback address onto a phone', () => {
  assert.equal(initialBaseUrl('', 'devtools'), 'http://127.0.0.1:4173');
  assert.equal(initialBaseUrl('http://127.0.0.1:4173', 'android'), '');
  assert.equal(initialBaseUrl('http://localhost:4173', 'ios'), '');
  assert.equal(initialBaseUrl('https://reply.example.com/', 'android'), 'https://reply.example.com');
});

test('mini program creates the existing trial Basic authorization without exposing provider keys', () => {
  assert.deepEqual(authorizationHeaders(''), {});
  assert.equal(authorizationHeaders('abcdefghijklmnop').Authorization, 'Basic dHJpYWw6YWJjZGVmZ2hpamtsbW5vcA==');
  assert.throws(() => authorizationHeaders('short'));
  assert.throws(() => authorizationHeaders('中文口令abcdefghijklmnop'));
});

test('mini program validates chat media before reading it into memory', () => {
  assert.deepEqual(classifyFile({ path: 'wxfile://photo.JPG', name: 'photo.JPG', size: 1024, type: 'image' }), {
    path: 'wxfile://photo.JPG', name: 'photo.JPG', size: 1024, kind: 'image', mime: 'image/jpeg'
  });
  assert.equal(classifyFile({ path: 'wxfile://clip.mp4', size: 2048, type: 'video' }).mime, 'video/mp4');
  assert.throws(() => classifyFile({ path: '', name: 'photo.jpg', size: 1024, type: 'image' }));
  assert.throws(() => classifyFile({ path: 'wxfile://note.pdf', size: 1024, type: 'file' }));
  assert.throws(() => classifyFile({ path: 'wxfile://clip.mp4', size: 21 * 1024 * 1024, type: 'video' }));
  assert.throws(() => classifyFile({ path: 'wxfile://photo.jpg', size: 1024, type: 'video' }));
});

test('mini program preserves Unicode limits and the server result contract', () => {
  assert.equal(codepointLength('家人🙂'), 3);
  assert.equal(validateMessage('  在吗？  ', false), '在吗？');
  assert.equal(validateMessage('', true), '');
  assert.throws(() => validateMessage('', false));
  assert.throws(() => validateMessage('🙂'.repeat(6001), false));
  const result = { content_summary: '一条问候。', possible_meanings: ['也许想聊聊。'], reply_tip: '先问问。', reply: '妈，怎么啦？' };
  assert.equal(validateResult(result), result);
  assert.throws(() => validateResult({ ...result, reply: '' }));
  assert.throws(() => validateResult({ ...result, generation_source: 'llm' }, 'image'));
  const mediaResult = { ...result, generation_source: 'llm', evidence: { kind: 'video', audio_status: 'transcribed' } };
  assert.equal(validateResult(mediaResult, 'video'), mediaResult);
  assert.throws(() => validateResult({ ...mediaResult, evidence: { kind: 'video', audio_status: 'not_applicable' } }, 'video'));
});

test('mini program keeps the text reply loop usable without a phone server', () => {
  const care = generateLocalReply('今天降温，记得多穿衣服。', 0);
  const next = generateLocalReply('今天降温，记得多穿衣服。', 1);
  const health = generateLocalReply('转发：这个偏方可以治愈高血压。', 0);
  assert.match(care.reply, /谢谢|关心|叮嘱/);
  assert.notEqual(care.reply, next.reply);
  assert.match(health.reply, /核实/);
  assert.equal(care.generation_source, 'templates');
});

test('mini program exposes one stable WeChat entry for sharing and favorites', (t) => {
  let definition;
  globalThis.Page = value => { definition = value; };
  globalThis.wx = {};
  const pagePath = require.resolve('../clients/wechat-miniprogram/pages/index/index.js');
  delete require.cache[pagePath];
  require(pagePath);
  t.after(() => {
    delete require.cache[pagePath];
    delete globalThis.Page;
    delete globalThis.wx;
  });
  assert.deepEqual(definition.onShareAppMessage(), {
    title: '言外：看懂分享，再回一句',
    path: '/pages/index/index'
  });
  assert.deepEqual(definition.onShareTimeline(), { title: '言外：看懂分享，再回一句' });
  assert.deepEqual(definition.onAddToFavorites(), { title: '言外｜打开就能准备回复' });
});

test('mini program page connects and asks for one automatic natural reply', async (t) => {
  let definition;
  const requests = [];
  globalThis.Page = value => { definition = value; };
  globalThis.wx = {
    getStorageSync: () => '',
    request(options) {
      requests.push(options);
      const data = options.url.endsWith('/api/config')
        ? { liveAvailable: true, mediaAvailable: true, videoAvailable: true }
        : { content_summary: '一条关心。', possible_meanings: ['也许想问问近况。'], reply_tip: '先接住关心。', reply: '爸，谢谢你惦记。', note: '测试结果。' };
      queueMicrotask(() => options.success({ statusCode: 200, data }));
    },
    pageScrollTo() {}
  };
  const pagePath = require.resolve('../clients/wechat-miniprogram/pages/index/index.js');
  delete require.cache[pagePath];
  require(pagePath);
  t.after(() => {
    delete require.cache[pagePath];
    delete globalThis.Page;
    delete globalThis.wx;
  });
  const page = { ...definition, data: structuredClone(definition.data), setData(update) { Object.assign(this.data, update); } };
  page.setData({ apiBase: 'http://127.0.0.1:4173' });
  await page.checkConnection();
  page.setData({ sourceText: '天凉了，记得加衣服。' });
  await page.generateReply();
  assert.equal(page.data.connectionLabel, '真实 AI · 在线生成');
  assert.equal(requests.length, 2);
  assert.deepEqual(requests[1].data, { text: '天凉了，记得加衣服。', recipient: 'auto', tone: 'warm', mode: 'live', variant: 0 });
  assert.equal(page.data.replyText, '爸，谢谢你惦记。');
  assert.equal(page.data.busy, false);
});

test('a repeated tap during startup connection sends only one reply request', async (t) => {
  let definition;
  const requests = [];
  globalThis.Page = value => { definition = value; };
  globalThis.wx = {
    getStorageSync: () => '',
    request(options) {
      requests.push(options);
      if (!options.url.endsWith('/api/config')) {
        queueMicrotask(() => options.success({ statusCode: 200, data: { content_summary: '一条关心。', possible_meanings: ['也许想问问近况。'], reply_tip: '先接住关心。', reply: '谢谢你惦记。', note: '测试结果。' } }));
      }
    },
    pageScrollTo() {}
  };
  const pagePath = require.resolve('../clients/wechat-miniprogram/pages/index/index.js');
  delete require.cache[pagePath];
  require(pagePath);
  t.after(() => {
    delete require.cache[pagePath];
    delete globalThis.Page;
    delete globalThis.wx;
  });
  const page = { ...definition, data: structuredClone(definition.data), setData(update) { Object.assign(this.data, update); } };
  page.setData({ apiBase: 'http://127.0.0.1:4173', sourceText: '最近降温，记得加衣服。' });
  const first = page.generateReply();
  await page.generateReply();
  assert.equal(page.data.busy, true);
  assert.equal(requests.length, 1);
  requests[0].success({ statusCode: 200, data: { liveAvailable: true, mediaAvailable: true, videoAvailable: true } });
  await first;
  assert.equal(requests.length, 2);
  assert.equal(page.data.replyText, '谢谢你惦记。');
  assert.equal(page.data.busy, false);
});

test('mini program opens WeChat chat media and phone media directly, then clears stale content', (t) => {
  let definition;
  let chatOptions;
  let mediaOptions;
  globalThis.Page = value => { definition = value; };
  globalThis.wx = {
    chooseMessageFile(options) { chatOptions = options; },
    chooseMedia(options) { mediaOptions = options; }
  };
  const pagePath = require.resolve('../clients/wechat-miniprogram/pages/index/index.js');
  delete require.cache[pagePath];
  require(pagePath);
  t.after(() => {
    delete require.cache[pagePath];
    delete globalThis.Page;
    delete globalThis.wx;
  });
  const page = { ...definition, data: structuredClone(definition.data), setData(update) { Object.assign(this.data, update); } };
  const selected = [];
  page.acceptFile = file => selected.push(file);
  page.chooseFromChat();
  assert.equal(chatOptions.count, 1);
  assert.equal(chatOptions.type, 'all');
  chatOptions.fail({ errMsg: 'chooseMessageFile:fail unavailable' });
  assert.match(page.data.error, /普通文字.*复制/);
  chatOptions.success({ tempFiles: [{ path: 'wxfile://chat.mp4' }] });
  page.chooseMedia();
  assert.deepEqual(mediaOptions.mediaType, ['image', 'video']);
  assert.deepEqual(mediaOptions.sourceType, ['album', 'camera']);
  mediaOptions.success({ type: 'image', tempFiles: [{ tempFilePath: 'wxfile://album.jpg', size: 100 }] });
  assert.deepEqual(selected, [{ path: 'wxfile://chat.mp4' }, { path: 'wxfile://album.jpg', size: 100, fileType: 'image' }]);

  page.setData({ sourceText: '旧内容', sourceCount: 3, attachment: { name: 'old.jpg' }, attachmentDetail: '1 MB', result: { reply: '旧回复' }, replyText: '旧回复' });
  page.clearInput();
  assert.equal(page.data.sourceText, '');
  assert.equal(page.data.sourceCount, 0);
  assert.equal(page.data.attachment, null);
  assert.equal(page.data.result, null);
  assert.equal(page.data.replyText, '');
});

test('a stale mini program connection cannot overwrite newly saved server settings', async (t) => {
  let definition;
  const pending = [];
  globalThis.Page = value => { definition = value; };
  globalThis.wx = {
    getStorageSync: () => '',
    request(options) { pending.push(options); }
  };
  const pagePath = require.resolve('../clients/wechat-miniprogram/pages/index/index.js');
  delete require.cache[pagePath];
  require(pagePath);
  t.after(() => {
    delete require.cache[pagePath];
    delete globalThis.Page;
    delete globalThis.wx;
  });
  const page = { ...definition, data: structuredClone(definition.data), setData(update) { Object.assign(this.data, update); } };
  page.setData({ apiBase: 'https://old.example.com' });
  const oldConnection = page.checkConnection();
  page.setData({ apiBase: 'https://new.example.com' });
  const newConnection = page.checkConnection();
  pending[1].success({ statusCode: 200, data: { liveAvailable: false, mediaAvailable: false, videoAvailable: false } });
  await newConnection;
  pending[0].success({ statusCode: 200, data: { liveAvailable: true, mediaAvailable: true, videoAvailable: true } });
  await oldConnection;
  assert.equal(page.data.connectionLabel, '本地示例 · 非实时 AI');
  assert.equal(page.data.liveAvailable, false);
  assert.equal(page.data.connecting, false);
});

test('mini program generates, edits and copies text in offline phone mode', async (t) => {
  let definition;
  let copied = '';
  globalThis.Page = value => { definition = value; };
  globalThis.wx = {
    getStorageSync: () => '',
    pageScrollTo() {},
    setClipboardData({ data, success }) { copied = data; success(); },
    exitMiniProgram() {}
  };
  const pagePath = require.resolve('../clients/wechat-miniprogram/pages/index/index.js');
  delete require.cache[pagePath];
  require(pagePath);
  t.after(() => {
    delete require.cache[pagePath];
    delete globalThis.Page;
    delete globalThis.wx;
  });
  const page = { ...definition, data: structuredClone(definition.data), setData(update) { Object.assign(this.data, update); } };
  page.setData({ sourceText: '妈说今天降温，记得多穿衣服。' });
  await page.generateReply();
  assert.equal(page.data.connectionLabel, '离线文字体验');
  assert.equal(page.data.result.generation_source, 'templates');
  page.onReplyInput({ detail: { value: '谢谢你惦记，我会照顾好自己。' } });
  page.copyReply();
  assert.equal(copied, '谢谢你惦记，我会照顾好自己。');
});
