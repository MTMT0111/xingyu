const MIME_BY_EXTENSION = {
  jpg: ['image', 'image/jpeg'],
  jpeg: ['image', 'image/jpeg'],
  png: ['image', 'image/png'],
  webp: ['image', 'image/webp'],
  mp4: ['video', 'video/mp4'],
  webm: ['video', 'video/webm'],
  mov: ['video', 'video/quicktime']
};

function codepointLength(value) {
  return Array.from(String(value || '')).length;
}

function normalizeBaseUrl(value) {
  const url = String(value || '').trim().replace(/\/+$/, '');
  const local = /^http:\/\/(?:127\.0\.0\.1|localhost)(?::[1-9]\d{0,4})?$/.test(url);
  const secure = /^https:\/\/[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?(?::[1-9]\d{0,4})?$/i.test(url);
  if (!local && !secure) throw new Error('服务地址需为 HTTPS 根地址；开发者工具可用 http://127.0.0.1:4173。');
  return url;
}

function initialBaseUrl(savedValue, platform) {
  const saved = String(savedValue || '').trim().replace(/\/+$/, '');
  const local = /^http:\/\/(?:127\.0\.0\.1|localhost)(?::[1-9]\d{0,4})?$/.test(saved);
  if (platform === 'devtools') return saved || 'http://127.0.0.1:4173';
  return local ? '' : saved;
}

function base64Ascii(value) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
  let output = '';
  for (let index = 0; index < value.length; index += 3) {
    const a = value.charCodeAt(index);
    const b = index + 1 < value.length ? value.charCodeAt(index + 1) : 0;
    const c = index + 2 < value.length ? value.charCodeAt(index + 2) : 0;
    if (a > 127 || b > 127 || c > 127) throw new Error('访问口令只能使用无空格 ASCII 字符。');
    const bits = (a << 16) | (b << 8) | c;
    output += alphabet[(bits >> 18) & 63] + alphabet[(bits >> 12) & 63]
      + (index + 1 < value.length ? alphabet[(bits >> 6) & 63] : '=')
      + (index + 2 < value.length ? alphabet[bits & 63] : '=');
  }
  return output;
}

function authorizationHeaders(password) {
  if (!password) return {};
  if (!/^[\x21-\x7E]{16,128}$/.test(password)) throw new Error('访问口令需为 16–128 位无空格 ASCII 字符。');
  return { Authorization: `Basic ${base64Ascii(`trial:${password}`)}` };
}

function classifyFile(file) {
  const path = String(file.path || file.tempFilePath || '');
  if (!path) throw new Error('没有读取到文件路径，请重新选择。');
  const fallbackName = path.split(/[\\/]/).pop() || '';
  const name = String(file.name || fallbackName || '微信文件').slice(0, 120);
  const extension = name.includes('.') ? name.split('.').pop().toLowerCase() : '';
  const mimeType = String(file.mimeType || (String(file.type || '').includes('/') ? file.type : '')).toLowerCase();
  const mimeDefinition = Object.values(MIME_BY_EXTENSION).find(([, mime]) => mime === mimeType);
  const definition = mimeDefinition || MIME_BY_EXTENSION[extension];
  if (!definition) throw new Error('请选择 JPG、PNG、WebP、MP4、WebM 或 MOV 文件。');
  const size = Number(file.size);
  if (!Number.isFinite(size) || size <= 0) throw new Error('没有读取到有效文件。');
  const [kind, mime] = definition;
  const expectedKind = file.fileType || (['image', 'video'].includes(file.type) ? file.type : '');
  if (['image', 'video'].includes(expectedKind) && expectedKind !== kind) throw new Error('文件类型与扩展名不一致。');
  if (mimeType && mimeType.split('/')[0] !== kind) throw new Error('文件类型与扩展名不一致。');
  const maxBytes = (kind === 'image' ? 4 : 20) * 1024 * 1024;
  if (size > maxBytes) throw new Error(kind === 'image' ? '图片不能超过 4 MB。' : '视频不能超过 20 MB。');
  return { path, name, size, kind, mime };
}

function videoMaterialFromLaunch(options) {
  if (!options || Number(options.scene) !== 1173 || !Array.isArray(options.forwardMaterials)) return null;
  const material = options.forwardMaterials.find(item => item && /^video\//i.test(String(item.type || '')));
  if (!material || !material.path || !Number.isFinite(Number(material.size)) || Number(material.size) <= 0) return null;
  return {
    path: String(material.path),
    name: String(material.name || '微信视频').slice(0, 120),
    size: Number(material.size),
    mimeType: String(material.type).toLowerCase(),
    fileType: 'video'
  };
}

function validateMessage(text, hasAttachment) {
  const clean = String(text || '').trim();
  if (!clean && !hasAttachment) throw new Error('请粘贴文字、公众号链接，或选择图片和视频。');
  if (codepointLength(clean) > 6000) throw new Error('文字不能超过 6000 字。');
  return clean;
}

function validateResult(value, evidenceKind) {
  const validMeanings = Array.isArray(value && value.possible_meanings)
    && value.possible_meanings.length >= 1
    && value.possible_meanings.length <= 2
    && value.possible_meanings.every(item => typeof item === 'string' && item.trim());
  if (!value || typeof value.content_summary !== 'string' || !value.content_summary.trim()
    || !validMeanings || typeof value.reply_tip !== 'string' || typeof value.reply !== 'string' || !value.reply.trim()) {
    throw new Error('这次返回的内容不完整，请重试。');
  }
  if (evidenceKind && (value.generation_source !== 'llm' || !value.evidence || value.evidence.kind !== evidenceKind
    || evidenceKind === 'video' && !['transcribed', 'silent'].includes(value.evidence.audio_status))) {
    throw new Error(evidenceKind === 'article' ? '这次没有读取到公众号正文，请粘贴正文或重试。' : '这次没有完成媒体分析，请保留文件后重试。');
  }
  return value;
}

function generateLocalReply(text, variant = 0) {
  const health = /停药|用药|吃药|治疗|治愈|偏方|降压|降糖|癌|养生|保健|疗效|排毒|饮食禁忌|疾病|千万别再吃|不能吃/.test(text);
  const topic = health ? 'health'
    : /对象|结婚|相亲|催|生孩子|工资|攀比/.test(text) ? 'pressure'
    : /回家|回来|聚餐|一起吃|哪天见/.test(text) ? 'invitation'
    : /视频|分享|转发|文章|链接|公众号|有意思|鸡汤|人生/.test(text) ? 'sharing'
    : /吃饭|熬夜|多穿|降温|天[凉冷]|保暖|关心|照顾|身体|保重/.test(text) ? 'care' : 'unclear';
  const templates = {
    care: {
      meanings: ['也许是在关心你的日常生活，想得到一点回应。', '也可能只是顺手叮嘱一句，不一定需要详细汇报。'],
      tip: '先接住这份关心，再补一句愿意分享的真实近况。',
      replies: ['看到你的叮嘱啦，谢谢你一直惦记我。你也要好好照顾自己呀。', '谢谢你的关心，读着心里暖暖的。你最近过得怎么样？']
    },
    invitation: {
      meanings: ['也许是在邀请你一起吃顿饭，想多一点相处的时间。', '也可能是在确认安排，具体时间还可以一起商量。'],
      tip: '接住邀约，时间没确定就坦诚说出来，不必先答应。',
      replies: ['看到邀请就觉得暖暖的。时间先一起商量，好不好？你比较方便哪天？', '谢谢你惦记着一起吃饭。我们先聊聊时间，方便哪天再一起定。']
    },
    sharing: {
      meanings: ['也许是觉得内容有意思，想和你分享一个话题。', '也可能只是随手转给你；仅凭这条消息还不知道具体内容。'],
      tip: '先回应分享，不假装看过；问问对方最在意哪一点。',
      replies: ['收到你的分享啦，谢谢你想到我。是什么地方让你觉得有意思呀？想听你讲讲。', '谢谢你分享给我。你最想让我留意哪一点？可以先跟我说说呀。']
    },
    health: {
      meanings: ['也许是在分享一条健康说法，或想提醒你照顾自己。', '也可能只是希望听听你的看法；仅凭消息不能判断说法是否可靠。'],
      tip: '接住关心，把健康说法留给可靠来源核实；涉及用药时请问医生或药师。',
      replies: ['谢谢你惦记我。这里的健康说法咱们先核实，涉及用药请问医生或药师，你也照顾好自己。', '你的关心我收到了。健康建议先核实，涉及用药请问医生或药师，咱们别急着照做。']
    },
    pressure: {
      meanings: ['也许是想了解你的打算，但比较和催问可能让对话有压力。', '也可能是彼此对时间安排有不同期待，需要把想法说清楚。'],
      tip: '承认对方的关心，同时说清自己的节奏，不用虚构进展。',
      replies: ['我知道你很关心我的以后。这件事我想按自己的节奏来，你愿意听听我的想法吗？', '谢谢你替我操心。我也希望把生活过好，我们慢慢聊，不用跟别人比较。']
    },
    unclear: {
      meanings: ['也许是想开始聊一件事，目前还不知道具体内容。', '也可能只是随口问候，可以先用一个开放的问题接住。'],
      tip: '信息不够时先问一句，认真回应不需要猜中潜台词。',
      replies: ['看到你的消息啦，想跟我聊什么？我想听你说说。', '消息收到啦，是有什么事想和我说吗？']
    }
  };
  const template = templates[topic];
  return {
    content_summary: '原文摘录：' + Array.from(text).slice(0, 140).join('') + (codepointLength(text) > 140 ? '…' : ''),
    possible_meanings: template.meanings,
    reply_tip: template.tip,
    reply: template.replies[variant % template.replies.length],
    decision_source: 'local_rules',
    generation_source: 'templates',
    note: '离线体验 · 本地规则和人工文案，内容没有上传，也没有调用 AI。'
  };
}

module.exports = { authorizationHeaders, classifyFile, codepointLength, generateLocalReply, initialBaseUrl, normalizeBaseUrl, validateMessage, validateResult, videoMaterialFromLaunch };
