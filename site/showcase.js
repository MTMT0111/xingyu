'use strict';
const reply = document.getElementById('demo-reply');
const original = reply.value;
const status = document.getElementById('copy-status');
document.getElementById('reset').addEventListener('click', () => {
  reply.value = original; status.textContent = '已恢复实测原始输出。';
});
document.getElementById('copy').addEventListener('click', async () => {
  if (!reply.value.trim()) { reply.focus(); status.textContent = '先写一句你想回复的话。'; return; }
  try {
    await navigator.clipboard.writeText(reply.value);
    status.textContent = '已复制。回到微信，确认对象后粘贴发送。';
  } catch {
    reply.focus(); reply.select(); reply.setSelectionRange(0, reply.value.length);
    status.textContent = '已选中文字，请长按复制，或按 Ctrl+C。';
  }
});
