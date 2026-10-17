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
  assert.match(html, /src="\.\/app\.js"/);
  assert.doesNotMatch(css, /url\('\/assets\//);
  assert.doesNotMatch(app, /\?\.|\?\?/, 'Android 9 WebView 68 must parse the interaction script');
});
