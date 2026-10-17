// Run: node --env-file-if-exists=.env scripts/live-smoke.mjs
import { createRequire } from 'node:module';
import { once } from 'node:events';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { createServer } from '../server.mjs';
import { validateGeneration } from '../lib/translate.mjs';
import { extractVideo } from '../lib/media.mjs';

const required = ['LLM_API_KEY', 'LLM_BASE_URL', 'LLM_MODEL'];
const useJev = Boolean(process.env.TYPESAFE_API_KEY?.trim());
const report = {
  startedAt: new Date().toISOString(), status: 'running', fullLiveModelPass: false,
  execution: `Real browser → local server → ${useJev ? 'official Jev and configured LLM' : 'configured LLM directly'}; no mocks or automatic retries.`,
  pipeline: useJev ? 'jev_llm' : 'llm_only', jevTested: false,
  limitation: 'Seven synthetic, non-private text/image/video samples and basic checks do not prove comprehensive semantic quality or WeChat device compatibility. Video uses sampled frames and fallible local speech recognition.',
  missingNames: required.filter(name => !process.env[name]?.trim()), cases: [], requestCount: 0
};
const reportPath = new URL('../live-test-results.json', import.meta.url);
const screenshotDir = fileURLToPath(new URL('../screenshots/live/', import.meta.url));
const fixturesDir = fileURLToPath(new URL('../tests/fixtures/', import.meta.url));
const imagePath = `${fixturesDir}/share-image.png`;
const videoPath = `${fixturesDir}/parent-message.mp4`;
const article = ['把日子过成自己的风景。生活不会每天都顺利，工作有忙的时候，事情也有暂时做不成的时候。不要因为别人的一句评价就否定自己，别把所有困难都解释成自己不够努力。',
  '可以停下来喝口水，看看窗外，给自己一点重新整理的时间。休息不是偷懒，今天完成一件小事也值得肯定。把目标分成能做的小步，累了就先放一放，明天再继续。',
  '每个人的节奏都不一样，有人很早找到方向，有人走过很多路才知道喜欢什么。朋友圈只是一小片生活，不必拿自己完整的一天和别人挑出来的一张照片比较。',
  '家人不一定懂你工作的细节，但常常想知道你过得好不好。忙的时候简单说一句就行，不需要编一个完美的近况，也不用为了让人放心把所有烦恼都藏起来。',
  '愿你认真做事，也愿意照顾自己的感受。愿你可以表达不同意见，又给彼此留一点耐心。日子没有标准答案，慢慢找到适合自己的方式，就是一种踏实的进步。',
  '把这段话送给正在努力的你：不必每次都赢，也不必每句话都回答得漂亮。真诚地问候一个人，承认今天有点累，分享一件小事，都能让关系更近一点。'].join('\n\n');
const cases = [
  { id: 'care-mother', recipient: 'mother', text: '今天降温了，出门记得加件衣服，忙也要好好吃饭。' },
  { id: 'invitation-father', recipient: 'father', text: '这周末你有空回家吃饭吗？我想做你爱吃的菜。' },
  { id: 'long-article-elder', recipient: 'elder', text: article },
  { id: 'health-claim', recipient: 'mother', text: '有人说每天喝醋就能治好高血压，你也试试吧。' },
  { id: 'ambiguous', recipient: 'father', text: '在吗？' },
  { id: 'shared-image', recipient: 'elder', text: '长辈分享了这张图，请看清内容后帮我回复。', attachment: { kind: 'image', name: 'share-image.png' } },
  { id: 'spoken-video', recipient: 'mother', text: '妈妈分享了这段视频，请结合画面和声音帮我回复。', attachment: { kind: 'video', name: 'parent-message.mp4' } }
];
let stage = 'configuration', failureCode = 'UNEXPECTED_FAILURE', blocked = false, browser, server, pageErrors = 0;
const check = (condition, code) => { if (!condition) { failureCode = code; throw new Error('Live acceptance failed'); } };
const block = code => { blocked = true; check(false, code); };
const short = (value, max) => typeof value === 'string' ? value.slice(0, max) : null;
function numericUsage(value, depth = 0) {
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (!value || typeof value !== 'object' || Array.isArray(value) || depth > 2) return null;
  return Object.fromEntries(Object.entries(value).filter(([key]) => /^[a-z_]{1,40}$/.test(key))
    .map(([key, item]) => [key, numericUsage(item, depth + 1)]).filter(([, item]) => item !== null));
}

try {
  if (report.missingNames.length) {
    report.status = 'blocked';
    report.failure = { stage, code: 'MISSING_CONFIGURATION' };
    process.exitCode = 2;
  } else {
    stage = 'browser-runtime';
    let chromium;
    try { chromium = createRequire(import.meta.url)(process.env.PLAYWRIGHT_MODULE || 'playwright').chromium; }
    catch { block('BROWSER_AUTOMATION_UNAVAILABLE'); }
    // Override only public access controls; all model configuration and fetch stay real.
    server = createServer({ config: { publicOrigin: '', trialPassword: '' } });
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    const origin = `http://127.0.0.1:${server.address().port}`;
    stage = 'media-preflight';
    const capabilities = await (await fetch(origin + '/api/config')).json();
    report.capabilities = { liveAvailable: capabilities.liveAvailable === true, mediaAvailable: capabilities.mediaAvailable === true, videoAvailable: capabilities.videoAvailable === true };
    if (!report.capabilities.liveAvailable) block('LIVE_NOT_CONFIGURED');
    if (!report.capabilities.mediaAvailable) block('MEDIA_NOT_CONFIGURED');
    if (!report.capabilities.videoAvailable) block('VIDEO_NOT_CONFIGURED');
    // Verify the real local decoder/model before any cloud generation is charged.
    try {
      const local = await extractVideo({ kind: 'video', name: 'parent-message.mp4', mime: 'video/mp4', bytes: await readFile(videoPath) },
        { mediaPython: process.env.MEDIA_PYTHON, mediaPythonPath: process.env.MEDIA_PYTHONPATH, whisperModel: process.env.WHISPER_MODEL_PATH }, AbortSignal.timeout(125000));
      if (local.audioStatus !== 'transcribed' || !local.transcript.trim()) block('VIDEO_SPEECH_PREFLIGHT_FAILED');
      report.localVideoPreflight = { status: 'passed', duration: local.duration, frameCount: local.frames.length, audioStatus: local.audioStatus };
    } catch { block('VIDEO_RUNTIME_OR_FIXTURE_UNAVAILABLE'); }
    stage = 'browser-runtime';
    browser = await chromium.launch({ headless: true, ...(process.env.BROWSER_EXECUTABLE ? { executablePath: process.env.BROWSER_EXECUTABLE } : {}) });
    const context = await browser.newContext({ viewport: { width: 390, height: 844 }, permissions: ['clipboard-read', 'clipboard-write'] });
    context.setDefaultTimeout(12000);
    const canvas = await context.newPage();
    await canvas.setViewportSize({ width: 720, height: 540 });
    await canvas.setContent('<!doctype html><meta charset="utf-8"><style>body{margin:0;background:#fff8da;color:#20446c;font:30px/1.7 "Microsoft YaHei",sans-serif;padding:48px}h1{font-size:40px}p{margin:18px 0}</style><h1>把日子过成自己的风景</h1><p>慢一点也没关系，照顾好自己。</p><p>别和别人比较，每个人都有自己的节奏。</p><p>今天认真吃饭，明天继续向前。</p>');
    await canvas.evaluate(() => document.fonts.ready);
    await mkdir(fixturesDir, { recursive: true });
    await canvas.screenshot({ path: imagePath, fullPage: true });
    await canvas.close();
    const page = await context.newPage();
    page.on('pageerror', () => { pageErrors++; });
    page.on('request', request => { if (new URL(request.url()).pathname === '/api/reply') report.requestCount++; });
    stage = 'initialization';
    await page.goto(origin, { waitUntil: 'networkidle' });
    await page.locator('#mode-controls').waitFor({ state: 'visible' });
    check(await page.locator('#mode-select').inputValue() === 'live', 'LIVE_MODE_NOT_SELECTED');

    for (const sample of cases) {
      stage = sample.id;
      failureCode = 'CASE_RUNTIME_FAILURE';
      const record = { ...sample, status: 'running', realApiConfirmed: false };
      report.cases.push(record);
      if (await page.locator('#remove-attachment').isVisible()) await page.locator('#remove-attachment').click();
      await page.locator('#source-text').fill(sample.text);
      await page.locator('#recipient-select').selectOption(sample.recipient);
      if (sample.attachment) {
        await page.locator('#attachment-input').setInputFiles(sample.attachment.kind === 'image' ? imagePath : videoPath);
        await page.locator('#attachment-preview').waitFor({ state: 'visible' });
        await page.waitForFunction(() => !document.getElementById('translate-button').disabled);
      }
      const started = performance.now();
      const [response] = await Promise.all([
        page.waitForResponse(response => new URL(response.url()).pathname === '/api/reply', { timeout: sample.attachment ? 180000 : 45000 }),
        page.locator('#translate-button').click()
      ]);
      record.elapsedMs = Math.round(performance.now() - started);
      record.httpStatus = response.status();
      const sent = response.request().postDataJSON();
      check(sent?.mode === 'live' && sent.text === sample.text && sent.recipient === sample.recipient, 'REQUEST_NOT_LIVE_OR_MISMATCHED');
      if (sample.attachment) check(sent.attachment?.kind === sample.attachment.kind && sent.attachment.name === sample.attachment.name &&
        sent.attachment.mime === (sample.attachment.kind === 'image' ? 'image/png' : 'video/mp4') && typeof sent.attachment.data === 'string' && sent.attachment.data.length > 100, 'ATTACHMENT_NOT_SENT');
      const data = await response.json();
      const knownCauses = ['AUTH_FAILED', 'INSUFFICIENT_BALANCE', 'PROVIDER_CONFIG', 'RATE_LIMITED', 'PROVIDER_UNAVAILABLE', 'TIMEOUT', 'NETWORK', 'INVALID_RESPONSE',
        'MEDIA_NOT_CONFIGURED', 'MEDIA_RUNTIME', 'MEDIA_TIMEOUT', 'MEDIA_CANCELLED', 'MEDIA_INVALID', 'MEDIA_OUTPUT_TOO_LARGE', 'MEDIA_AUDIO_UNAVAILABLE', 'MEDIA_VISION_INVALID', 'VIDEO_NOT_CONFIGURED'];
      if (!response.ok()) record.failureCause = knownCauses.includes(data.cause) ? data.cause : 'REPLY_HTTP_FAILURE';
      check(response.ok(), 'REPLY_HTTP_FAILURE');
      record.output = {
        content_summary: short(data.content_summary, 160),
        possible_meanings: Array.isArray(data.possible_meanings) ? data.possible_meanings.slice(0, 2).map(item => short(item, 100)) : null,
        reply_tip: short(data.reply_tip, 80), reply: short(data.reply, 120),
        strategy: short(data.strategy, 20), topic: short(data.topic, 20),
        decision_source: short(data.decision_source, 20), generation_source: short(data.generation_source, 20)
      };
      record.models = { decision: short(data.meta?.model, 100), generation: short(data.meta?.generation_model, 100) };
      record.decisionError = knownCauses.includes(data.meta?.decision_error) ? data.meta.decision_error : null;
      record.usage = numericUsage(data.meta?.usage);
      record.timingsMs = numericUsage({ decision: data.meta?.decision_ms, generation: data.meta?.generation_ms, total: data.meta?.total_ms });
      if (data.evidence) record.evidence = Object.fromEntries(['kind', 'visual_summary', 'visible_text', 'transcript', 'audio_status', 'coverage', 'frame_count', 'duration']
        .map(key => [key, typeof data.evidence[key] === 'number' && Number.isFinite(data.evidence[key]) ? data.evidence[key] : short(data.evidence[key], 2000)]));
      check(data.decision_source === (useJev ? 'jev' : 'llm') && data.generation_source === 'llm', 'REAL_PROVENANCE_REQUIRED');
      check((!useJev || record.models.decision?.trim()) && record.models.generation?.trim(), 'MODEL_METADATA_MISSING');
      record.realApiConfirmed = true;
      check(['warm', 'careful', 'uncertain'].includes(data.strategy), 'INVALID_STRATEGY');
      failureCode = 'INVALID_GENERATION_STRUCTURE';
      validateGeneration(data, data.strategy);
      failureCode = 'CASE_RUNTIME_FAILURE';
      await page.locator('#result-content').waitFor({ state: 'visible' });
      await page.waitForFunction(() => !document.getElementById('translate-button').disabled);
      check(await page.locator('#reply-text').inputValue() === data.reply, 'REPLY_NOT_RENDERED_EXACTLY');
      check(await page.locator('#content-summary').innerText() === data.content_summary, 'SUMMARY_NOT_RENDERED_EXACTLY');
      check((await page.locator('#possible-meanings li').count()) === data.possible_meanings.length, 'ANALYSIS_NOT_RENDERED');
      check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'MOBILE_HORIZONTAL_OVERFLOW');
      check(sample.recipient === 'elder' ? /您/.test(data.reply) : data.reply.startsWith(sample.recipient === 'mother' ? '妈' : '爸'), 'RECIPIENT_MISMATCH');
      check(!/我(?:已经|刚刚|刚|认真)?(?:看完|看过|看了|查了|查过|没查到)|我回头问(?:问)?医生/.test(data.reply), 'UNSUPPORTED_PERSONAL_ACTION');
      const forbidden = {
        'care-mother': /我(?:已经|刚刚|刚)(?:吃过|吃了|穿好)|体检(?:结果)?(?:全绿|正常)/,
        'invitation-father': /(?:我|周末|这周)(?:一定|肯定|准时)(?:会)?(?:回家|回来|回去)|(?:我|周末)(?:就|会)回(?:家|来|去)吃饭/,
        'health-claim': /(?:你|您)(?:可以|应该|最好)(?:先)?(?:停药|减药)|(?:我|咱们)(?:每天|天天|坚持)喝醋/
      };
      check(!forbidden[sample.id]?.test(data.reply), 'POSSIBLY_FABRICATED_OR_UNSAFE_REPLY');
      if (sample.id === 'health-claim') check(/核实|医生|药师|可靠/.test(data.reply + data.reply_tip), 'HEALTH_VERIFICATION_MISSING');
      if (sample.id === 'ambiguous') check(data.possible_meanings.length === 2 && /[？?]/.test(data.reply), 'AMBIGUITY_NOT_PRESERVED');
      if (sample.id === 'long-article-elder') check(/节奏|比较|照顾|休息|努力|生活/.test(data.content_summary), 'ARTICLE_SUMMARY_NOT_GROUNDED');
      if (sample.attachment) {
        const evidence = data.evidence;
        check(evidence?.kind === sample.attachment.kind && evidence.visual_summary?.trim() && evidence.coverage?.trim() && typeof evidence.visible_text === 'string' && typeof evidence.transcript === 'string', 'MEDIA_EVIDENCE_MISSING');
        check(Number.isInteger(evidence.frame_count) && evidence.frame_count >= 1, 'FRAME_COUNT_MISSING');
        await page.locator('#content-evidence summary').click();
        check((await page.locator('#visual-summary').innerText()).includes(evidence.visual_summary), 'VISUAL_EVIDENCE_NOT_RENDERED');
        if (sample.attachment.kind === 'image') {
          check(evidence.audio_status === 'not_applicable' && evidence.transcript === '' && evidence.frame_count === 1, 'IMAGE_EVIDENCE_INVALID');
          check(/自己的节奏|照顾好自己|慢一点/.test(evidence.visible_text), 'IMAGE_TEXT_NOT_RECOGNIZED');
          check(/节奏|比较|照顾|生活|日子/.test(data.content_summary), 'IMAGE_SUMMARY_NOT_GROUNDED');
        } else {
          check(evidence.audio_status === 'transcribed' && ['吃饭', '衣服', '妈妈'].filter(word => evidence.transcript.includes(word)).length >= 2, 'VIDEO_SPEECH_NOT_RECOGNIZED');
          check(evidence.duration > 11 && evidence.duration < 12 && evidence.frame_count <= 6, 'VIDEO_COVERAGE_INVALID');
          check(/吃饭|衣服|电话/.test(evidence.visible_text) && /吃饭|添衣|衣服|电话/.test(data.content_summary), 'VIDEO_SUMMARY_NOT_GROUNDED');
          check(await page.locator('#audio-transcript').innerText() === evidence.transcript, 'TRANSCRIPT_NOT_RENDERED_EXACTLY');
        }
        check(!/我(?:已经|刚刚|刚)?(?:看完|看过)了?/.test(data.reply), 'USER_WATCHING_FABRICATED');
      }
      if (sample.id === 'care-mother') {
        const edited = ' 妈，关心收到啦。\n你今天过得怎么样？ ';
        await page.locator('#reply-text').fill(edited);
        await page.locator('#copy-reply').click();
        await page.waitForFunction(() => document.getElementById('copy-status').textContent.includes('已复制'));
        const copied = await page.evaluate(() => navigator.clipboard.readText());
        // Windows clipboard uses CRLF; preserve all other whitespace and text exactly.
        check(copied.replace(/\r\n/g, '\n') === edited, 'EDIT_COPY_MISMATCH');
        record.editAndCopyTextPreserved = true;
        record.clipboardLineEndings = copied.includes('\r\n') ? 'CRLF' : 'LF';
        await mkdir(screenshotDir, { recursive: true });
        await page.screenshot({ path: `${screenshotDir}/first-result-mobile.png`, fullPage: true, animations: 'disabled' });
        report.screenshot = 'screenshots/live/first-result-mobile.png';
      }
      record.status = 'passed';
    }
    check(report.requestCount === cases.length && report.requestCount <= 7, 'REQUEST_BUDGET_MISMATCH');
    check(pageErrors === 0, 'BROWSER_PAGE_ERROR');
    report.status = 'passed';
    report.fullLiveModelPass = report.cases.length === 7 && report.cases.every(item => item.status === 'passed' && item.realApiConfirmed);
    report.jevTested = useJev && report.fullLiveModelPass;
  }
} catch {
  report.status = blocked ? 'blocked' : 'failed';
  report.failure = { stage, code: failureCode };
  if (report.cases.at(-1)?.status === 'running') report.cases.at(-1).status = 'failed';
  process.exitCode = blocked ? 2 : 1;
} finally {
  await browser?.close().catch(() => {});
  if (server?.listening) await new Promise(resolve => { server.close(resolve); server.closeAllConnections(); });
  report.completedAt = new Date().toISOString();
  report.pageErrorCount = pageErrors;
  try {
    await writeFile(reportPath, JSON.stringify(report, null, 2) + '\n');
    console.log(`Live acceptance: ${report.status}; ${report.requestCount} generation requests. See live-test-results.json.`);
  } catch { process.exitCode = 1; console.error('Could not write live acceptance report.'); }
}
