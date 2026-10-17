const {
  authorizationHeaders,
  classifyFile,
  codepointLength,
  generateLocalReply,
  initialBaseUrl,
  normalizeBaseUrl,
  validateMessage,
  validateResult
} = require('../../utils');

const DEFAULT_API_BASE = '';
const DEFAULT_RECIPIENT = 'auto';
const DEFAULT_TONE = 'warm';

function wxRequest({ url, method = 'GET', data, header, timeout = 60000 }) {
  return new Promise((resolve, reject) => {
    wx.request({
      url, method, data, header, timeout,
      success(response) {
        if (response.statusCode >= 200 && response.statusCode < 300) resolve(response.data);
        else reject(new Error(response.data && response.data.error || `服务返回 ${response.statusCode}`));
      },
      fail(error) { reject(new Error(error.errMsg || '暂时连接不上服务。')); }
    });
  });
}

function readBase64(path) {
  return new Promise((resolve, reject) => {
    wx.getFileSystemManager().readFile({
      filePath: path,
      encoding: 'base64',
      success(result) { resolve(result.data); },
      fail() { reject(new Error('文件读取失败，请重新选择。')); }
    });
  });
}

function getVideoInfo(path) {
  return new Promise((resolve, reject) => {
    wx.getVideoInfo({
      src: path,
      success: resolve,
      fail() { reject(new Error('无法读取视频时长，请重新选择。')); }
    });
  });
}

Page({
  data: {
    apiBase: DEFAULT_API_BASE,
    apiBaseDraft: DEFAULT_API_BASE,
    accessCode: '',
    accessCodeDraft: '',
    showSettings: false,
    connected: false,
    connecting: false,
    connectionLabel: '离线文字体验',
    liveAvailable: false,
    mediaAvailable: false,
    videoAvailable: false,
    mode: 'demo',
    sourceText: '',
    sourceCount: 0,
    attachment: null,
    attachmentDetail: '',
    busy: false,
    error: '',
    result: null,
    replyText: '',
    variant: 0,
    showAnalysis: false,
    showEvidence: false,
    evidenceText: ''
  },

  onLoad() {
    let apiBase = DEFAULT_API_BASE;
    let accessCode = '';
    try {
      const savedBase = wx.getStorageSync('haohaohui_api_base') || '';
      const device = typeof wx.getDeviceInfo === 'function' ? wx.getDeviceInfo()
        : typeof wx.getSystemInfoSync === 'function' ? wx.getSystemInfoSync() : {};
      apiBase = initialBaseUrl(savedBase, device.platform);
      if (savedBase && !apiBase) wx.removeStorageSync('haohaohui_api_base');
      accessCode = wx.getStorageSync('haohaohui_access_code') || '';
    } catch (_) {}
    this.setData({ apiBase, apiBaseDraft: apiBase, accessCode, accessCodeDraft: accessCode });
    if (typeof wx.showShareMenu === 'function') {
      wx.showShareMenu({ menus: ['shareAppMessage', 'shareTimeline'] });
    }
    this.checkConnection();
    this.consumeIncomingVideo();
  },

  onShow() {
    this.consumeIncomingVideo();
  },

  consumeIncomingVideo() {
    const app = getApp();
    const material = app.globalData && app.globalData.pendingVideo;
    if (!material) return;
    app.globalData.pendingVideo = null;
    this.setData({ sourceText: '', sourceCount: 0, attachment: null, attachmentDetail: '' });
    this.clearResult();
    this.acceptFile(material);
  },

  onShareAppMessage() {
    return { title: '言外：看懂分享，再回一句', path: '/pages/index/index' };
  },

  onShareTimeline() {
    return { title: '言外：看懂分享，再回一句' };
  },

  onAddToFavorites() {
    return { title: '言外｜打开就能准备回复' };
  },

  request(path, options = {}) {
    const header = { 'Content-Type': 'application/json', ...authorizationHeaders(this.data.accessCode) };
    return wxRequest({ url: this.data.apiBase + path, header, ...options });
  },

  async checkConnection(showError = false) {
    const sequence = (this.connectionSequence || 0) + 1;
    this.connectionSequence = sequence;
    if (!this.data.apiBase) {
      this.setData({
        connecting: false,
        connected: false,
        liveAvailable: false,
        mediaAvailable: false,
        videoAvailable: false,
        mode: 'demo',
        connectionLabel: '离线文字体验',
        ...(showError ? { error: '' } : {})
      });
      return;
    }
    this.setData({ connecting: true, connectionLabel: '正在连接服务…', ...(showError ? { error: '' } : {}) });
    try {
      const config = await this.request('/api/config');
      if (this.connectionSequence !== sequence) return;
      const live = Boolean(config.liveAvailable);
      this.setData({
        connected: true,
        liveAvailable: live,
        mediaAvailable: Boolean(config.mediaAvailable),
        videoAvailable: Boolean(config.videoAvailable),
        mode: live ? 'live' : 'demo',
        connectionLabel: live ? '真实 AI · 在线生成' : '本地示例 · 非实时 AI',
        error: ''
      });
    } catch (error) {
      if (this.connectionSequence !== sequence) return;
      this.setData({ connected: false, connectionLabel: '离线文字体验', ...(showError ? { error: `${error.message} 已切换到离线文字体验。` } : {}) });
    } finally {
      if (this.connectionSequence === sequence) this.setData({ connecting: false });
    }
  },

  toggleSettings() {
    this.setData({ showSettings: !this.data.showSettings, error: '' });
  },

  onApiBaseInput(event) { this.setData({ apiBaseDraft: event.detail.value }); },
  onAccessCodeInput(event) { this.setData({ accessCodeDraft: event.detail.value }); },

  async saveSettings() {
    try {
      const apiBase = normalizeBaseUrl(this.data.apiBaseDraft);
      const accessCode = this.data.accessCodeDraft.trim();
      authorizationHeaders(accessCode);
      wx.setStorageSync('haohaohui_api_base', apiBase);
      wx.setStorageSync('haohaohui_access_code', accessCode);
      this.setData({ apiBase, accessCode, showSettings: false, connected: false, result: null, replyText: '', error: '' });
      await this.checkConnection(true);
    } catch (error) { this.setData({ error: error.message }); }
  },

  resetSettings() {
    try {
      wx.removeStorageSync('haohaohui_api_base');
      wx.removeStorageSync('haohaohui_access_code');
    } catch (_) {}
    this.setData({ apiBase: DEFAULT_API_BASE, apiBaseDraft: DEFAULT_API_BASE, accessCode: '', accessCodeDraft: '', result: null, replyText: '', error: '' });
    this.checkConnection(true);
  },

  clearResult() {
    this.setData({ result: null, replyText: '', variant: 0, showAnalysis: false, showEvidence: false, evidenceText: '' });
  },

  onSourceInput(event) {
    const sourceText = event.detail.value;
    this.setData({ sourceText, sourceCount: codepointLength(sourceText), error: '' });
    this.clearResult();
  },

  clearInput() {
    if (this.data.busy) return;
    this.setData({ sourceText: '', sourceCount: 0, attachment: null, attachmentDetail: '', error: '' });
    this.clearResult();
  },

  pasteClipboard() {
    wx.getClipboardData({
      success: result => {
        const sourceText = String(result.data || '');
        if (!sourceText.trim()) return this.setData({ error: '剪贴板里没有文字。' });
        this.setData({ sourceText, sourceCount: codepointLength(sourceText), error: '' });
        this.clearResult();
      },
      fail: () => this.setData({ error: '没有读取剪贴板，请长按输入框粘贴。' })
    });
  },

  chooseFromChat() {
    if (!wx.chooseMessageFile) return this.setData({ error: '当前微信版本不支持选择聊天文件；普通文字请先复制，再点“粘贴文字”。' });
    wx.chooseMessageFile({
      count: 1,
      type: 'all',
      success: result => this.acceptFile(result.tempFiles[0]),
      fail: error => { if (!/cancel/i.test(error.errMsg || '')) this.setData({ error: '没有选中聊天文件；普通文字请先复制，再点“粘贴文字”。' }); }
    });
  },

  chooseMedia() {
    if (!wx.chooseMedia) return this.setData({ error: '当前微信版本不支持选择媒体。' });
    wx.chooseMedia({
      count: 1,
      mediaType: ['image', 'video'],
      sourceType: ['album', 'camera'],
      maxDuration: 60,
      success: result => {
        const file = result.tempFiles[0];
        this.acceptFile({ path: file.tempFilePath, size: file.size, fileType: file.fileType || result.type });
      },
      fail: error => { if (!/cancel/i.test(error.errMsg || '')) this.setData({ error: '没有选中图片或视频，请重试。' }); }
    });
  },

  async acceptFile(rawFile) {
    try {
      const attachment = classifyFile(rawFile);
      let duration = null;
      if (attachment.kind === 'video') {
        const info = await getVideoInfo(attachment.path);
        duration = Number(info.duration);
        if (!Number.isFinite(duration) || duration <= 0 || duration > 60) throw new Error('请选择 60 秒以内的视频。');
      }
      const detail = `${(attachment.size / 1024 / 1024).toFixed(2)} MB${duration ? ` · ${duration.toFixed(1)} 秒` : ''}`;
      this.setData({ attachment: { ...attachment, duration }, attachmentDetail: detail, error: '' });
      this.clearResult();
    } catch (error) { this.setData({ error: error.message }); }
  },

  removeAttachment() {
    if (this.data.busy) return;
    this.setData({ attachment: null, attachmentDetail: '', error: '' });
    this.clearResult();
  },

  async generateReply() {
    if (this.data.busy) return;
    const previous = this.data.result;
    try {
      const text = validateMessage(this.data.sourceText, Boolean(this.data.attachment));
      this.setData({ busy: true, error: '', showSettings: false });
      if (!this.data.connected) await this.checkConnection(true);
      if (!this.data.connected) {
        if (this.data.attachment) throw new Error('图片和视频分析需要先在设置中连接 HTTPS 服务。');
        const variant = this.data.result ? (this.data.variant + 1) % 2 : this.data.variant;
        const result = validateResult(generateLocalReply(text, variant));
        this.setData({ result, replyText: result.reply, variant, evidenceText: '', showEvidence: false, error: '' });
        wx.pageScrollTo({ selector: '#result', duration: 220 });
        return;
      }
      if (this.data.attachment && !this.data.mediaAvailable) throw new Error('当前服务还没有配置图片/视频分析。');
      if (this.data.attachment && this.data.attachment.kind === 'video' && !this.data.videoAvailable) throw new Error('当前服务还没有配置视频语音识别。');
      const payload = {
        text,
        recipient: DEFAULT_RECIPIENT,
        tone: DEFAULT_TONE,
        mode: this.data.mode,
        variant: this.data.result ? (this.data.variant + 1) % 3 : this.data.variant
      };
      if (this.data.attachment) {
        // shortcut: reuse the existing JSON API; switch to streamed upload before raising the 20 MB limit.
        payload.attachment = {
          kind: this.data.attachment.kind,
          name: this.data.attachment.name,
          mime: this.data.attachment.mime,
          data: await readBase64(this.data.attachment.path)
        };
      }
      const expectedEvidence = this.data.attachment && this.data.attachment.kind
        || this.data.mode === 'live' && /https:\/\/mp\.weixin\.qq\.com\/s(?:[/?#\s]|$)/i.test(text) && 'article';
      const result = validateResult(await this.request('/api/reply', { method: 'POST', data: payload }), expectedEvidence);
      const evidence = result.evidence;
      const evidenceText = evidence ? [evidence.visual_summary, evidence.visible_text, evidence.transcript, evidence.coverage].filter(Boolean).join('\n\n') : '';
      this.setData({ result, replyText: result.reply, variant: payload.variant, evidenceText, showEvidence: false });
      wx.pageScrollTo({ selector: '#result', duration: 220 });
    } catch (error) {
      this.setData({ result: previous, error: error.message });
    } finally {
      this.setData({ busy: false });
    }
  },

  onReplyInput(event) { this.setData({ replyText: event.detail.value, error: '' }); },
  toggleAnalysis() { this.setData({ showAnalysis: !this.data.showAnalysis }); },
  toggleEvidence() { this.setData({ showEvidence: !this.data.showEvidence }); },

  copyReply() {
    const text = this.data.replyText.trim();
    if (!text) return this.setData({ error: '回复是空的，请先编辑。' });
    wx.setClipboardData({
      data: text,
      success: () => {
        if (typeof wx.exitMiniProgram === 'function') {
          wx.exitMiniProgram({
            fail: () => wx.showToast({ title: '已复制，可返回聊天粘贴', icon: 'none' })
          });
          return;
        }
        wx.showToast({ title: '已复制，可返回聊天粘贴', icon: 'none' });
      },
      fail: () => this.setData({ error: '复制失败，请长按回复文字复制。' })
    });
  }
});
