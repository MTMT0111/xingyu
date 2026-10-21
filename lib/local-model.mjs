import { readFile, writeFile, rename, unlink } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';

// Keep all other local settings; replace only these exact dotenv assignments.
export async function saveDeepSeekKey(path, key) {
  if (typeof key !== 'string' || !/^[A-Za-z0-9_-]{16,256}$/.test(key)) throw new Error('密钥格式无效。');
  let previous = '';
  try { previous = await readFile(path, 'utf8'); } catch (error) { if (error.code !== 'ENOENT') throw error; }
  const values = { LLM_API_KEY: key, LLM_BASE_URL: 'https://api.deepseek.com', LLM_MODEL: 'deepseek-flash' };
  const lines = previous.split(/\r?\n/).filter(line => !/^\s*(?:export\s+)?(LLM_API_KEY|LLM_BASE_URL|LLM_MODEL)\s*=/.test(line));
  const temporary = path + '.' + randomUUID() + '.tmp';
  try {
    await writeFile(temporary, lines.join('\n').trimEnd() + '\n' + Object.entries(values).map(([name, value]) => `${name}="${value}"`).join('\n') + '\n', { mode: 0o600, flag: 'wx' });
    await rename(temporary, path);
  } finally { await unlink(temporary).catch(() => {}); }
}

// Session-only, bounded evidence reuse. No chat recipient, reply or media credentials are stored.
export class EvidenceCache {
  constructor({ ttlMs = 600000, limit = 8, now = Date.now } = {}) { this.entries = new Map(); this.ttlMs = ttlMs; this.limit = limit; this.now = now; }
  get(key) {
    const entry = this.entries.get(key);
    if (!entry || this.now() >= entry.expires) { this.entries.delete(key); return null; }
    return structuredClone(entry.evidence);
  }
  set(key, evidence) {
    if (!key || Buffer.byteLength(JSON.stringify(evidence)) > 1024 * 1024) return;
    this.entries.delete(key);
    this.entries.set(key, { evidence: structuredClone(evidence), expires: this.now() + this.ttlMs });
    while (this.entries.size > this.limit) this.entries.delete(this.entries.keys().next().value);
  }
  clear() { this.entries.clear(); }
}
