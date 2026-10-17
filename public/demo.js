(function (root) {
  'use strict';

  const examples = [
    { text: '今天降温了，多穿点。忙也要记得吃饭，别总熬夜。' },
    { text: '这周末有空回来吃饭吗？想给你做你爱吃的菜。' },
    { text: '给你分享个视频，你有空看看，我觉得挺有意思的。' }
  ];

  const replies = {
    care: {
      meanings: ['也许是在关心你的日常生活，想得到一点回应。', '也可能只是顺手叮嘱一句，不一定需要详细汇报。'],
      tip: '先接住这份关心，再补一句愿意分享的真实近况。',
      warm: '看到你的叮嘱啦，谢谢你一直惦记我。你也要好好照顾自己呀。',
      brief: '收到你的关心啦，你也照顾好自己。',
      boundary: '知道你惦记我，谢谢你。我想按自己的节奏照顾好生活，也希望你放心些。'
    },
    invitation: {
      meanings: ['也许是在邀请你一起吃顿饭，想多一点相处的时间。', '也可能是在确认安排，具体时间还可以一起商量。'],
      tip: '接住邀约，时间没确定就坦诚说出来，不必先答应。',
      warm: '看到邀请就觉得暖暖的。时间先一起商量，好不好？你比较方便哪天？',
      brief: '收到邀请啦，时间我们再商量，好吗？',
      boundary: '谢谢你惦记我。回家的时间我还需要自己安排，我们先别把日期定下来，好吗？'
    },
    sharing: {
      meanings: ['也许是觉得内容有意思，想和你分享一个话题。', '也可能只是随手转给你；仅凭这条消息还不知道具体内容。'],
      tip: '先回应分享，不假装看过；问问对方最在意哪一点。',
      warm: '收到你的分享啦，谢谢你想到我。是什么地方让你觉得有意思呀？想听你讲讲。',
      brief: '收到分享啦，哪一点最让你有感触？',
      boundary: '谢谢你想到我。内容我不一定能及时看完，可以先和我说说你想分享的重点吗？'
    },
    health: {
      meanings: ['也许是在分享一条健康说法，或想提醒你照顾自己。', '也可能只是希望听听你的看法；仅凭消息不能判断说法是否可靠。'],
      tip: '接住关心，把健康说法留给可靠来源核实；涉及用药时请问医生或药师。',
      warm: '谢谢你惦记我。这里的健康说法咱们先核实，涉及用药请问医生或药师，你也照顾好自己。',
      brief: '收到关心啦，健康说法先核实，用药请问医生或药师。',
      boundary: '我知道你是关心我。不过健康建议需要先核实，涉及用药请问医生或药师，希望你理解。'
    },
    pressure: {
      meanings: ['也许是想了解你的打算，但比较和催问可能让对话有压力。', '也可能是彼此对时间安排有不同期待，需要把想法说清楚。'],
      tip: '承认对方的关心，同时说清自己的节奏，不用虚构进展。',
      warm: '我知道你很关心我的以后。这件事我想按自己的节奏来，你愿意听听我的想法吗？',
      brief: '我知道你关心我，这件事想按自己的节奏来。',
      boundary: '我知道你关心我，但反复比较会让我有压力。这件事请让我自己决定，好吗？'
    },
    unclear: {
      meanings: ['也许是想开始聊一件事，目前还不知道具体内容。', '也可能只是随口问候，可以先用一个开放的问题接住。'],
      tip: '信息不够时先问一句，认真回应不需要猜中潜台词。',
      warm: '看到你的消息啦，想跟我聊什么？我想听你说说。',
      brief: '看到消息啦，有什么事呀？',
      boundary: '看到消息啦，可以先告诉我是什么事吗？我们再慢慢聊。'
    }
  };

  function reply(input) {
    const text = String(input && input.text || '').trim();
    if (!text || Array.from(text).length > 6000) throw new Error('请粘贴 1–6000 字的消息或正文。');
    if (input && input.attachment) throw new Error('公开 Demo 只演示文字；图片和视频需连接真实 AI 服务。');
    const health = /停药|用药|吃药|治疗|治愈|偏方|降压|降糖|癌|养生|保健|疗效|排毒|饮食禁忌|疾病|千万别再吃|不能吃/.test(text);
    const topic = health ? 'health'
      : /对象|结婚|相亲|催|生孩子|工资|攀比/.test(text) ? 'pressure'
      : /回家|回来|聚餐|一起吃|哪天见/.test(text) ? 'invitation'
      : /视频|分享|转发|文章|链接|公众号|有意思|鸡汤|人生/.test(text) ? 'sharing'
      : /吃饭|熬夜|多穿|降温|天[凉冷]|保暖|关心|照顾|身体|保重/.test(text) ? 'care' : 'unclear';
    const template = replies[topic];
    const tone = ['warm', 'brief', 'boundary'].includes(input.tone) ? input.tone : 'warm';
    let generated = template[tone];
    if (input.recipient === 'mother') generated = `妈，${generated}`;
    else if (input.recipient === 'father') generated = `爸，${generated}`;
    else if (input.recipient === 'elder') generated = generated.replace(/你/g, '您');
    return {
      content_summary: '原文摘录：' + Array.from(text).slice(0, 140).join('') + (Array.from(text).length > 140 ? '…' : ''),
      possible_meanings: template.meanings,
      reply_tip: template.tip,
      reply: generated,
      decision_source: 'local_rules',
      generation_source: 'templates',
      note: '公开 Demo · 本地规则和人工文案，未上传内容、未调用 AI。'
    };
  }

  root.HaoHaoHuiDemo = {
    examples,
    config: { mode: 'demo', liveAvailable: false, mediaAvailable: false, videoAvailable: false },
    reply
  };
}(typeof window === 'object' ? window : this));
