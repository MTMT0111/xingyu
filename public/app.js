'use strict';

const $ = id => document.getElementById(id);
if (new URLSearchParams(location.search).get('host') === 'android') document.body.dataset.host = 'android';
const staticDemo = location.hostname.endsWith('.github.io') || new URLSearchParams(location.search).get('demo') === '1';
const hasOwn = (object, key) => Object.prototype.hasOwnProperty.call(object, key);
const settle = promises => Promise.all(promises.map(promise => Promise.resolve(promise).then(
  value => ({ status: 'fulfilled', value }),
  reason => ({ status: 'rejected', reason })
)));
const requestId = () => typeof crypto.randomUUID === 'function' ? crypto.randomUUID() :
  Array.from(crypto.getRandomValues(new Uint32Array(4)), value => value.toString(16).padStart(8, '0')).join('-');
const state = { busy: false, sharing: false, initializing: false, mode: 'demo', modeSettled: false, liveAvailable: false, mediaAvailable: false, videoAvailable: false, attachment: null, controller: null, result: null, submitted: null, exampleIndex: -1 };
let pendingConfig = null;
let pendingAndroidAutoGenerate = null;
const draftKey = 'xingyu-draft-v1';
let attachmentNeedsReselect = false;
const nativeWechat = { post: null, available: false, reason: 'checking', pending: new Map(), refreshing: false, lastShare: null };
let pendingInboundShare = null;
let activeInboundId = null;
let acceptingInboundShare = false;
let examples = [
  { text: '今天降温了，多穿点。忙也要记得吃饭，别总熬夜。' },
  { text: '这周末有空回来吃饭吗？想给你做你爱吃的菜。' },
  { text: '给你分享个视频，你有空看看，我觉得挺有意思的。' }
];

async function requestJSON(url, options, { timeout = 32000, signal } = {}) {
  if (staticDemo) {
    if (!window.HaoHaoHuiDemo) throw new Error('公开体验资源未完整加载，请刷新页面。');
    if (url === '/api/config') return window.HaoHaoHuiDemo.config;
    if (url === '/api/examples') return window.HaoHaoHuiDemo.examples;
    if (url === '/api/reply') return window.HaoHaoHuiDemo.reply(JSON.parse(options && options.body || '{}'));
  }
  const controller = new AbortController();
  const cancel = () => controller.abort();
  if (signal && signal.aborted) cancel();
  if (signal) signal.addEventListener('abort', cancel, { once: true });
  const timer = setTimeout(cancel, timeout);
  try {
    const response = await fetch(url, { ...options, signal: controller.signal });
    let data;
    try { data = await response.json(); } catch {
      if (controller.signal.aborted) throw new DOMException('Aborted', 'AbortError');
      throw new Error('服务返回的内容不完整，请重试。');
    }
    if (!response.ok) throw new Error(data && typeof data.error === 'string' ? data.error : '暂时没能生成回复，请重试。');
    return data;
  } catch (error) {
    if (signal && signal.aborted) throw new DOMException('Aborted', 'AbortError');
    if (error.name === 'AbortError') throw new Error('等得有点久了，输入和已有回复已保留，请再试一次。');
    if (error instanceof TypeError) throw new Error('暂时连接不上服务，输入和已有回复已保留，请稍后重试。');
    throw error;
  } finally { clearTimeout(timer); if (signal) signal.removeEventListener('abort', cancel); }
}

function showError(message) {
  $('form-error').textContent = message;
  $('form-error').hidden = false;
}

function clearError() { $('form-error').hidden = true; }

function scrollIntoViewNow(element, block = 'center') {
  const previous = document.documentElement.style.scrollBehavior;
  document.documentElement.style.scrollBehavior = 'auto';
  element.scrollIntoView({ block });
  document.documentElement.style.scrollBehavior = previous;
}

function saveDraft() {
  try {
    // Keep summaries and replies in this tab; omit file bytes, full OCR, article bodies and transcripts.
    const result = state.result && ['content_summary', 'possible_meanings', 'reply_tip', 'reply', 'decision_source', 'generation_source', 'note'].reduce((saved, key) => {
      saved[key] = state.result[key];
      return saved;
    }, {});
    const draft = { text: $('source-text').value, recipient: $('recipient-select').value, tone: $('tone-select').value, mode: state.mode, result, submitted: state.submitted, reply: $('reply-text').value, hadAttachment: Boolean(state.attachment || attachmentNeedsReselect) };
    if (draft.text.length > 24000 || draft.reply.length > 20000) throw new Error('Draft too large');
    if (draft.text || result || draft.hadAttachment) sessionStorage.setItem(draftKey, JSON.stringify(draft));
    else sessionStorage.removeItem(draftKey);
    const status = attachmentNeedsReselect ? '草稿已恢复；附件需重新选择' : '草稿仅在当前标签页暂存';
    if ($('draft-status').textContent !== status) $('draft-status').textContent = status;
  } catch {
    try { sessionStorage.removeItem(draftKey); } catch { /* Storage may be unavailable in embedded browsers. */ }
    $('draft-status').textContent = '当前无法暂存，请保留或复制文字';
  }
}

function restoreDraft() {
  try {
    const raw = sessionStorage.getItem(draftKey);
    if (!raw) return;
    if (raw.length > 70000) throw new Error('Draft too large');
    const draft = JSON.parse(raw);
    const tone = draft && draft.tone === undefined ? 'warm' : draft ? draft.tone : undefined;
    if (!draft || typeof draft.text !== 'string' || draft.text.length > 24000 || typeof draft.reply !== 'string' || draft.reply.length > 20000 || !['auto', 'mother', 'father', 'elder'].includes(draft.recipient) || !['warm', 'brief', 'boundary'].includes(tone) || !['demo', 'live'].includes(draft.mode) || typeof draft.hadAttachment !== 'boolean') throw new Error('Invalid draft');
    if (draft.result) {
      validateResult(draft.result);
      if (!draft.submitted || draft.submitted.text !== draft.text.trim() || draft.submitted.recipient !== draft.recipient || (draft.submitted.tone == null ? 'warm' : draft.submitted.tone) !== tone) throw new Error('Stale draft');
    }
    $('source-text').value = draft.text;
    $('recipient-select').value = draft.recipient;
    $('tone-select').value = tone;
    $('mode-select').value = draft.mode;
    state.mode = draft.mode;
    state.modeSettled = true;
    updateModeNotice();
    attachmentNeedsReselect = draft.hadAttachment;
    if (draft.result) {
      renderResult(draft.result);
      $('reply-text').value = draft.reply;
      state.submitted = draft.submitted;
      $('empty-state').hidden = true;
      $('result-status').textContent = '已恢复，可继续编辑';
      $('result-note').textContent += ' 当前为恢复的草稿；识别全文未暂存。';
    }
    $('draft-status').textContent = attachmentNeedsReselect ? '草稿已恢复；附件需重新选择' : '已恢复这个标签页的草稿';
  } catch {
    try { sessionStorage.removeItem(draftKey); } catch { /* An unavailable store must not block composing. */ }
  }
}

function clearDraft() {
  pendingAndroidAutoGenerate = null;
  if (state.busy || state.sharing) return;
  attachmentNeedsReselect = false;
  $('source-text').value = '';
  updateAttachment(null);
  countInput();
  saveDraft();
  $('draft-status').textContent = '本次草稿已清空';
  $('source-text').focus();
}

function countInput() {
  const count = Array.from($('source-text').value).length;
  $('source-counter').textContent = count + ' / 6000';
  $('source-counter').classList.toggle('over-limit', count > 6000);
  $('source-text').setAttribute('aria-invalid', String(count > 6000));
  document.querySelector('.source-wrap').classList.toggle('invalid', count > 6000);
}

function canShareReply() {
  if (!window.isSecureContext || typeof navigator.share !== 'function') return false;
  try { return typeof navigator.canShare !== 'function' || navigator.canShare({ text: $('reply-text').value }); }
  catch { return false; }
}

function receiveWechat(value) {
  let data;
  try { data = typeof value === 'string' ? JSON.parse(value) : value; } catch { return; }
  if (!data || typeof data !== 'object' || typeof data.id !== 'string') return;
  if (data.type === 'shareResult') {
    if (!nativeWechat.lastShare || nativeWechat.lastShare.id !== data.id || nativeWechat.lastShare.text !== $('reply-text').value) return;
    const messages = { completed: '微信分享流程已返回，请到聊天中确认。', cancelled: '已取消微信分享，回复已保留。', error: '微信分享未完成，回复已保留，可复制后发送。' };
    if (hasOwn(messages, data.status)) $('copy-status').textContent = data.code === 'result_unknown' ? '微信未返回分享结果，请到微信确认；回复仍在这里。' : messages[data.status];
    return;
  }
  const pending = nativeWechat.pending.get(data.id);
  if (!pending || pending.type !== data.type) return;
  if (data.type === 'capabilities' && (typeof data.available !== 'boolean' ||
      !['ready', 'missing_app_id', 'wechat_not_installed', 'invalid_configuration', 'sdk_unavailable'].includes(data.reason) || data.available !== (data.reason === 'ready'))) return;
  if (data.type === 'shareText' && !['handoff', 'error', 'cancelled', 'completed'].includes(data.status)) return;
  clearTimeout(pending.timer);
  nativeWechat.pending.delete(data.id);
  pending.resolve(data);
}

function requestWechat(type, text) {
  return new Promise((resolve, reject) => {
    const id = requestId();
    const timer = setTimeout(() => {
      nativeWechat.pending.delete(id);
      reject(new Error('微信未返回确认，请先查看微信；回复仍在这里。'));
    }, type === 'capabilities' ? 3000 : 10000);
    nativeWechat.pending.set(id, { type, timer, resolve });
    if (type === 'shareText') nativeWechat.lastShare = { id, text };
    try { nativeWechat.post({ type, id, ...(type === 'shareText' ? { text } : {}) }); }
    catch { clearTimeout(timer); nativeWechat.pending.delete(id); reject(new Error('暂时无法连接微信，可复制回复后发送。')); }
  });
}

async function refreshWechat() {
  if (!nativeWechat.post || nativeWechat.refreshing || state.sharing) return;
  nativeWechat.refreshing = true;
  try {
    const result = await requestWechat('capabilities');
    nativeWechat.available = result.available;
    nativeWechat.reason = result.reason;
  } catch { nativeWechat.available = false; nativeWechat.reason = 'sdk_unavailable'; }
  finally { nativeWechat.refreshing = false; updateControls(); }
}

function initializeWechat() {
  if (window.XingyuWeChat && typeof window.XingyuWeChat.postMessage === 'function') {
    nativeWechat.post = data => window.XingyuWeChat.postMessage(JSON.stringify(data));
    window.XingyuWeChat.onmessage = event => receiveWechat(event.data);
  } else if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.xingyuWeChat && typeof window.webkit.messageHandlers.xingyuWeChat.postMessage === 'function') {
    nativeWechat.post = data => window.webkit.messageHandlers.xingyuWeChat.postMessage(data);
    window.addEventListener('xingyu-wechat', event => receiveWechat(event.detail));
  }
  window.addEventListener('focus', refreshWechat);
  document.addEventListener('visibilitychange', () => { if (!document.hidden) refreshWechat(); });
  window.addEventListener('xingyu-inbound-share', event => receiveInboundShare(event.detail));
  refreshWechat();
}

function validInboundText(text) {
  if (typeof text !== 'string' || !text.trim() || Array.from(text).length > 6000 || text.includes('\u0000')) return false;
  for (let index = 0; index < text.length; index++) {
    const code = text.charCodeAt(index);
    if (code >= 0xd800 && code <= 0xdbff) {
      if (++index >= text.length || text.charCodeAt(index) < 0xdc00 || text.charCodeAt(index) > 0xdfff) return false;
    } else if (code >= 0xdc00 && code <= 0xdfff) return false;
  }
  return true;
}

function normalizeInboundShare(value) {
  let data;
  try { data = typeof value === 'string' ? JSON.parse(value) : value; } catch { return null; }
  if (!data || data.schemaVersion !== 1 || data.type !== 'inboundShare' || typeof data.id !== 'string' || !/^[\w.:-]{1,128}$/.test(data.id)) return null;
  const text = data.text === undefined || data.text === null || data.text === '' ? '' : data.text;
  if (text && !validInboundText(text)) return null;
  let attachment = null;
  if (data.attachment !== undefined) {
    const item = data.attachment;
    const mimeKinds = { 'image/jpeg': 'image', 'image/png': 'image', 'image/webp': 'image', 'video/mp4': 'video', 'video/webm': 'video', 'video/quicktime': 'video' };
    if (!item || typeof item !== 'object' || mimeKinds[item.mime] !== item.kind || !Number.isSafeInteger(item.size) || item.size < 1 || item.size > (item.kind === 'image' ? 4 : 20) * 1024 * 1024 ||
        typeof item.name !== 'string' || !item.name.trim() || Array.from(item.name).length > 120 || /[\u0000-\u001f\u007f/\\]/.test(item.name)) return null;
    let url;
    try { url = new URL(item.url, location.href); } catch { return null; }
    if (url.origin !== location.origin || url.search || url.hash || !/^\/\.xingyu-share\/[\w-]{8,128}$/.test(url.pathname)) return null;
    attachment = { kind: item.kind, mime: item.mime, size: item.size, name: item.name, url: url.href };
  }
  const entry = data.entry === 'process_text' ? 'process_text' : data.entry === 'clipboard' ? 'clipboard' : data.entry === 'screen' ? 'screen' : 'share';
  return text || attachment ? { id: data.id, text, attachment, entry } : null;
}

function hasDraftContent() {
  return Boolean($('source-text').value.trim() || state.attachment || state.result || $('reply-text').value.trim() || attachmentNeedsReselect);
}

function acknowledgeInbound(id, action, finish = false) {
  if (!nativeWechat.post) return;
  try {
    nativeWechat.post({ type: 'ackInboundShare', id, action });
    if (finish) nativeWechat.post({ type: 'finishShareEntry', id });
  } catch { /* The native host may already be closing. */ }
}

function hideInboundShare() {
  pendingInboundShare = null;
  $('inbound-share').hidden = true;
  document.body.removeAttribute('data-entry');
  updateControls();
}

function receiveInboundShare(value) {
  const inbound = normalizeInboundShare(value);
  if (!inbound) return;
  pendingAndroidAutoGenerate = null;
  pendingInboundShare = inbound;
  activeInboundId = null;
  $('return-source').hidden = true;
  if (state.busy || state.sharing || acceptingInboundShare) {
    $('inbound-share').hidden = true;
    return;
  }
  presentInboundShare(inbound);
  updateControls();
}

function presentInboundShare(inbound) {
  const androidHost = document.body.dataset.host === 'android';
  document.body.dataset.entry = 'share';
  const conflict = hasDraftContent();
  $('inbound-share-title').textContent = conflict ? '这里还有一份未完成的草稿' : inbound.entry === 'process_text' ? '已选中一段文字' : inbound.entry === 'clipboard' ? '剪贴板里的一段文字' : inbound.entry === 'screen' ? '已截取微信当前屏幕' : inbound.attachment && inbound.attachment.kind === 'image' ? '收到一张图片' : inbound.attachment && inbound.attachment.kind === 'video' ? '收到一段视频' : '收到一段文字';
  const preview = inbound.text || inbound.attachment.name;
  $('inbound-share-preview').textContent = conflict ? `使用新内容会替换当前输入、附件和分析结果。新内容：${preview}` : preview;
  $('inbound-share-note').textContent = nativeWechat.post ? '尚未上传，也不会自动分析。' : '尚未上传，也不会自动分析；返回原应用请使用系统返回键。';
  $('inbound-dismiss').textContent = conflict ? '继续原草稿' : '暂不使用';
  $('inbound-accept').textContent = androidHost ? conflict ? '改用并开始识别' : '识别并准备回复' : conflict ? '改用新内容' : '放入编辑区';
  $('inbound-return').hidden = !nativeWechat.post;
  $('inbound-share').hidden = false;
  $('inbound-share').scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth', block: 'start' });
}

async function acceptInboundShare() {
  const inbound = pendingInboundShare;
  if (!inbound || state.busy || state.sharing || acceptingInboundShare) return;
  acceptingInboundShare = true;
  let nextAttachment = null;
  let accepted = false;
  const autoGenerate = document.body.dataset.host === 'android';
  updateControls();
  $('inbound-share-note').textContent = inbound.attachment ? '正在安全读取附件，尚未发送给 AI。' : '正在放入编辑区。';
  try {
    if (inbound.attachment) {
      const response = await fetch(inbound.attachment.url, { cache: 'no-store', credentials: 'same-origin' });
      const mime = (response.headers.get('content-type') || '').split(';')[0].trim().toLowerCase();
      if (!response.ok || mime !== inbound.attachment.mime) throw new Error('分享的附件读取失败，请返回原应用后重试。');
      const blob = await response.blob();
      if (!blob.size || blob.size !== inbound.attachment.size || blob.size > (inbound.attachment.kind === 'image' ? 4 : 20) * 1024 * 1024) throw new Error('分享的附件不完整，请返回原应用后重试。');
      const file = new File([blob], inbound.attachment.name, { type: mime });
      nextAttachment = { file, kind: inbound.attachment.kind, mime, url: URL.createObjectURL(file), duration: null };
      if (nextAttachment.kind === 'video') {
        const controller = new AbortController();
        nextAttachment.duration = await videoDuration(nextAttachment.url, controller.signal);
        if (nextAttachment.duration > 60) throw new Error('请先截取 60 秒以内的视频，再分享给言外。');
      }
    }
    if (pendingInboundShare !== inbound) {
      if (nextAttachment && nextAttachment.url) URL.revokeObjectURL(nextAttachment.url);
      nextAttachment = null;
      return;
    }
    updateAttachment(nextAttachment);
    $('source-text').value = inbound.text;
    inputChanged();
    activeInboundId = inbound.id;
    acknowledgeInbound(inbound.id, 'accepted');
    hideInboundShare();
    document.body.dataset.entry = 'share';
    $('draft-status').textContent = '分享内容已放入；确认后再点“帮我回一句”';
    accepted = true;
  } catch (error) {
    if (nextAttachment && nextAttachment.url) URL.revokeObjectURL(nextAttachment.url);
    if (pendingInboundShare === inbound) $('inbound-share-note').textContent = error.message;
  } finally {
    acceptingInboundShare = false;
    updateControls();
    if (accepted) {
      if (autoGenerate && state.initializing) pendingAndroidAutoGenerate = {
        text: $('source-text').value,
        recipient: $('recipient-select').value,
        attachment: state.attachment
      };
      else if (autoGenerate) generateReply();
      else {
        scrollIntoViewNow($('translate-button'));
        $('translate-button').focus({ preventScroll: true });
      }
    }
  }
}

function dismissInboundShare(finish = false) {
  if (!pendingInboundShare) return;
  const id = pendingInboundShare.id;
  if (!finish) activeInboundId = id;
  acknowledgeInbound(id, 'dismissed', finish);
  hideInboundShare();
}

function returnToSource() {
  if (!activeInboundId || !nativeWechat.post) return;
  try { nativeWechat.post({ type: 'finishShareEntry', id: activeInboundId }); }
  catch { /* The native host may already be closing. */ }
}

function updateControls() {
  const locked = state.busy || state.sharing || acceptingInboundShare;
  if (!locked && pendingInboundShare && $('inbound-share').hidden) presentInboundShare(pendingInboundShare);
  const empty = !state.result || !$('reply-text').value.trim();
  if (!state.busy) $('translate-button-text').textContent = state.result ? '换一种回复' : '读懂并准备回复';
  $('clear-draft').disabled = locked || !($('source-text').value || state.result || state.attachment || attachmentNeedsReselect);
  for (const id of ['step-input', 'step-understand', 'step-reply']) {
    if (id === (state.busy ? 'step-understand' : state.result ? 'step-reply' : 'step-input')) $(id).setAttribute('aria-current', 'step');
    else $(id).removeAttribute('aria-current');
  }
  $('source-text').readOnly = locked;
  $('reply-text').readOnly = locked;
  for (const id of ['translate-button', 'recipient-select', 'tone-select', 'mode-select', 'try-example', 'retry-init', 'attachment-input', 'remove-attachment']) $(id).disabled = locked;
  $('retry-init').disabled = locked || state.initializing;
  $('copy-reply').disabled = locked || empty;
  $('share-reply').hidden = !canShareReply();
  $('share-reply').disabled = locked || empty;
  $('share-reply-text').textContent = state.sharing ? '正在分享…' : '系统分享';
  $('share-note').textContent = $('share-reply').hidden ? '当前浏览器不能直接选择微信联系人，可复制后回原聊天发送。' : '系统菜单里有微信时才能继续选人；没有微信请复制回复。';
  $('wechat-reply').hidden = !nativeWechat.post || !nativeWechat.available;
  $('wechat-reply').disabled = locked || empty || !nativeWechat.available;
  for (const id of ['inbound-accept', 'inbound-dismiss', 'inbound-return']) $(id).disabled = locked;
  if (nativeWechat.post) {
    const notes = { ready: '接下来由你在微信选择联系人，并确认发送。', checking: '正在检查微信连接…', missing_app_id: '当前客户端尚未开通微信直连，可先复制回复回微信发送。', wechat_not_installed: '这台设备未安装微信，可先复制回复。', invalid_configuration: '微信连接配置不完整，请检查客户端设置。', sdk_unavailable: '暂时无法连接微信，可复制回复后发送。' };
    $('share-note').textContent = notes[nativeWechat.reason];
  }
}

function setBusy(busy, phase = 'text') {
  state.busy = busy;
  $('result-card').setAttribute('aria-busy', String(busy));
  $('loading-state').hidden = !busy;
  $('empty-state').hidden = busy || Boolean(state.result);
  updateControls();
  $('cancel-request').hidden = !busy || !state.controller;
  $('translate-button-text').textContent = !busy ? (state.result ? '换一种回复' : '读懂并准备回复') : phase === 'prepare' ? '正在准备附件…' : phase === 'media' ? '正在读懂内容…' : phase === 'article' ? '正在读懂文章…' : '正在想一句回复…';
  $('loading-text').textContent = phase === 'prepare' ? '正在检查附件，文件还没有上传。' : phase === 'media' ? '正在识别内容、整理理解。视频可能需要一两分钟，可以随时取消。' : phase === 'article' ? '正在读取公众号正文、整理理解，可以随时取消。' : '正在想一句合适的回应…';
}

function clearResult() {
  nativeWechat.lastShare = null;
  state.result = null;
  state.submitted = null;
  $('result-content').hidden = true;
  $('reply-text').value = '';
  $('empty-state').hidden = false;
  updateControls();
  $('copy-status').textContent = '';
  $('result-status').textContent = '等你的一句话';
}

function inputChanged() {
  pendingAndroidAutoGenerate = null;
  countInput();
  clearError();
  if (state.submitted && ($('source-text').value.trim() !== state.submitted.text || $('recipient-select').value !== state.submitted.recipient || $('tone-select').value !== (state.submitted.tone == null ? 'warm' : state.submitted.tone))) {
    clearResult();
    $('result-announcement').textContent = '消息或回复对象已修改，请重新生成。';
  }
  updateControls();
  saveDraft();
}

function setMode({ preserveResult = false } = {}) {
  state.mode = state.liveAvailable && $('mode-select').value === 'live' ? 'live' : 'demo';
  $('mode-select').value = state.mode;
  updateModeNotice();
  if (!preserveResult) { clearResult(); clearError(); }
}

function updateModeNotice() {
  const live = state.mode === 'live';
  $('mode-label').lastChild.textContent = live ? '真实 AI · 在线生成' : '本地示例 · 非实时 AI';
  $('privacy-note').textContent = live ? '点击生成后，文字及所选附件将发送到配置的 AI 服务。' : '本地示例：按关键词匹配文案，未调用 AI。';
  updateMediaNotice();
}

function applyConfig(config) {
  // Reconnection must not change the mode or discard a draft while a reply is pending.
  if (state.busy || state.sharing) { pendingConfig = config; return; }
  pendingConfig = null;
  state.liveAvailable = config.liveAvailable;
  state.mediaAvailable = config.mediaAvailable;
  state.videoAvailable = config.videoAvailable;
  $('mode-controls').hidden = !state.liveAvailable;
  if (!state.liveAvailable && state.mode === 'live') {
    $('mode-select').value = 'demo';
    setMode({ preserveResult: true });
  } else if (config.connected && !state.modeSettled) {
    state.modeSettled = true;
    $('mode-select').value = config.mode === 'live' && state.liveAvailable ? 'live' : 'demo';
    setMode({ preserveResult: true });
  }
  updateMediaNotice();
}

function mediaError() {
  if (attachmentNeedsReselect) return '上次的附件没有暂存，重新分析前请再次选择文件；现有回复仍可编辑、复制。';
  if (!state.attachment) return '';
  if (!state.mediaAvailable) return '图片和视频分析尚未配置，目前只能本地预览，文件不会上传。';
  if (state.mode !== 'live') return '图片和视频需要真实 AI 分析，请先切换生成方式；本地示例不会读取附件。';
  if (state.attachment.kind === 'video' && !state.videoAvailable) return '视频语音识别尚未配置，目前只能预览视频，文件不会上传。';
  return '';
}

function updateMediaNotice() {
  const notice = mediaError();
  $('media-note').hidden = !notice;
  $('media-note').textContent = notice;
}

function updateAttachment(attachment) {
  pendingAndroidAutoGenerate = null;
  const previousUrl = state.attachment && state.attachment.url;
  $('attachment-video').pause();
  $('attachment-video').removeAttribute('src');
  $('attachment-video').load();
  $('attachment-image').removeAttribute('src');
  $('attachment-image').hidden = true;
  $('attachment-video').hidden = true;
  state.attachment = attachment;
  attachmentNeedsReselect = false;
  $('attachment-preview').hidden = !attachment;
  if (attachment) {
    const preview = $(attachment.kind === 'image' ? 'attachment-image' : 'attachment-video');
    preview.src = attachment.url;
    preview.hidden = false;
    $('attachment-name').textContent = attachment.file.name;
    $('attachment-detail').textContent = `${(attachment.file.size / 1024 / 1024).toFixed(2)} MB · 本地预览` +
      (attachment.kind === 'video' ? attachment.duration ? ` · ${attachment.duration.toFixed(1)} 秒` : ' · 时长将由服务端校验' : '');
  } else $('attachment-input').value = '';
  if (previousUrl) URL.revokeObjectURL(previousUrl);
  clearResult();
  clearError();
  updateMediaNotice();
  saveDraft();
}

function videoDuration(url, signal) {
  return new Promise((resolve, reject) => {
    const video = document.createElement('video');
    const finish = (duration, error) => {
      clearTimeout(timer);
      signal.removeEventListener('abort', cancel);
      video.onloadedmetadata = video.onerror = null;
      video.removeAttribute('src');
      video.load();
      error ? reject(error) : resolve(duration);
    };
    const cancel = () => finish(null, new DOMException('Aborted', 'AbortError'));
    const timer = setTimeout(() => finish(null), 5000);
    video.preload = 'metadata';
    video.onloadedmetadata = () => finish(Number.isFinite(video.duration) && video.duration > 0 ? video.duration : null);
    // Some phone MOV codecs cannot be previewed by this browser; the server still validates them.
    video.onerror = () => finish(null);
    signal.addEventListener('abort', cancel, { once: true });
    video.src = url;
  });
}

async function chooseAttachment() {
  if (state.busy || state.sharing) return;
  const file = $('attachment-input').files[0];
  if (!file) return;
  clearError();
  const mimeByExtension = { jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', webp: 'image/webp', mp4: 'video/mp4', webm: 'video/webm', mov: 'video/quicktime' };
  const mime = file.type.toLowerCase() || mimeByExtension[file.name.split('.').pop().toLowerCase()];
  const kind = ['image/jpeg', 'image/png', 'image/webp'].includes(mime) ? 'image' : ['video/mp4', 'video/webm', 'video/quicktime'].includes(mime) ? 'video' : null;
  if (!kind || !file.size || file.size > (kind === 'image' ? 4 : 20) * 1024 * 1024) {
    showError(!kind ? '请选择 JPG、PNG、WebP 图片，或 MP4、WebM、MOV 视频。' : !file.size ? '这个文件是空的，请重新选择。' : kind === 'image' ? '图片不能超过 4 MB，请缩小后再添加。' : '视频不能超过 20 MB，请压缩后再添加。');
    $('attachment-input').value = '';
    return;
  }
  const attachment = { file, kind, mime, url: URL.createObjectURL(file), duration: null };
  state.controller = new AbortController();
  setBusy(true, 'prepare');
  try {
    if (kind === 'video') attachment.duration = await videoDuration(attachment.url, state.controller.signal);
    if (attachment.duration > 60) throw new Error('请截取 60 秒以内的视频后再添加。');
    if (state.controller.signal.aborted) throw new DOMException('Aborted', 'AbortError');
    updateAttachment(attachment);
  } catch (error) {
    URL.revokeObjectURL(attachment.url);
    $('attachment-input').value = '';
    showError(error.name === 'AbortError' ? '已取消添加，已有内容已保留。' : error.message);
  } finally {
    state.controller = null;
    setBusy(false);
    if (pendingConfig) applyConfig(pendingConfig);
  }
}

function readAttachment(file, signal) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    const cancel = () => reader.abort();
    const finish = (error) => {
      signal.removeEventListener('abort', cancel);
      error ? reject(error) : resolve(reader.result.slice(reader.result.indexOf(',') + 1));
    };
    reader.onload = () => finish();
    reader.onerror = () => finish(new Error('附件读取失败，文件仍在，请重试或重新选择。'));
    reader.onabort = () => finish(new DOMException('Aborted', 'AbortError'));
    if (signal.aborted) return reject(new DOMException('Aborted', 'AbortError'));
    signal.addEventListener('abort', cancel, { once: true });
    reader.readAsDataURL(file);
  });
}

function validateResult(data, attachmentKind) {
  const shortText = (value, max) => typeof value === 'string' && value.trim() && Array.from(value).length <= max;
  if (!data || !shortText(data.content_summary, 160) || !Array.isArray(data.possible_meanings) || data.possible_meanings.length < 1 || data.possible_meanings.length > 2 ||
      !data.possible_meanings.every(item => shortText(item, 100)) || !shortText(data.reply, 120) ||
      !shortText(data.reply_tip, 80) || !['local_rules', 'jev', 'llm', 'unavailable'].includes(data.decision_source) ||
      !['templates', 'llm'].includes(data.generation_source)) {
    throw new Error('这次的结果格式不完整，原来的文字还在，请重试。');
  }
  const evidence = data.evidence;
  if (evidence !== undefined && (!evidence || !['image', 'video', 'article'].includes(evidence.kind) ||
      !shortText(evidence.visual_summary, 12000) || (evidence.visible_text !== undefined && (typeof evidence.visible_text !== 'string' || evidence.visible_text.length > 12000)) || typeof evidence.transcript !== 'string' || evidence.transcript.length > 20000 ||
      !['not_applicable', 'transcribed', 'silent'].includes(evidence.audio_status) || !shortText(evidence.coverage, 1000))) {
    throw new Error('识别内容不完整，输入和已有回复已保留，请重试。');
  }
  if (attachmentKind && (data.generation_source !== 'llm' || !evidence || evidence.kind !== attachmentKind ||
      attachmentKind === 'video' && !['transcribed', 'silent'].includes(evidence.audio_status))) {
    throw new Error(attachmentKind === 'article' ? '这次没有读取到文章正文，输入和已有回复已保留，请重试。' : '这次没有完成附件分析，输入和已有回复已保留，请重试。');
  }
}

function renderResult(data) {
  nativeWechat.lastShare = null;
  $('return-source').hidden = true;
  $('content-summary').textContent = data.content_summary;
  $('content-evidence').hidden = !data.evidence;
  $('content-evidence').open = false;
  if (data.evidence) {
    const evidence = data.evidence;
    $('visual-evidence-title').textContent = evidence.kind === 'article' ? '文章标题与正文' : '画面与文字';
    $('visual-summary').textContent = evidence.visual_summary + (evidence.visible_text ? (evidence.kind === 'article' ? '\n\n读取到的正文：\n' : '\n\n识别到的文字：\n') + evidence.visible_text : '');
    $('audio-evidence').hidden = evidence.kind !== 'video';
    $('audio-transcript').textContent = evidence.transcript;
    $('audio-status').textContent = evidence.audio_status === 'silent' ? '未识别到可用语音，分析依据为画面内容。' : '已识别视频中的语音，转写可能存在误差。';
    $('evidence-coverage').textContent = evidence.coverage;
  }
  $('possible-meanings').textContent = '';
  for (const meaning of data.possible_meanings) {
    const item = document.createElement('li');
    item.textContent = meaning;
    $('possible-meanings').append(item);
  }
  $('reply-tip-text').textContent = data.reply_tip;
  $('reply-text').value = data.reply;
  $('copy-status').textContent = '';
  const source = data.generation_source === 'templates' ? '本地模板，未调用 AI。' :
    data.decision_source === 'jev' ? 'Jev 分析 · AI 生成。' : data.decision_source === 'llm' ? 'AI 分析 · AI 生成。' : '分析服务暂不可用，AI 保留了多种理解。';
  $('result-note').textContent = data.generation_source === 'templates' && typeof data.note === 'string' ? data.note :
    source + ' ' + (typeof data.note === 'string' ? data.note : '发送前，请确认内容符合真实情况。');
  $('result-status').textContent = '可编辑，再复制';
  $('result-content').hidden = false;
  state.result = data;
}

async function generateReply() {
  if (state.busy || state.sharing) return;
  const text = $('source-text').value.trim();
  clearError();
  if ((!text && !state.attachment) || Array.from(text).length > 6000) {
    showError(!text ? '先粘贴一段消息，或添加一张图片、小视频。' : '文字请控制在 6000 字以内。');
    $('source-text').setAttribute('aria-invalid', 'true');
    document.querySelector('.source-wrap').classList.add('invalid');
    $('source-text').focus();
    return;
  }
  const unavailable = mediaError();
  if (unavailable) { showError(unavailable); return; }
  const payload = { text, recipient: $('recipient-select').value, tone: $('tone-select').value, mode: state.mode };
  state.modeSettled = true;
  const attachment = state.attachment;
  const article = !attachment && state.mode === 'live' && /https:\/\/mp\.weixin\.qq\.com\/s(?:[/?#\s]|$)/i.test(text);
  const controller = new AbortController();
  state.controller = controller;
  setBusy(true, attachment ? 'media' : article ? 'article' : 'text');
  try {
    if (attachment) payload.attachment = { kind: attachment.kind, name: attachment.file.name, mime: attachment.mime, data: await readAttachment(attachment.file, controller.signal) };
    const data = await requestJSON('/api/reply', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) }, { timeout: attachment ? 180000 : article ? 60000 : 32000, signal: controller.signal });
    if (controller.signal.aborted) throw new DOMException('Aborted', 'AbortError');
    validateResult(data, attachment && attachment.kind || (article ? 'article' : undefined));
    renderResult(data);
    state.submitted = { text, recipient: payload.recipient, tone: payload.tone };
    saveDraft();
    $('result-announcement').textContent = '分析和回复已准备好，可以编辑并复制。';
    $('result-title').focus({ preventScroll: true });
    if (document.body.dataset.host === 'android') {
      requestAnimationFrame(() => scrollIntoViewNow($('reply-text'), 'start'));
    } else if (matchMedia('(max-width: 650px)').matches) {
      $('result-card').scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth', block: 'start' });
    }
  } catch (error) {
    const cancelled = controller.signal.aborted;
    showError(cancelled ? '已取消，输入、附件和已有回复已保留。' : error.message);
    $('result-announcement').textContent = cancelled ? '已取消生成，已有内容已保留。' : '这次没有生成成功，已有内容已保留，可以重试。';
  } finally {
    state.controller = null;
    setBusy(false);
    if (pendingConfig) applyConfig(pendingConfig);
  }
}

async function copyReply() {
  if (state.busy || state.sharing || !state.result) return;
  const reply = $('reply-text').value;
  if (!reply.trim()) {
    $('copy-status').textContent = '先写一句回复，再复制吧。';
    $('reply-text').focus();
    return;
  }
  try {
    if (!navigator.clipboard || typeof navigator.clipboard.writeText !== 'function') throw new Error('Clipboard unavailable');
    await navigator.clipboard.writeText(reply);
    $('copy-status').textContent = activeInboundId ? nativeWechat.post ? '已复制，返回后在原应用输入框粘贴发送。' : '已复制，请用系统返回键回原应用粘贴发送。' : '已复制，回微信粘贴即可。';
    $('return-source').hidden = !(activeInboundId && nativeWechat.post);
  } catch {
    $('reply-text').focus();
    $('reply-text').select();
    try {
      if (typeof document.execCommand === 'function' && document.execCommand('copy')) {
        $('copy-status').textContent = activeInboundId ? nativeWechat.post ? '已复制，返回后在原应用输入框粘贴发送。' : '已复制，请用系统返回键回原应用粘贴发送。' : '已复制，回微信粘贴即可。';
        $('return-source').hidden = !(activeInboundId && nativeWechat.post);
        return;
      }
    } catch { /* Keep the text selected for manual copy. */ }
    $('copy-status').textContent = '已选中文字，请长按或按 Ctrl / ⌘ + C 复制。';
  }
}

async function shareReply() {
  if (state.busy || state.sharing || !state.result) return;
  const text = $('reply-text').value;
  if (!text.trim()) return;
  if (!canShareReply()) {
    updateControls();
    $('copy-status').textContent = '当前无法打开分享菜单，可点“复制回复”后回微信发送。';
    return;
  }
  state.sharing = true;
  updateControls();
  try {
    // Call before the first await so the browser keeps this click's user activation.
    const shared = navigator.share({ text });
    await shared;
    $('copy-status').textContent = '已交给所选应用，请在应用内确认发送。';
  } catch (error) {
    // AbortError also covers a system with no available share targets.
    const reasons = {
      AbortError: '分享未完成：可能已取消，或系统没有可用的分享应用。',
      NotAllowedError: '当前浏览器未允许打开系统分享。',
      InvalidStateError: '分享窗口仍在使用中，或当前页面未处于活动状态。',
      DataError: '系统未能将回复交给所选应用。',
      TypeError: '当前系统不支持分享这段文字。'
    };
    const reason = error && hasOwn(reasons, error.name) ? reasons[error.name] : '系统分享未完成。';
    $('copy-status').textContent = reason + '回复已保留，可点“复制回复”回微信粘贴。';
  } finally {
    state.sharing = false;
    updateControls();
    if (pendingConfig) applyConfig(pendingConfig);
  }
}

async function shareWechatReply() {
  if (state.busy || state.sharing || !state.result || !nativeWechat.available) return;
  const text = $('reply-text').value;
  if (!text.trim()) return;
  if (new TextEncoder().encode(text).length > 10240) {
    $('copy-status').textContent = '回复较长，请精简后再分享给微信。';
    return;
  }
  state.sharing = true;
  updateControls();
  try {
    const result = await requestWechat('shareText', text);
    $('copy-status').textContent = result.status === 'handoff' ? '已交给微信，请选择联系人并确认发送。' :
      result.status === 'cancelled' ? '已取消微信分享，回复已保留。' :
      result.status === 'completed' ? '微信分享流程已返回，请到聊天中确认。' : '微信分享未完成，可复制回复后发送。';
  } catch (error) { $('copy-status').textContent = error.message; }
  finally { state.sharing = false; updateControls(); if (pendingConfig) applyConfig(pendingConfig); }
}

async function initialize() {
  if (state.initializing) return;
  state.initializing = true;
  $('retry-init').disabled = true;
  const results = await settle([requestJSON('/api/config'), requestJSON('/api/examples')]);
  const [config, sample] = results;
  const connected = config.status === 'fulfilled' && config.value && typeof config.value.liveAvailable === 'boolean';
  applyConfig({ connected, liveAvailable: connected && config.value.liveAvailable, mediaAvailable: connected && config.value.mediaAvailable === true, videoAvailable: connected && config.value.videoAvailable === true, mode: config.status === 'fulfilled' && config.value ? config.value.mode : 'demo' });
  if (sample.status === 'fulfilled' && Array.isArray(sample.value) && sample.value.length && sample.value.every(item => typeof item.text === 'string')) examples = sample.value;
  $('init-error').hidden = connected && sample.status === 'fulfilled';
  state.initializing = false;
  $('retry-init').disabled = state.busy || state.sharing;
  if (pendingAndroidAutoGenerate) {
    const pending = pendingAndroidAutoGenerate;
    pendingAndroidAutoGenerate = null;
    if (!pendingInboundShare && $('source-text').value === pending.text && $('recipient-select').value === pending.recipient && state.attachment === pending.attachment) generateReply();
  }
}

$('translate-form').addEventListener('submit', event => { event.preventDefault(); generateReply(); });
$('source-text').addEventListener('input', inputChanged);
$('recipient-select').addEventListener('change', inputChanged);
$('tone-select').addEventListener('change', inputChanged);
$('source-text').addEventListener('keydown', event => { if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') { event.preventDefault(); generateReply(); } });
$('try-example').addEventListener('click', () => {
  if (state.busy || state.sharing) return;
  state.exampleIndex = (state.exampleIndex + 1) % examples.length;
  $('source-text').value = examples[state.exampleIndex].text;
  inputChanged();
  $('source-text').focus();
});
$('mode-select').addEventListener('change', () => { state.modeSettled = true; setMode(); saveDraft(); });
$('clear-draft').addEventListener('click', clearDraft);
$('attachment-input').addEventListener('change', chooseAttachment);
$('inbound-accept').addEventListener('click', acceptInboundShare);
$('inbound-dismiss').addEventListener('click', () => dismissInboundShare(false));
$('inbound-return').addEventListener('click', () => dismissInboundShare(true));
$('return-source').addEventListener('click', returnToSource);
$('remove-attachment').addEventListener('click', () => { if (!state.busy && !state.sharing) updateAttachment(null); });
$('cancel-request').addEventListener('click', () => { if (state.controller) state.controller.abort(); });
$('copy-reply').addEventListener('click', copyReply);
$('share-reply').addEventListener('click', shareReply);
$('wechat-reply').addEventListener('click', shareWechatReply);
$('reply-text').addEventListener('input', () => {
  nativeWechat.lastShare = null;
  $('return-source').hidden = true;
  $('copy-status').textContent = '';
  updateControls();
  saveDraft();
});
$('retry-init').addEventListener('click', initialize);
window.addEventListener('pagehide', saveDraft);
document.addEventListener('visibilitychange', () => { if (document.hidden) saveDraft(); });
restoreDraft();
countInput();
updateControls();
initializeWechat();
initialize();
