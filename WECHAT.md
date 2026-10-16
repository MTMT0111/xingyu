# 好好回 · 微信 H5 邀请试用

当前单页支持消息、公众号正文、截图和上传小视频。DeepSeek 已在原开发电脑配置，LLM 可独立分析与生成；TypeSafe/Jev 仅作为可选分类器，尚未配置其密钥。本轮验收状态见 `QA.md`。**没有部署公网域名，也没有完成微信真机验证**。2026-10-16 实际启动免费 Quick Tunnel 时，官方 API 在域名申请阶段超时，进程已自动清理，当前无公网地址。安卓本地演示改用 USB 转发，不需要隧道，见 [本地部署与安卓演示](docs/本地部署与安卓演示.md)。下面是微信内 HTTPS 邀请试用的配置步骤，不是已接入微信聊天系统的记录。

## 最小可用路径

微信打开 HTTPS 页面 → 粘贴消息/文章链接，或保存截图、小视频后选文件 → 选择回复对象 → 查看内容摘要、可能的理解和回复 → 编辑、复制 → 回微信粘贴发送。全部操作在同一页完成。未配置模型时仅支持文字模板；配置齐全后默认真实 AI。媒体识别还需 `MEDIA.md` 中的视觉与本地运行环境。

页面和 `/api/reply` 放在同一个 HTTPS 来源，密钥只保存在服务器 `.env` 中。普通表单、分析和复制不依赖公众号登录；目前没有读取微信聊天记录、代发消息或抓取抖音视频的权限或实现。复制权限不可用时，页面会选中文字供用户长按复制。

## 本次做好的部署开关

| 设置 | 默认与用途 |
| --- | --- |
| `PUBLIC_ORIGIN` | 空值：仅接受本机 Host。邀请试用时填完整固定来源，例如 `https://reply.example.com`；不含路径、查询或账号。 |
| `TRIAL_PASSWORD` | 公网模式必填，16–128 位无空格 ASCII 字符；使用随机口令。HTTP Basic 用户名固定为 `trial`。整站含静态页面及 API 都要求认证。 |
| `BIND_HOST` | 默认 `127.0.0.1`，供同机 HTTPS 反向代理访问。仅隔离容器网络才设 `0.0.0.0`；此时仍要求配置公网来源与试用口令。 |
| `PORT` | 默认 `4173`，Node 的内部 HTTP 端口。 |

保留 `.env.example` 中的 LLM 配置（Jev 可选），另加：

```dotenv
PUBLIC_ORIGIN=https://reply.example.com
TRIAL_PASSWORD=在此填写随机生成的私有试用口令
BIND_HOST=127.0.0.1
PORT=4173
```

上面的中文口令是说明，不是可用值；可自行在本机生成一个随机口令：

```sh
node -e "console.log(require('node:crypto').randomBytes(24).toString('base64url'))"
```

私下向受邀测试者提供用户名和口令，不要把它们放进 URL、前端源码或截图。公网模式缺少有效口令会直接拒绝启动；本机体验仍不需要口令。

## HTTPS 反向代理的边界

Node 自身不终止 TLS。选择已有 HTTPS 主机，将外部 443 转发到同机 `127.0.0.1:4173`。代理必须保留固定公网 Host；不要把 Node 端口直接开放到互联网。以下仅是已有 Nginx/TLS 环境的配置片段，域名和证书路径需由部署者替换：

```nginx
server {
    listen 443 ssl;
    server_name reply.example.com;
    ssl_certificate /etc/ssl/xingyu/fullchain.pem;
    ssl_certificate_key /etc/ssl/xingyu/privkey.pem;
    client_max_body_size 30m;

    location / {
        proxy_pass http://127.0.0.1:4173;
        proxy_set_header Host reply.example.com;
        proxy_read_timeout 190s;
    }
}
```

应用只接受 `PUBLIC_ORIGIN` 的 Host 与 Origin；不信任 `X-Forwarded-Host`、`X-Forwarded-Proto` 等客户端可伪造的标头。不提供通配 CORS。来源检查不能替代身份认证，所以公网模式另有试用口令。

公网真实 AI 每个进程每小时最多接受 **30 次生成尝试**，失败也计数，最多 **2 个并发生成**。耗尽小时额度返回 429 和 `Retry-After`；并发已满返回 429，本地模板仍可用。这是邀请试用额度：重启会重置，不跨实例共享。模型提供商还应设置余额/消费上限；正式公开或多实例部署前，需要独立用户身份和持久限额。应用不记录消息正文；部署者仍需检查反向代理及模型提供商的日志设置。

## 上线前必须实际验收

1. 外部访问只有 HTTPS 入口；内部 HTTP 端口不可从公网连接。无口令返回 401，错误口令不能访问页面或 API。
2. iOS 微信、Android 微信分别打开链接，验证 HTTP Basic 登录是否可用。**目前没有真机验证这一点。** 若客户端不弹出登录框，先使用部署平台已有的私有访问网关；不要删除试用保护来绕过。
3. 从微信粘贴一段测试消息，选择家人，分别跑本地模式、真实模式；真实模式应显示 LLM 或 Jev + LLM 的实际来源，LLM 失败应显示错误，已配置 Jev 却失败时须披露降级，不冒充 Jev 成功。
4. 检查键盘、手机布局、长文、截图、有声小视频与弱网络重试。核对摘要、画面文字和语音转写；失败、超时或取消后应保留输入和改稿，取消应停止后台处理。分别验证点击复制与被拒绝时长按复制。
5. 回到微信粘贴，确认没有自动发送；切换收件人或原消息后，旧结果应失效。
6. 真实模型每次试用都可能计费。先用不含敏感信息的样例检查提供商后台用量和额度，再邀请他人。

这些步骤是验收清单，不是本次已执行的结果。当前自动测试通过本机 HTTP 模拟反向代理的 Host、HTTPS Origin 与 Basic 认证，不能替代 TLS 部署和手机验证。

## 生成后直接选择微信对象

用户已于 2026-10-10 确认当前个人主体不能创建微信移动应用，原生 SDK 直接选人所需 AppID 路线受资格阻碍，本阶段暂停。复制回微信是当前主路径。下面的网页系统分享不等于微信已接通。

Android 客户端已实现进入好好回的 `ACTION_SEND`（单段文字、单张 JPG/PNG/WebP、单个 MP4/WebM/MOV）和 `ACTION_PROCESS_TEXT` 选中文字入口。内容先显示确认条，草稿冲突须由用户选择保留或替换；主动点击生成才上传和分析，复制后可主动返回来源 App。它不需微信 AppID、额外插件、常驻服务或存储权限；微信是否露出入口依版本与 ROM，尚未真机验证。iOS Share Extension 和网页分享目标仍未实现。接收入口与网页向外分享回复是两个方向，具体见 `CLIENTS.md`。

当前 H5 提供“系统分享”入口，使用浏览器的系统文本分享能力。仅在浏览器支持当前文本分享时显示；系统面板提供微信选项时可尝试，但微信仍可能拒绝接收。分享数据只含编辑后的回复，不带页面链接、原消息、分析或附件。网页无法指定分享应用、读取联系人或验证消息是否送达。

不支持分享时保留复制路径：复制回复 → 返回原微信聊天 → 粘贴并确认发送。页面的“妈妈/爸爸/其他长辈”只用于调整称呼，不是微信联系人选择器。取消分享不会自动发送、复制或重新分享，草稿仍保留。

真机补充验收：在 iOS/Android 的实际浏览器和微信内各检查是否出现分享入口，系统菜单是否列出微信，选择联系人后的内容是否为最后编辑的纯文本，取消是否保留草稿。自动化只模拟系统分享 API，不代表微信转发成功。

依据：[W3C Web Share API](https://www.w3.org/TR/web-share/) 定义由用户选择系统提供的分享目标；网站不能预选某个应用或联系人，也不能把 Promise 完成当成送达回执。

6.0 已为“按钮 → 微信选人 → 纯文本回复”增加 Android 与 iOS 客户端工程，复用当前 H5：Android 使用 `WXTextObject` 与 `SendMessageToWX.Req`，场景为 `WXSceneSession`；iOS 使用 `SendMessageToWXReq.text`、`bText=YES`。原生 SDK 可用时显示“去微信选人”。Android 已实际编译开发 APK；iOS 当前仅源码和静态检查。用户尚无移动应用 AppID，平台登记、HTTPS 服务、iOS 签名和两端真机测试仍待完成。步骤见 CLIENTS.md；普通微信浏览器仍走上述系统分享或复制。参考 [Android 官方文字分享](https://developers.weixin.qq.com/doc/oplatform/Mobile_App/Share_and_Favorites/Android.html)、[iOS 官方文档](https://developers.weixin.qq.com/doc/oplatform/Mobile_App/Share_and_Favorites/iOS.html)。

仓库现已实现原生微信小程序 `clients/wechat-miniprogram`，可由用户从会话选择图片/视频、读取剪贴板或使用相册/拍摄，再调用同一后端分析。它仍不能自动读取聊天；`onShareAppMessage` 的 `title/path/imageUrl` 只转发小程序卡片，不能把它称为发送任意纯文本回复。回复仍由用户复制回聊天。依据为[腾讯官方类型定义](https://github.com/wechat-miniprogram/api-typings/blob/master/types/wx/lib.wx.page.d.ts#L193)。

## 什么时候需要微信官方接口

- 用户身份、账号同步：再评估[微信网页授权](https://developers.weixin.qq.com/doc/service/guide/h5/auth.html)。官方说明 `snsapi_base` 用于获取 OpenID，用户信息授权另有 scope；当前无需添加。
- 图片选择、扫码、定制分享：再评估[微信 JS-SDK](https://developers.weixin.qq.com/doc/service/guide/h5/jssdk.html)。需要符合接口权限、JS 接口安全域名、服务端签名及 `wx.config` / `ready` / `error` 流程。当前普通文本工作流没有加载 SDK。
- 小程序入口：已使用原生 WXML/WXSS/JavaScript 实现，不依赖 `web-view`；仍需小程序 AppID、HTTPS `request` 合法域名、隐私保护指引和真机提审，见 `clients/wechat-miniprogram/README.md`。

以上官方页面于 2026-10-09 查阅。当前没有注册账号、申请域名、购买服务或对外发布。
