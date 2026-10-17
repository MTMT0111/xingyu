const { videoMaterialFromLaunch } = require('./utils');

App({
  globalData: { pendingVideo: null },

  onShow(options) {
    this.globalData.pendingVideo = videoMaterialFromLaunch(options);
  }
});
