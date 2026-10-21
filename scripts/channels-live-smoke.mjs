import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { once } from 'node:events';
import { createHash } from 'node:crypto';
import { createServer } from '../server.mjs';

// The input file stays local: WeChat URLs may contain signed session parameters.
const bySearch = process.argv[2] === '--search';
const url = bySearch ? '' : (await readFile(process.argv[2] || 'work/channel-live-url.txt', 'utf8')).trim();
const server = createServer();
server.listen(0, '127.0.0.1');
await once(server, 'listening');
try {
  let selected;
  if (bySearch) {
    const response = await fetch(`http://127.0.0.1:${server.address().port}/api/channels/search?q=${encodeURIComponent(process.argv[3])}`);
    const data = await response.json();
    assert.equal(response.status, 200, data.error);
    selected = data.items.find(item => item.object_id === process.argv[4]);
    assert.ok(selected, 'The explicitly selected real video must appear in the search results');
  }
  const response = await fetch(`http://127.0.0.1:${server.address().port}/api/reply`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ text: selected?.title ?? url, ...(selected ? { channel: selected } : {}), mode: 'live', recipient: 'auto', tone: 'warm', context: '妈妈分享的视频，接住一个具体细节，日常聊天，不要套话，不要替我编造经历或承诺。' }),
    signal: AbortSignal.timeout(240000)
  });
  const result = await response.json();
  assert.equal(response.status, 200, `${result.code || response.status}: ${result.error || '视频读取失败'}`);
  assert.equal(result.source_kind, 'channels');
  assert.equal(result.generation_source, 'llm');
  assert.ok(result.evidence?.duration > 0 && result.evidence?.frame_count > 0);
  const evidence = result.evidence;
  const proof = { testedAt: new Date().toISOString(), inputHash: createHash('sha256').update(selected?.object_id ?? url).digest('hex'),
    entry: bySearch ? 'real-backend-search' : 'real-share-link',
    realWeChat: true, duration: evidence.duration, frames: evidence.frame_count,
    audioStatus: evidence.audio_status, transcriptCharacters: evidence.transcript.length,
    commentCount: evidence.comments.length, commentStatus: evidence.comment_status,
    reply: result.reply, model: result.meta?.generation_model, result };
  await mkdir('work', { recursive: true });
  await writeFile('work/channels-real-result.json', JSON.stringify(proof, null, 2));
  console.log(JSON.stringify({ ...proof, result: undefined }, null, 2));
} finally { server.closeAllConnections(); server.close(); }
