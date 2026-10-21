// Real model calls for reply style. This does not test WeChat extraction.
import { mkdir, writeFile } from 'node:fs/promises';
import { once } from 'node:events';
import assert from 'node:assert/strict';
import { createServer } from '../server.mjs';

const server = createServer({ config: {
  llmKey: process.env.LLM_API_KEY, llmBaseUrl: process.env.LLM_BASE_URL,
  llmModel: process.env.LLM_MODEL, timeoutLlmMs: 30000
} });
server.listen(0, '127.0.0.1'); await once(server, 'listening');
const report = { checkedAt: new Date().toISOString(), scope: '真实模型的文字回复风格测试；不是微信视频链接、音轨或评论抓取验收。', cases: [] };
const cases = [
  { name: '真实微信视频说明摘录', text: '妈分享的视频说明：10月19日，广东，男子分享：鼻炎的人永远不要让自己有凉的感觉，平时穿立领的衣服、穿长袜，让身体保持微微热。以上是页面说明，不是完整视频转写。', context: '回复妈妈。只谈实际给出的内容，别写谢谢惦记，别背书治疗方法。' },
  { name: '日常提醒测试文本', text: '今天降温了，多穿点，别总熬夜。', context: '回复妈妈，平常说话就行，不要客套。' },
  { name: '普通分享测试文本', text: '这篇分享说旅行时每天赶五六个景点，一直拍照打卡，回来却连街边吃过什么都记不住。作者建议留出半天在一条街上慢慢逛。', context: '回复朋友，具体接话，不要询问人生感触。' }
];
try {
  for (const sample of cases) {
    const response = await fetch(`http://127.0.0.1:${server.address().port}/api/reply`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ...sample, recipient: 'auto', mode: 'live' }), signal: AbortSignal.timeout(40000)
    });
    const result = await response.json();
    assert.equal(response.status, 200, result.error);
    assert.doesNotMatch(result.reply, /关心收到|心意我收到|谢谢.*分享|值得深思|我看完了|我查过/);
    if (sample.name === '真实微信视频说明摘录') assert.doesNotMatch(result.reply, /有道理|确实有效|应该有用/);
    if (sample.name === '日常提醒测试文本') assert.doesNotMatch(result.reply, /我.*会|尽量.*熬夜/);
    report.cases.push({ name: sample.name, reply: result.reply, model: result.meta?.generation_model, elapsedMs: result.meta?.total_ms });
  }
  await mkdir(new URL('../work/', import.meta.url), { recursive: true });
  await writeFile(new URL('../work/reply-style-real.json', import.meta.url), JSON.stringify(report, null, 2));
  console.log(JSON.stringify(report, null, 2));
} finally { server.close(); server.closeAllConnections(); }
