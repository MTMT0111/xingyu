import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { resolve } from 'node:path';
import { createHash, timingSafeEqual } from 'node:crypto';
import { examples, validateInput, questions, validateDecision, selectStrategy, generateLocal, generationPrompt, validateGeneration } from './lib/translate.mjs';
import { extractVideo, validateVisualEvidence, visionPrompt } from './lib/media.mjs';
import { findWechatArticleUrl, readWechatArticle } from './lib/article.mjs';

const publicRoot = fileURLToPath(new URL('./public/', import.meta.url));
const staticFiles = new Map([
  ['/', ['index.html', 'text/html; charset=utf-8']],
  ['/index.html', ['index.html', 'text/html; charset=utf-8']],
  ['/styles.css', ['styles.css', 'text/css; charset=utf-8']],
  ['/demo.js', ['demo.js', 'text/javascript; charset=utf-8']],
  ['/app.js', ['app.js', 'text/javascript; charset=utf-8']],
  ['/assets/starry-night.jpg', ['assets/starry-night.jpg', 'image/jpeg']],
  ['/assets/rose-atelier.svg', ['assets/rose-atelier.svg', 'image/svg+xml']],
  ['/assets/MaShanZheng-Regular.woff2', ['assets/MaShanZheng-Regular.woff2', 'font/woff2']],
  ['/assets/LXGWWenKai-Regular.woff2', ['assets/LXGWWenKai-Regular.woff2', 'font/woff2']]
]);

function settings() {
  return {
    typesafeKey: process.env.TYPESAFE_API_KEY || '',
    llmKey: process.env.LLM_API_KEY || '',
    llmBaseUrl: process.env.LLM_BASE_URL || '',
    llmModel: process.env.LLM_MODEL || '',
    jevModel: process.env.JEV_MODEL || 'jev-latest',
    llmVision: process.env.LLM_VISION === '1',
    mediaPython: process.env.MEDIA_PYTHON || '',
    mediaPythonPath: process.env.MEDIA_PYTHONPATH || '',
    whisperModel: process.env.WHISPER_MODEL_PATH || '',
    publicOrigin: process.env.PUBLIC_ORIGIN || '',
    trialPassword: process.env.TRIAL_PASSWORD || '',
    timeoutJevMs: 8000, timeoutLlmMs: 20000
  };
}

function endpoint(base) {
  const url = new URL(base.replace(/\/$/, '') + '/chat/completions');
  if (url.protocol !== 'https:' || url.username || url.password || url.search || url.hash) throw new Error('Invalid model endpoint');
  return url.href;
}

function liveAvailable(config) {
  try { return Boolean(config.llmKey && config.llmModel && endpoint(config.llmBaseUrl)); }
  catch { return false; }
}

function mediaAvailable(config) {
  return liveAvailable(config) && (config.llmVision || (new URL(config.llmBaseUrl).hostname === 'api.deepseek.com' && ['deepseek-flash', 'deepseek-v4-flash', 'deepseek-v4-flash-vision-exp'].includes(config.llmModel)));
}

function videoAvailable(config) {
  return mediaAvailable(config) && Boolean(config.mediaPython && config.whisperModel && existsSync(config.mediaPython) && existsSync(resolve(config.whisperModel, 'model.bin')));
}

function json(res, status, value) {
  res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8' });
  res.end(JSON.stringify(value));
}

async function readBody(req) {
  let bytes = 0;
  const chunks = [];
  for await (const chunk of req) {
    bytes += chunk.length;
    if (bytes > 30 * 1024 * 1024) {
      const error = new Error('输入过长，请缩短后再试。');
      error.status = 413;
      throw error;
    }
    chunks.push(chunk);
  }
  try { return JSON.parse(Buffer.concat(chunks).toString('utf8')); }
  catch { throw new Error('请求需要是有效的 JSON。'); }
}

async function postModel(fetchImpl, url, key, body, timeout, signal) {
  const response = await fetchImpl(url, {
    method: 'POST', redirect: 'error',
    headers: { Authorization: `Bearer ${key}`, 'Content-Type': 'application/json' },
    body: JSON.stringify(body), signal: AbortSignal.any([signal, AbortSignal.timeout(timeout)])
  });
  if (!response.ok) {
    await response.body?.cancel();
    throw Object.assign(new Error('Model service unavailable'), { status: response.status });
  }
  return response.json();
}

function modelFailure(error) {
  if (error?.articleCode) return { code: 'ARTICLE_' + error.articleCode.toUpperCase(), message: error.message };
  if (error?.mediaCode) return { code: error.mediaCode, message: error.message };
  const status = error?.status;
  if (status === 401 || status === 403) return { code: 'AUTH_FAILED', message: 'AI 服务认证未通过，请检查服务配置。已有文字已保留。' };
  if (status === 402) return { code: 'INSUFFICIENT_BALANCE', message: 'AI 服务额度不足，已有文字已保留。请补充服务额度后重试。' };
  if (status === 429 || status === 529) return { code: 'RATE_LIMITED', message: 'AI 服务暂时繁忙，已有文字已保留，请稍后重试。' };
  if (status === 400 || status === 404 || status === 422) return { code: 'PROVIDER_CONFIG', message: 'AI 服务不接受当前模型或参数，请检查服务配置。已有文字已保留。' };
  if (status >= 500) return { code: 'PROVIDER_UNAVAILABLE', message: 'AI 服务暂时不可用，已有文字已保留，请稍后重试。' };
  if (error?.name === 'TimeoutError' || error?.name === 'AbortError') return { code: 'TIMEOUT', message: 'AI 服务响应超时，已有文字已保留，请重试。' };
  if (error instanceof TypeError) return { code: 'NETWORK', message: '暂时连不上 AI 服务，已有文字已保留，请稍后重试。' };
  return { code: 'INVALID_RESPONSE', message: 'AI 返回的内容不完整，已有文字已保留，请重试。' };
}

async function replyLive(input, config, fetchImpl, signal, extractVideoImpl) {
  const start = performance.now();
  let evidence;
  const articleUrl = input.attachment ? null : findWechatArticleUrl(input.text);
  if (articleUrl) {
    const article = await readWechatArticle(articleUrl, { fetchImpl, signal });
    evidence = { kind: 'article', visual_summary: '文章标题：' + [...article.title].slice(0, 200).join(''), visible_text: article.text,
      transcript: '', audio_status: 'not_applicable', duration: null, frame_count: 0,
      coverage: '已读取公众号公开正文；文章中的图片和嵌入视频未自动读取。' };
  }
  if (input.attachment) {
    const attachment = input.attachment;
    const video = attachment.kind === 'video' ? await extractVideoImpl(attachment, config, signal) : null;
    const frames = video ? video.frames : [{ time: 0, dataUrl: `data:${attachment.mime};base64,${attachment.bytes.toString('base64')}` }];
    const visionUrl = endpoint(config.llmBaseUrl);
    const visualResponse = await postModel(fetchImpl, visionUrl, config.llmKey, {
      model: config.llmModel,
      messages: [
        { role: 'system', content: visionPrompt },
        { role: 'user', content: [{ type: 'text', text: video ? `按时间顺序抽取的${frames.length}帧视频画面，时长${video.duration}秒。` : '识别这张图片的内容。' }, ...frames.flatMap(frame => [{ type: 'text', text: `画面时间：${frame.time}秒` }, { type: 'image_url', image_url: { url: frame.dataUrl } }])] }
      ],
      ...(new URL(visionUrl).hostname === 'api.deepseek.com' ? { thinking: { type: 'disabled' } } : {}),
      response_format: { type: 'json_object' }, max_tokens: 3000, stream: false
    }, config.timeoutLlmMs, signal);
    if (visualResponse.choices?.[0]?.finish_reason === 'length') throw new Error('Truncated visual evidence');
    const visual = validateVisualEvidence(JSON.parse(visualResponse.choices?.[0]?.message?.content));
    evidence = { kind: attachment.kind, ...visual, transcript: video?.transcript ?? '',
      audio_status: video?.audioStatus ?? 'not_applicable', duration: video?.duration ?? null, frame_count: frames.length,
      coverage: video ? `根据抽取的 ${frames.length} 帧画面${video.audioStatus === 'silent' ? '整理；视频没有可辨识语音' : '和语音转写整理'}，可能遗漏短暂出现的画面。` : '根据上传图片中的可见内容整理，模糊或遮挡文字可能遗漏。' };
  }
  const analyzedText = evidence ? JSON.stringify({ message_text: input.text, source_evidence: evidence }) : input.text;
  const decisionStart = performance.now();
  let decision = null;
  let decisionModel = null;
  let decisionUsage = null;
  let decisionError = null;
  if (config.typesafeKey) try {
    const data = await postModel(fetchImpl, 'https://api.typesafe.ai/v1/systemone', config.typesafeKey,
      { model: config.jevModel, state: { message_text: analyzedText }, questions }, config.timeoutJevMs, signal);
    decision = validateDecision(data);
    decisionModel = typeof data.model === 'string' ? data.model : null;
    decisionUsage = data.usage ?? null;
  } catch (error) { decisionError = modelFailure(error).code; }
  if (signal.aborted) throw new Error('Request cancelled');
  const decisionMs = Math.round(performance.now() - decisionStart);
  const strategy = selectStrategy(decision);
  const generationStart = performance.now();
  const generationUrl = endpoint(config.llmBaseUrl);
  const data = await postModel(fetchImpl, generationUrl, config.llmKey, {
    model: config.llmModel,
    messages: [
      { role: 'system', content: generationPrompt(strategy, input.tone, input.recipient) },
      { role: 'user', content: JSON.stringify({ message_text: input.text, context: input.context, recipient: input.recipient, variation: input.variant, ...(evidence ? { source_evidence: evidence } : {}) }) }
    ],
    // DeepSeek enables thinking by default; short replies need the budget for their JSON answer.
    ...(new URL(generationUrl).hostname === 'api.deepseek.com' ? { thinking: { type: 'disabled' } } : {}),
    response_format: { type: 'json_object' }, max_tokens: 800, stream: false
  }, config.timeoutLlmMs, signal);
  if (data.choices?.[0]?.finish_reason === 'length') throw new Error('Truncated generation');
  const result = validateGeneration(JSON.parse(data.choices?.[0]?.message?.content), strategy);
  return {
    ...result, ...(evidence ? { evidence } : {}), strategy, topic: decision?.topic ?? 'unclear',
    decision_source: decision ? 'jev' : config.typesafeKey ? 'unavailable' : 'llm', generation_source: 'llm',
    meta: {
      decision_ms: decisionMs, generation_ms: Math.round(performance.now() - generationStart), total_ms: Math.round(performance.now() - start),
      model: decisionModel, generation_model: typeof data.model === 'string' ? data.model : config.llmModel,
      decision_error: decisionError,
      usage: { decision: decisionUsage, generation: data.usage ?? null }
    },
    note: decision || !config.typesafeKey ? 'AI 只提供可能的理解，具体想法还需要和家人聊聊。' : '内容判断暂不可用，以下由语言模型保留多种理解，仅供参考。'
  };
}

export function createServer({ config = {}, fetchImpl = globalThis.fetch, extractVideoImpl = extractVideo } = {}) {
  const cfg = { ...settings(), ...config };
  let publicUrl = null;
  let trialAuthorization = null;
  let trialAccessToken = null;
  let trialAccessAuthorization = null;
  if (cfg.publicOrigin) {
    try {
      publicUrl = new URL(cfg.publicOrigin);
      if (publicUrl.protocol !== 'https:' || publicUrl.username || publicUrl.password || publicUrl.pathname !== '/' || publicUrl.search || publicUrl.hash) throw new Error();
    } catch { throw new Error('PUBLIC_ORIGIN 必须为固定的 HTTPS 来源，例如 https://reply.example.com，不含路径、查询参数或账号。'); }
    if (!/^[\x21-\x7E]{16,128}$/.test(cfg.trialPassword)) throw new Error('公网试用必须设置 TRIAL_PASSWORD：16–128 位无空格 ASCII 字符；用户名固定为 trial。');
    trialAuthorization = createHash('sha256').update(`Basic ${Buffer.from(`trial:${cfg.trialPassword}`).toString('base64')}`).digest();
    trialAccessToken = createHash('sha256').update(`Link ${cfg.trialPassword}`).digest('base64url');
    trialAccessAuthorization = createHash('sha256').update(trialAccessToken).digest();
  }
  let activeLiveRequests = 0;
  let activeBodyReads = 0;
  let trialWindowStart = performance.now();
  let trialLiveRequests = 0;
  return http.createServer(async (req, res) => {
    res.setHeader('Cache-Control', 'no-store');
    res.setHeader('X-Content-Type-Options', 'nosniff');
    // Chrome 68 WebView sends Origin: null for same-origin POSTs under no-referrer.
    res.setHeader('Referrer-Policy', 'same-origin');
    res.setHeader('Content-Security-Policy', "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; media-src 'self' blob:; connect-src 'self'; font-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'");
    const host = req.headers.host ?? '';
    if (publicUrl ? host.toLowerCase() !== publicUrl.host : !/^(127\.0\.0\.1|localhost)(:\d+)?$/.test(host)) return json(res, 403, { error: '不接受这个访问地址。', code: 'HOST_DENIED' });
    const origin = publicUrl?.origin ?? `http://${host}`;
    if (req.headers.origin && req.headers.origin !== origin) return json(res, 403, { error: '不接受来自其他页面的请求。', code: 'ORIGIN_DENIED' });
    let url;
    try {
      url = new URL(req.url, origin);
      if (url.origin !== origin) throw new Error('Foreign URL');
    } catch {
      return json(res, 400, { error: '请求地址无效。', code: 'INVALID_URL' });
    }
    if (trialAuthorization) {
      const access = url.searchParams.get('access') ?? '';
      if (req.method === 'GET' && url.pathname === '/' && url.searchParams.size === 1 && timingSafeEqual(trialAccessAuthorization, createHash('sha256').update(access).digest())) {
        res.writeHead(303, {
          Location: '/',
          'Set-Cookie': `xingyu_trial=${trialAccessToken}; Path=/; Max-Age=28800; HttpOnly; Secure; SameSite=Strict`,
        });
        return res.end();
      }
      const cookieAuthorized = (req.headers.cookie ?? '').split(';').some((entry) => {
        const [name, value = ''] = entry.trim().split('=');
        return name === 'xingyu_trial' && timingSafeEqual(trialAccessAuthorization, createHash('sha256').update(value).digest());
      });
      const basicAuthorized = timingSafeEqual(trialAuthorization, createHash('sha256').update(req.headers.authorization ?? '').digest());
      if (!cookieAuthorized && !basicAuthorized) {
        res.setHeader('WWW-Authenticate', 'Basic realm="Xingyu private trial", charset="UTF-8"');
        return json(res, 401, { error: '这是邀请试用页面，请使用直达链接，或输入用户名 trial 和试用口令。', code: 'TRIAL_AUTH_REQUIRED' });
      }
    }
    try {
      if (req.method === 'GET' && url.pathname === '/api/config') {
        const available = liveAvailable(cfg);
        return json(res, 200, { mode: available ? 'live' : 'demo', liveAvailable: available, decisionMode: available ? (cfg.typesafeKey ? 'jev' : 'llm') : 'demo', mediaAvailable: mediaAvailable(cfg), videoAvailable: videoAvailable(cfg), label: available ? '真实 AI · 在线生成' : '本地体验 · 非实时 AI' });
      }
      if (req.method === 'GET' && url.pathname === '/api/examples') return json(res, 200, examples);
      if (req.method === 'POST' && url.pathname === '/api/reply') {
        if (Number(req.headers['content-length']) > 30 * 1024 * 1024) {
          req.resume();
          return json(res, 413, { error: '附件过大，请缩小后重试。', code: 'BODY_TOO_LARGE' });
        }
        if (!(req.headers['content-type'] ?? '').toLowerCase().startsWith('application/json')) return json(res, 415, { error: '请求需要使用 JSON 格式。', code: 'CONTENT_TYPE' });
        let input;
        if (activeBodyReads >= 2) return json(res, 429, { error: '正在接收其他消息，请稍后再试。', code: 'BUSY' });
        activeBodyReads++;
        try { input = validateInput(await readBody(req)); }
        catch (error) { return json(res, error.status ?? 400, { error: error.message, code: 'INVALID_INPUT' }); }
        finally { activeBodyReads--; }
        if (input.mode === 'demo') return json(res, 200, generateLocal(input));
        if (!liveAvailable(cfg)) return json(res, 503, { error: '真实 AI 尚未配置。可以继续本地体验，或按 README 配置 LLM。', code: 'NOT_CONFIGURED' });
        if (input.attachment && !mediaAvailable(cfg)) return json(res, 503, { error: '图片和视频需要支持视觉的模型，请检查服务配置。', code: 'MEDIA_NOT_CONFIGURED' });
        if (input.attachment?.kind === 'video' && !videoAvailable(cfg)) return json(res, 503, { error: '本地视频语音识别尚未就绪，请检查服务配置。', code: 'VIDEO_NOT_CONFIGURED' });
        if (activeLiveRequests >= 2) return json(res, 429, { error: '正在生成其他回复，请稍后再试。', code: 'BUSY' });
        if (publicUrl) {
          if (performance.now() - trialWindowStart >= 3600000) { trialWindowStart = performance.now(); trialLiveRequests = 0; }
          // shortcut: single-process invitation trial; use durable per-user quotas before an open or multi-instance launch.
          if (trialLiveRequests >= 30) {
            res.setHeader('Retry-After', String(Math.ceil((3600000 - (performance.now() - trialWindowStart)) / 1000)));
            return json(res, 429, { error: '本轮邀请试用的 AI 额度已用完，请稍后再试。本地体验仍可使用。', code: 'TRIAL_LIMIT' });
          }
          trialLiveRequests++;
        }
        activeLiveRequests++;
        const controller = new AbortController();
        res.on('close', () => { if (!res.writableEnded) controller.abort(); });
        try { return json(res, 200, await replyLive(input, cfg, fetchImpl, controller.signal, extractVideoImpl)); }
        catch (error) {
          const failure = modelFailure(error);
          return json(res, 502, { error: failure.message, code: 'GENERATION_FAILED', cause: failure.code });
        }
        finally { activeLiveRequests--; }
      }
      const file = staticFiles.get(url.pathname);
      if (file && ['GET', 'HEAD'].includes(req.method)) {
        const body = await readFile(resolve(publicRoot, file[0]));
        res.writeHead(200, { 'Content-Type': file[1] });
        return res.end(req.method === 'HEAD' ? undefined : body);
      }
      return json(res, 404, { error: '没有找到这个页面。', code: 'NOT_FOUND' });
    } catch {
      return json(res, 500, { error: '页面暂时不可用，请稍后重试。', code: 'SERVER_ERROR' });
    }
  });
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const port = Number(process.env.PORT || 4173);
  if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('PORT 必须为 1–65535 的整数');
  const bindHost = process.env.BIND_HOST || '127.0.0.1';
  if (!['127.0.0.1', '0.0.0.0'].includes(bindHost)) throw new Error('BIND_HOST 仅支持 127.0.0.1 或 0.0.0.0');
  if (bindHost !== '127.0.0.1' && !process.env.PUBLIC_ORIGIN) throw new Error('监听外部网络前必须配置 PUBLIC_ORIGIN 和 TRIAL_PASSWORD；HTTPS 由反向代理提供。');
  const runtimeConfig = settings();
  const server = createServer({ config: runtimeConfig });
  server.on('error', error => { console.error(error.code === 'EADDRINUSE' ? `端口 ${port} 已占用，请修改 PORT。` : '服务启动失败。'); process.exitCode = 1; });
  server.listen(port, bindHost, () => console.log(`言外 · 长辈消息回复助手已启动：http://${bindHost}:${port}\n${process.env.PUBLIC_ORIGIN ? `邀请试用来源：${new URL(process.env.PUBLIC_ORIGIN).origin}（请经 HTTPS 反向代理访问）` : '服务仅监听本机。'}\n${liveAvailable(runtimeConfig) ? `真实 AI 已配置：${runtimeConfig.typesafeKey ? 'Jev + LLM' : 'LLM 直接分析与回复'}，点击生成后才调用。` : '当前为本地示例，未配置真实 AI。'}\n按 Ctrl+C 停止。`));
}
