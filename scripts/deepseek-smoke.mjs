// Two bounded real calls using synthetic fixtures; no Jev call or automatic retry.
import { readFile, writeFile } from 'node:fs/promises';
import { generationPrompt, validateGeneration } from '../lib/translate.mjs';
import { visionPrompt, validateVisualEvidence } from '../lib/media.mjs';

const report = {
  checkedAt: new Date().toISOString(), provider: 'DeepSeek', model: process.env.LLM_MODEL,
  status: 'running', realApiCalls: 0, jevTested: false, fullLiveModelPass: false,
  scope: 'Official DeepSeek text generation and vision with synthetic fixtures; not the complete Jev/application chain.',
  cases: [],
};
function category(error) {
  if ([401, 403].includes(error.status)) return 'AUTH_FAILED';
  if (error.status === 402) return 'INSUFFICIENT_BALANCE';
  if (error.status === 429) return 'RATE_LIMITED';
  if ([400, 404, 422].includes(error.status)) return 'PROVIDER_CONFIG';
  if (error.status >= 500) return 'PROVIDER_UNAVAILABLE';
  if (error.name === 'TimeoutError') return 'TIMEOUT';
  if (error instanceof TypeError) return 'NETWORK';
  return 'INVALID_RESPONSE';
}
async function evaluate(name, messages, validate, maxTokens) {
  const record = { name, status: 'running' };
  report.cases.push(record);
  const started = performance.now();
  try {
    report.realApiCalls++;
    const response = await fetch('https://api.deepseek.com/chat/completions', {
      method: 'POST', redirect: 'error', signal: AbortSignal.timeout(30000),
      headers: { Authorization: `Bearer ${process.env.LLM_API_KEY}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({ model: process.env.LLM_MODEL, messages, thinking: { type: 'disabled' },
        response_format: { type: 'json_object' }, max_tokens: maxTokens, stream: false }),
    });
    record.httpStatus = response.status;
    if (!response.ok) { await response.body?.cancel(); throw Object.assign(new Error('Provider rejected request'), { status: response.status }); }
    const data = await response.json();
    if (data.choices?.[0]?.finish_reason === 'length') throw new Error('Truncated response');
    record.output = validate(JSON.parse(data.choices?.[0]?.message?.content));
    record.model = typeof data.model === 'string' ? data.model.slice(0, 100) : null;
    record.usage = Object.fromEntries(Object.entries(data.usage ?? {}).filter(([key, value]) => /^[a-z_]{1,40}$/.test(key) && Number.isFinite(value)));
    record.status = 'passed';
    return true;
  } catch (error) {
    record.status = 'failed'; record.failure = category(error);
    return false;
  } finally { record.elapsedMs = Math.round(performance.now() - started); }
}
try {
  if (!process.env.LLM_API_KEY?.trim() || process.env.LLM_BASE_URL !== 'https://api.deepseek.com' || process.env.LLM_MODEL !== 'deepseek-flash') {
    report.status = 'blocked'; report.failure = 'OFFICIAL_CONFIGURATION_REQUIRED';
  } else {
    const textOK = await evaluate('parent-message-text', [
      { role: 'system', content: generationPrompt('uncertain', 'warm', 'mother') },
      { role: 'user', content: JSON.stringify({ message_text: '今天降温了，出门记得加件衣服，忙也要好好吃饭。', context: '', recipient: 'mother', variation: 0 }) },
    ], value => validateGeneration(value, 'uncertain'), 800);
    if (textOK) {
      const bytes = await readFile(new URL('../tests/fixtures/share-image.png', import.meta.url));
      await evaluate('synthetic-image-vision', [
        { role: 'system', content: visionPrompt },
        { role: 'user', content: [{ type: 'text', text: '识别这张图片的内容。' }, { type: 'image_url', image_url: { url: `data:image/png;base64,${bytes.toString('base64')}` } }] },
      ], validateVisualEvidence, 1200);
    }
    report.status = report.cases.length === 2 && report.cases.every(item => item.status === 'passed') ? 'passed' : 'failed';
  }
} catch (error) { report.status = 'failed'; report.failure = category(error); }
await writeFile(new URL('../deepseek-test-results.json', import.meta.url), JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report));
if (report.status !== 'passed') process.exitCode = 2;
