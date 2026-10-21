import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile, readFile, access, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { once } from 'node:events';
import { storeVideoStream } from '../lib/video-file.mjs';
import { createServer } from '../server.mjs';

const video = await readFile(new URL('./fixtures/parent-message.mp4', import.meta.url));
test('streamed media keeps bounded bytes on disk, validates the actual format and disposes owned files', async () => {
  const result = await storeVideoStream(new Response(video).body, new AbortController().signal);
  assert.equal(result.bytes, undefined);
  assert.deepEqual(await readFile(result.path), video);
  await result.dispose();
  await assert.rejects(access(result.path));
  await assert.rejects(storeVideoStream(new Response('<html>not video</html>').body, new AbortController().signal), /视频/);
});

test('cancelling a stalled streamed video stops its reader', async () => {
  const controller = new AbortController();
  let cancelled = false;
  const stream = new ReadableStream({ cancel() { cancelled = true; } });
  const result = storeVideoStream(stream, controller.signal);
  setTimeout(() => controller.abort(), 25);
  await assert.rejects(result);
  assert.equal(cancelled, true);
});

test('a streamed upload is consumed once; every long-video segment reaches analysis and temporary input is removed', async t => {
  const root = await mkdtemp(join(tmpdir(), 'zhiyi-test-model-'));
  await writeFile(join(root, 'model.bin'), 'fixture only');
  t.after(() => rm(root, { recursive: true, force: true }));
  let sourcePath; let calls = 0; let fail = false;
  const server = createServer({ config: { llmKey: 'test', llmBaseUrl: 'https://llm.example.test', llmModel: 'fixture', llmVision: true, mediaPython: process.execPath, whisperModel: root },
    extractVideoImpl: async attachment => {
      sourcePath = attachment.path;
      assert.deepEqual(await readFile(sourcePath), video);
      const frames = [{ time: 0, dataUrl: 'data:image/jpeg;base64,fixture' }];
      return { duration: 240, frames: [...frames, ...frames], transcript: '前半段。后半段。', audioStatus: 'transcribed', segments: [
        { start: 0, end: 180, frames, transcript: '前半段讲计划' }, { start: 180, end: 240, frames, transcript: '后半段说计划会改变' }
      ] };
    }, fetchImpl: async (_, options) => {
      if (fail) return new Response('unavailable', { status: 503 });
      const body = JSON.parse(options.body); calls++;
      let answer;
      if (calls < 3) {
        assert.match(JSON.stringify(body.messages), calls === 1 ? /前半段讲计划/ : /后半段说计划会改变/);
        answer = { visual_summary: calls === 1 ? '安排计划' : '计划需要调整', visible_text: '' };
      } else {
        const evidence = JSON.parse(body.messages[1].content).source_evidence;
        assert.match(evidence.visual_summary, /安排计划[\s\S]*计划需要调整/);
        answer = { content_summary: '谈计划与调整。', possible_meanings: ['可能想聊变化。', '也许认同留出调整的空间。'], reply_tip: '接住具体例子。', reply: '计划有了还得留点调整的余地。' };
      }
      return Response.json({ choices: [{ message: { content: JSON.stringify(answer) } }] });
    } });
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  t.after(() => { server.closeAllConnections(); server.close(); });
  const origin = `http://127.0.0.1:${server.address().port}`;
  const upload = () => fetch(origin + '/api/video?name=test.mp4', { method: 'POST', body: video });
  const post = id => fetch(origin + '/api/reply', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ text: '', mode: 'live', videoUpload: id }) });
  const id = (await (await upload()).json()).id;
  const response = await post(id); const result = await response.json();
  assert.equal(response.status, 200, JSON.stringify(result));
  assert.equal(result.evidence.duration, 240);
  assert.equal(result.evidence.segments.at(-1).end, 240);
  assert.match(result.evidence.segments.at(-1).transcript, /后半段/);
  await new Promise(resolve => setTimeout(resolve, 30));
  await assert.rejects(access(sourcePath));
  assert.equal((await post(id)).status, 400);
  fail = true;
  const retryId = (await (await upload()).json()).id;
  assert.equal((await post(retryId)).status, 502);
  await new Promise(resolve => setTimeout(resolve, 30));
  await assert.rejects(access(sourcePath));
});
