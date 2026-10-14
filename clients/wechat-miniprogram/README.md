# 好好回 · 微信小程序

这是原生微信小程序客户端，不使用 `web-view`、第三方 UI 框架或额外插件。它复用仓库现有 `/api/config` 和 `/api/reply`：用户主动粘贴文字/公众号链接，或从微信聊天、相册选择图片和 60 秒内视频；确认后由服务端分析，结果可编辑并复制回聊天。

## 已实现

- `wx.getClipboardData`：只在用户点击“粘贴文字”后读取。
- `wx.chooseMessageFile`：从微信会话选择单张图片或单个视频。
- `wx.chooseMedia`：从相册选择或拍摄图片/视频。
- 图片 4 MB、视频 20 MB / 60 秒、文字 6000 字的客户端校验；服务端仍会再次校验真实内容。
- 妈妈、爸爸、其他长辈与三种回复方式；真实 LLM / 本地示例状态取自服务端。
- 内容摘要、可能理解、识别依据、可编辑回复和剪贴板复制。
- 服务地址和邀请试用口令设置；DeepSeek、Jev 等模型密钥始终只留在服务端。

小程序不会读取好友列表或聊天记录，不能自动接收一条聊天消息，也不能预选联系人或代替用户发送。转发小程序卡片和发送任意回复文字是两种能力；当前回复路径是“复制 → 返回原聊天 → 粘贴并确认发送”。

## 开发者工具本机运行

1. 在仓库根目录运行 `npm start`，确认 `http://127.0.0.1:4173/api/config` 可访问。
2. 安装并登录[微信开发者工具](https://developers.weixin.qq.com/miniprogram/dev/devtools/download.html)。当前电脑已安装腾讯签名的稳定版 `2.02.2609231`，位置为 `D:\CodexTools\WeixinDevTools`。
3. 导入目录 `clients/wechat-miniprogram`。仓库已配置本项目的小程序 AppID `wx44641c021b958fdd`；若复制为其他小程序，再替换 `project.config.json` 中的 AppID。
4. 当前 `project.config.json` 为本机联调关闭了合法域名检查，因此模拟器可连接 `http://127.0.0.1:4173`。发布前必须恢复合法域名检查，并使用已登记的 HTTPS 域名。
5. 文字流程可直接测试；“聊天文件”、相册、拍摄、隐私授权和复制需要在真实微信环境复核，开发者工具不能替代真机。

2026-10-15 本机实测：微信开发者工具 `open` 和 `preview` 均使用真实 AppID 成功，预览包 35.4 KB；仓库 52 项自动测试全部通过。真机连接本地服务仍需 HTTPS 地址和后台合法域名。

## 真机和发布前配置

移动应用 AppID 与小程序 AppID 是两套登记。个人主体不能创建移动应用，不等于不能注册小程序；实际可用类目和审核要求以小程序后台为准。

1. 在[微信公众平台](https://mp.weixin.qq.com/)登录当前小程序，确认“开发管理 / 开发设置”中的 AppID 与 `project.config.json` 一致。
2. 把现有 Node 服务部署到固定 HTTPS 根域名。服务器 `.env` 设置 `PUBLIC_ORIGIN`、`TRIAL_PASSWORD` 和 LLM 配置；不要把模型密钥或 AppSecret 写入小程序。
3. 在小程序后台“开发设置 / 服务器域名”把该 HTTPS 根域名加入 `request` 合法域名。真机不能使用 IP、`localhost` 或电脑的 `127.0.0.1`。
4. 在小程序后台填写《用户隐私保护指引》，如实声明剪贴板、会话文件、相册/相机以及把用户主动提交的文字/图片/视频发送到自有服务和模型提供商。项目已启用 `__usePrivacyCheck__`，未完成后台声明时相关接口会被微信拦截。
5. 小程序“设置”中填写同一 HTTPS 根地址和邀请试用口令。当前口令保存在该小程序的本地存储，便于测试；正式公开前应改为 `wx.login` 对应的服务端会话和用户级额度。
6. 在开发者工具打开合法域名检查，预览到 Android / iPhone 微信，逐项验证文字、公众号链接、单图、单视频、拒绝隐私授权、断网、复制与回原聊天。
7. 完成名称、头像、类目、隐私指引和体验成员配置后，在开发者工具上传版本，再由小程序后台提交审核。

## 当前外部依赖

| 项目 | 当前状态 |
| --- | --- |
| 小程序源码 | 已完成，纯原生组件和 API |
| DeepSeek 后端 | 原开发电脑已配置；密钥不在仓库 |
| 小程序 AppID | 已配置 `wx44641c021b958fdd`，开发者工具已验证 |
| 微信开发者工具 | 已安装 `2.02.2609231` 至 `D:\CodexTools\WeixinDevTools`；登录和 CLI 正常 |
| 公网 HTTPS 与合法域名 | 未部署、未登记 |
| 隐私保护指引 | 尚未在小程序后台填写 |
| 开发者工具 / 真机 / 提审 | 开发者工具导入与预览已通过；实体 Android/iPhone、上传和提审尚未执行 |

从聊天选择文件使用微信的 `wx.chooseMessageFile`；小程序网络请求需要后台登记的 HTTPS 合法域名。相关入口见[微信小程序 API 文档](https://developers.weixin.qq.com/miniprogram/dev/api/media/image/wx.chooseMessageFile.html)与[网络能力说明](https://developers.weixin.qq.com/miniprogram/dev/framework/ability/network.html)。
