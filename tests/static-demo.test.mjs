import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

test('public static demo completes a private, editable text reply without an API', async () => {
  const source = await readFile(new URL('../public/demo.js', import.meta.url), 'utf8');
  const context = { window: {} };
  vm.runInNewContext(source, context);
  const demo = context.window.HaoHaoHuiDemo;
  assert.equal(demo.config.liveAvailable, false);
  const result = demo.reply({ text: '妈转发说这个偏方能治愈高血压。', recipient: 'mother', tone: 'warm' });
  assert.match(result.reply, /^妈，/);
  assert.match(result.reply, /核实/);
  assert.equal(result.generation_source, 'templates');
  assert.match(result.note, /未上传内容/);
  assert.notEqual(demo.reply({ text: '多穿点，注意保暖', variant: 0 }).reply, demo.reply({ text: '多穿点，注意保暖', variant: 1 }).reply);
  assert.match(demo.reply({ text: '你好', context: '回短一点' }).note, /真实 AI/);
  assert.throws(() => demo.reply({ text: '', recipient: 'mother', tone: 'warm' }));
  assert.throws(() => demo.reply({ text: '视频', attachment: { kind: 'video' } }));
});

test('public page assets use repository-relative URLs for GitHub Pages', async () => {
  const [html, css, app] = await Promise.all([
    readFile(new URL('../public/index.html', import.meta.url), 'utf8'),
    readFile(new URL('../public/styles.css', import.meta.url), 'utf8'),
    readFile(new URL('../public/app.js', import.meta.url), 'utf8')
  ]);
  assert.match(html, /href="\.\/styles\.css"/);
  assert.match(html, /src="\.\/demo\.js"/);
  assert.match(html, /src="\.\/history\.js"/);
  assert.match(html, /src="\.\/app\.js"/);
  assert.match(html, /<h1 id="hero-title">知意<\/h1>/);
  assert.doesNotMatch(css, /url\('\/assets\//);
  assert.doesNotMatch(app, /\?\.|\?\?/, 'Android 9 WebView 68 must parse the interaction script');
});

test('local history keeps safe summaries and the latest 20 replies', async () => {
  const source = await readFile(new URL('../public/history.js', import.meta.url), 'utf8');
  const values = new Map();
  const storage = {
    getItem: key => values.has(key) ? values.get(key) : null,
    setItem: (key, value) => values.set(key, value),
    removeItem: key => values.delete(key)
  };
  const context = { window: {} };
  vm.runInNewContext(source, context);
  const history = context.window.ZhiyiHistory;
  for (let index = 0; index < 22; index += 1) {
    history.add(storage, { id: `item-${index}`, createdAt: index + 1, recipient: 'mother', tone: 'warm', source: `消息 ${index}`, summary: '一条关心。', possibleMeanings: ['想问近况。'], replyTip: '先接住关心。', reply: `谢谢关心 ${index}` });
  }
  assert.equal(history.read(storage).length, 20);
  assert.equal(history.read(storage)[0].id, 'item-21');
  history.updateReply(storage, 'item-21', '我会照顾好自己。');
  assert.equal(history.read(storage)[0].reply, '我会照顾好自己。');
  const entry = history.read(storage)[0];
  assert.equal(entry.context, undefined, 'legacy entries remain readable for migration');
  history.add(storage, { ...entry, context: '这是爸爸发来的，回短一点' });
  assert.equal(history.read(storage)[0].context, '这是爸爸发来的，回短一点');
  assert.throws(() => history.add(storage, { ...entry, context: '好'.repeat(201) }));
  assert.throws(() => history.add(storage, { ...entry, context: { instruction: 'invalid' } }));
  assert.throws(() => history.add(storage, { id: 'unsafe', createdAt: 1, recipient: 'stranger', tone: 'warm', source: '', summary: '', possibleMeanings: [], replyTip: '', reply: '' }));
  history.clear(storage);
  assert.equal(history.read(storage).length, 0);
});
