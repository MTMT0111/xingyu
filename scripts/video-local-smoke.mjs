import assert from 'node:assert/strict';
import childProcess from 'node:child_process';
import { createHash } from 'node:crypto';
import { once } from 'node:events';
import { access, readFile, readdir, writeFile } from 'node:fs/promises';
import { syncBuiltinESMExports } from 'node:module';
import { tmpdir } from 'node:os';
import { basename, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createServer } from '../server.mjs';
import { extractVideo } from '../lib/media.mjs';

const project = fileURLToPath(new URL('../', import.meta.url));
const fixturePath = process.env.VIDEO_TEST_FILE || resolve(project, 'tests/fixtures/parent-message.mp4');
for (const name of ['MEDIA_PYTHON', 'WHISPER_MODEL_PATH']) {
  assert.ok(process.env[name]?.trim(), `Configure ${name} before running test:video; see MEDIA.md.`);
}
const config = {
  typesafeKey: 'fixture-only-not-a-real-jev-key', llmKey: 'fixture-only-not-a-real-llm-key',
  llmBaseUrl: 'https://vision-fixture.example.test/v1', llmModel: 'explicitly-mocked-vision-and-reply',
  jevModel: 'explicitly-mocked-jev', llmVision: true, publicOrigin: '', trialPassword: '',
  mediaPython: process.env.MEDIA_PYTHON,
  mediaPythonPath: process.env.MEDIA_PYTHONPATH || '',
  whisperModel: process.env.WHISPER_MODEL_PATH
};
const visualFixture = { visual_summary: '黄色星球旁有蓝色字幕，内容是吃饭、添衣和给妈妈打电话的提醒。', visible_text: '来自妈妈的一段关心。孩子，晚上记得吃饭。天气凉了，多穿一件衣服。有空时，给妈妈打个电话。' };
const replyFixture = { content_summary: '视频提醒孩子吃饭、天凉添衣，并希望有空给妈妈打电话。', possible_meanings: ['也许是在惦记你的日常，也想听听你的声音。'], reply_tip: '回应关心，通话时间没确定就不要先承诺。', reply: '妈，提醒收到啦，谢谢你惦记我。你最近怎么样？我们找个都方便的时间聊聊呀。' };
const decisionFixture = { model: 'explicitly-mocked-jev', answers: { topic: { type: 'choice', choice: 'care', confidence: 0.9, probabilities: { care: 1, invitation: 0, pressure: 0, sharing: 0, conflict: 0, unclear: 0 } }, health_claim: { type: 'noul', noul: 0 } } };
const json = value => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
const completion = value => json({ model: config.llmModel, choices: [{ finish_reason: 'stop', message: { content: JSON.stringify(value) } }] });
const directories = async () => new Set((await readdir(tmpdir(), { withFileTypes: true })).filter(item => item.isDirectory() && item.name.startsWith('xingyu-video-')).map(item => item.name));
const beforeDirectories = await directories();
const calls = [];
const spawned = [];
const startedAt = new Date().toISOString();
const started = performance.now();
let observeSpawn;
const realSpawn = childProcess.spawn;
// Observe the real process; no local decoding or ASR implementation is replaced.
childProcess.spawn = (...args) => {
  const child = realSpawn(...args);
  if (args[0] === config.mediaPython && args[1]?.[0]?.endsWith('process-media.py')) {
    const record = { child, pid: child.pid, directory: dirname(args[1][1]) };
    spawned.push(record);
    observeSpawn?.(record);
  }
  return child;
};
syncBuiltinESMExports();
const server = createServer({ config, fetchImpl: async (url, options) => {
  const body = JSON.parse(options.body);
  calls.push({ url: String(url), body });
  if (calls.length === 1) {
    assert.equal(String(url), config.llmBaseUrl + '/chat/completions');
    const images = body.messages[1].content.filter(item => item.type === 'image_url');
    assert.equal(images.length, 6);
    assert.ok(images.every(item => item.image_url.url.startsWith('data:image/jpeg;base64,')));
    return completion(visualFixture);
  }
  if (calls.length === 2) {
    assert.equal(String(url), 'https://api.typesafe.ai/v1/systemone');
    const evidence = JSON.parse(body.state.message_text).source_evidence;
    assert.equal(evidence.kind, 'video');
    assert.equal(evidence.frame_count, 6);
    assert.equal(evidence.audio_status, 'transcribed');
    assert.ok(['孩子', '吃饭', '衣服', '妈妈'].filter(word => evidence.transcript.includes(word)).length >= 3);
    assert.equal(evidence.visible_text, visualFixture.visible_text);
    return json(decisionFixture);
  }
  assert.equal(calls.length, 3, 'Unexpected cloud request; all cloud traffic must be mocked.');
  assert.equal(String(url), config.llmBaseUrl + '/chat/completions');
  assert.deepEqual(JSON.parse(body.messages[1].content).source_evidence, JSON.parse(calls[1].body.state.message_text).source_evidence);
  return completion(replyFixture);
} });

try {
  const videoBytes = await readFile(fixturePath);
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  const origin = `http://127.0.0.1:${server.address().port}`;
  const availability = await (await fetch(origin + '/api/config')).json();
  assert.equal(availability.videoAvailable, true);
  const response = await fetch(origin + '/api/reply', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: AbortSignal.timeout(125000),
    body: JSON.stringify({ text: '妈妈发来一段小视频', mode: 'live', recipient: 'mother', attachment: { kind: 'video', name: '非隐私中文语音测试.mp4', data: videoBytes.toString('base64') } })
  });
  const result = await response.json();
  assert.equal(response.status, 200, JSON.stringify(result));
  assert.equal(calls.length, 3);
  assert.equal(spawned.length, 1);
  assert.equal(result.content_summary, replyFixture.content_summary);
  assert.equal(result.reply, replyFixture.reply);
  assert.deepEqual(result.evidence, JSON.parse(calls[1].body.state.message_text).source_evidence);
  assert.equal(result.decision_source, 'jev');
  assert.equal(result.generation_source, 'llm');
  assert.ok(result.evidence.duration > 11 && result.evidence.duration < 12);
  assert.doesNotMatch(JSON.stringify(result), /fixture-only-not-a-real|data:image|;base64/);
  const httpSeconds = Number(((performance.now() - started) / 1000).toFixed(3));

  const controller = new AbortController();
  const didSpawn = new Promise(resolveSpawn => { observeSpawn = resolveSpawn; });
  const cancellationStarted = performance.now();
  const extraction = extractVideo({ kind: 'video', mime: 'video/mp4', bytes: videoBytes }, config, controller.signal);
  const rejection = assert.rejects(extraction, error => error.mediaCode === 'MEDIA_CANCELLED');
  const cancelledProcess = await didSpawn;
  observeSpawn = undefined;
  await once(cancelledProcess.child, 'spawn');
  controller.abort();
  await rejection;
  assert.ok(cancelledProcess.child.killed);
  assert.throws(() => process.kill(cancelledProcess.pid, 0), error => error.code === 'ESRCH');
  const cancellationSeconds = Number(((performance.now() - cancellationStarted) / 1000).toFixed(3));
  const afterDirectories = await directories();
  for (const record of spawned) {
    assert.equal(beforeDirectories.has(basename(record.directory)), false);
    assert.equal(afterDirectories.has(basename(record.directory)), false);
    await assert.rejects(access(record.directory), error => error.code === 'ENOENT');
  }
  const report = {
    startedAt, completedAt: new Date().toISOString(), status: 'passed',
    localASRReal: true, cloudModelsMocked: true, fullLiveModelPass: false,
    scope: 'Real local video decoding and Whisper ASR through HTTP business flow; visual LLM, Jev and reply LLM are explicit fixtures. This is not a complete real cloud-model test.',
    fixture: { filename: basename(fixturePath), bytes: videoBytes.length, sha256: createHash('sha256').update(videoBytes).digest('hex'), provenance: 'Local Windows SAPI Huihui Chinese synthetic speech with generated subtitle video; no private user media.' },
    runtime: { python: config.mediaPython, pythonPath: config.mediaPythonPath, whisperModel: config.whisperModel, model: 'Systran/faster-whisper-base', device: 'cpu', computeType: 'int8' },
    http: { status: response.status, seconds: httpSeconds, cloudFixtureCalls: calls.length, order: ['vision_fixture', 'jev_fixture', 'reply_fixture'], frameCount: result.evidence.frame_count, duration: result.evidence.duration, audioStatus: result.evidence.audio_status, realTranscript: result.evidence.transcript, contentSummary: result.content_summary, evidenceReachedJevAndReply: true },
    cancellation: { code: 'MEDIA_CANCELLED', seconds: cancellationSeconds, childProcessExited: true },
    cleanup: { ownedTemporaryDirectories: spawned.length, ownedDirectoriesRemaining: 0, unrelatedDirectoriesTouched: false, preexistingDirectories: beforeDirectories.size, afterDirectories: afterDirectories.size },
    limitations: ['ASR transcript contains homophone errors; users should be allowed to review it.', 'No real cloud provider was contacted in this test.', 'This test does not validate WeChat on a physical phone.']
  };
  await writeFile(resolve(project, 'local-video-test-results.json'), JSON.stringify(report, null, 2) + '\n', 'utf8');
  console.log(JSON.stringify(report, null, 2));
} finally {
  childProcess.spawn = realSpawn;
  syncBuiltinESMExports();
  await new Promise(resolveClose => { server.close(resolveClose); server.closeAllConnections(); });
}
