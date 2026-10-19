(function (root) {
  'use strict';

  var key = 'xingyu-history-v1';
  var limit = 20;
  var recipients = ['auto', 'mother', 'father', 'elder'];
  var tones = ['warm', 'brief', 'boundary'];

  function text(value, max) {
    if (typeof value !== 'string' || value.length > max) throw new Error('Invalid history text');
    return value;
  }

  function normalize(item) {
    if (!item || typeof item !== 'object' || typeof item.id !== 'string' || item.id.length > 100 ||
        !Number.isFinite(item.createdAt) || !recipients.includes(item.recipient) || !tones.includes(item.tone) ||
        !Array.isArray(item.possibleMeanings) || item.possibleMeanings.length > 5) throw new Error('Invalid history item');
    return {
      id: item.id,
      createdAt: item.createdAt,
      recipient: item.recipient,
      tone: item.tone,
      source: text(item.source, 600),
      summary: text(item.summary, 1000),
      possibleMeanings: item.possibleMeanings.map(function (value) { return text(value, 200); }),
      replyTip: text(item.replyTip, 500),
      reply: text(item.reply, 1000)
    };
  }

  function read(storage) {
    var raw = storage.getItem(key);
    if (!raw) return [];
    if (raw.length > 100000) throw new Error('History too large');
    try {
      var value = JSON.parse(raw);
      if (!Array.isArray(value)) throw new Error('Invalid history');
      return value.map(normalize).slice(0, limit);
    } catch (error) {
      storage.removeItem(key);
      return [];
    }
  }

  function write(storage, items) {
    var normalized = items.slice(0, limit).map(normalize);
    storage.setItem(key, JSON.stringify(normalized));
    return normalized;
  }

  function add(storage, item) {
    var entry = normalize(item);
    return write(storage, [entry].concat(read(storage).filter(function (saved) { return saved.id !== entry.id; })));
  }

  function updateReply(storage, id, reply) {
    return write(storage, read(storage).map(function (item) {
      return item.id === id ? Object.assign({}, item, { reply: text(reply, 1000) }) : item;
    }));
  }

  function clear(storage) { storage.removeItem(key); }

  root.ZhiyiHistory = { key: key, limit: limit, read: read, add: add, updateReply: updateReply, clear: clear };
}(window));
