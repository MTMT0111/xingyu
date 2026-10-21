import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { once } from 'node:events';
import { EvidenceCache, saveDeepSeekKey } from '../lib/local-model.mjs';
import { createServer } from '../server.mjs';

test('evidence reuse is bounded, isolated from mutation and expires without retaining chat context', () => {
  let now = 0;
  const cache = new EvidenceCache({ now: () => now, ttlMs: 100, limit: 2 });
  const original = { transcript: '原文', comments: [{ text: '评论' }] };
  cache.set('one', original); original.comments[0].text = 'changed';
  assert.equal(cache.get('one').comments[0].text, '评论');
  cache.get('one').comments.length = 0;
  assert.equal(cache.get('one').comments.length, 1);
  cache.set('two', {}); cache.set('three', {});
  assert.equal(cache.get('one'), null);
  now = 100; assert.equal(cache.get('two'), null);
  cache.set('large', { transcript: 'x'.repeat(1024 * 1024) }); assert.equal(cache.get('large'), null);
  cache.clear(); assert.equal(cache.get('three'), null);
});

test('key persistence replaces exact assignments while retaining unrelated local settings', async t => {
  const directory = await mkdtemp(join(tmpdir(), 'zhiyi-settings-')); t.after(() => rm(directory, { recursive: true, force: true }));
  const path = join(directory, '.env');
  await writeFile(path, '# retained\nOTHER_KEY="unchanged"\nexport LLM_API_KEY = "old"\nLLM_MODEL=old\nLLM_API_KEY=duplicate\n');
  await saveDeepSeekKey(path, 'test-key-1234567890');
  const actual = await readFile(path, 'utf8');
  assert.match(actual, /OTHER_KEY="unchanged"/);
  assert.equal(actual.match(/LLM_API_KEY/g).length, 1);
  assert.match(actual, /LLM_MODEL="deepseek-flash"/);
  await assert.rejects(saveDeepSeekKey(path, 'invalid\nOTHER=oops'));
  assert.equal(await readFile(path, 'utf8'), actual);
});

test('local model setup verifies before saving and never returns keys or upstream errors', async t => {
  const directory = await mkdtemp(join(tmpdir(), 'zhiyi-model-')); t.after(() => rm(directory, { recursive: true, force: true }));
  const path = join(directory, '.env'); await writeFile(path, 'LLM_API_KEY=original\nOTHER=preserved\n');
  let calls = 0;
  const server = createServer({ envPath: path, config: { llmKey: '', llmBaseUrl: '', llmModel: '', publicOrigin: '' }, fetchImpl: (url, options) => {
    calls++; assert.equal(url, 'https://api.deepseek.com/chat/completions');
    assert.equal(JSON.parse(options.body).model, 'deepseek-flash');
    return options.headers.Authorization.endsWith('invalid-1234567890') ? new Response('private upstream details', { status: 401 }) : new Response(JSON.stringify({ choices: [{ message: { content: 'OK' } }] }));
  } });
  server.listen(0, '127.0.0.1'); await once(server, 'listening'); t.after(() => { server.close(); server.closeAllConnections(); });
  const base = `http://127.0.0.1:${server.address().port}`;
  const save = (apiKey, headers = {}) => fetch(base + '/api/local/model', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Zhiyi-Local-Config': '1', ...headers }, body: JSON.stringify({ apiKey }) });
  assert.equal((await save('test-key-1234567890', { Origin: 'https://foreign.test' })).status, 403);
  assert.equal((await save('test-key-1234567890', { 'X-Zhiyi-Local-Config': '' })).status, 403);
  assert.equal((await save('bad')).status, 400); assert.equal(calls, 0);
  const failed = await save('invalid-1234567890'); assert.equal(failed.status, 400);
  assert.doesNotMatch(await failed.text(), /private|invalid-123/);
  assert.equal(await readFile(path, 'utf8'), 'LLM_API_KEY=original\nOTHER=preserved\n');
  const saved = await save('test-key-1234567890'); assert.equal(saved.status, 200);
  assert.doesNotMatch(await saved.text(), /test-key/);
  const state = await (await fetch(base + '/api/local/model')).json();
  assert.deepEqual(state, { configured: true, model: 'deepseek-flash' });
  assert.match(await readFile(path, 'utf8'), /test-key-1234567890/);
  assert.doesNotMatch(await (await fetch(base + '/api/config')).text(), /test-key/);
});
