# 星语 · 微信客户端接入

安卓与 iPhone 客户端复用这套网页和后端。客户端只负责打开服务、选择图片/视频、连接微信官方 SDK；AI 密钥留在服务器。网页的“妈妈/爸爸/其他长辈”仍是回复称呼，真实收件人在微信中选择。

**当前账号限制：**用户于 2026-10-10 在开放平台确认个人主体不能创建移动应用，本阶段暂停 AppID 申请路线。下面的 SDK 流程是有合格主体及有效 AppID 时的实现设计，不是当前可用状态。个人试用优先使用网页复制回微信；系统分享仅作可选尝试。当前也没有实现从微信直接分享内容进星语的接收扩展。

## 已实现的流程

1. 在客户端配置星语服务的 HTTPS 根地址；Android Debug 可选“USB 本地调试”，通过 `adb reverse` 使用固定 `http://127.0.0.1:4173`。若 HTTPS 服务启用了邀请口令，会出现原生登录框；口令不作为明文配置保存。
2. 打开同一套单页界面，分析并编辑回复。
3. 微信 SDK 配置有效且安装了微信时，“去微信选人”可用；点击只把当前回复文本交给微信，再由用户选择联系人、确认。
4. 返回星语继续编辑。取消、拒绝或未知结果不丢失当前草稿；页面不会声称消息已送达。

未配置微信时仍可打开已配置的星语服务、编辑和复制。“去微信选人”明确显示连接尚未配置，不用虚构 AppID。普通浏览器没有原生桥接时继续使用系统分享或复制。

## 客户端工程

| 平台 | 工程 | 构建方式 | 配置 |
| --- | --- | --- | --- |
| Android | clients/android | Gradle，官方微信 SDK 6.8.40 | HTTPS 地址；Debug 另支持固定 USB 本地地址；AppID 仅 SDK 选人需要；包名 com.xingyu.reply |
| iOS | clients/ios | XcodeGen + CocoaPods，官方微信 SDK 2.0.9 | xcconfig、URL Scheme、Universal Links、Associated Domains 与 Apple 签名 |

完整步骤分别见 clients/android/README.md 和 clients/ios/README.md。安卓开发签名只供开发测试；正式版需使用自己持有的签名，并与微信开放平台登记的应用一致。iOS 必须在 Mac 上编译和签名，源码静态检查不是 iOS 编译通过。

本仓库附带新版 [Android 开发版 APK](downloads/xingyu-android-debug.apk)。本轮 Debug / Release 均实际构建，各 9 项 JUnit 通过，lint 0 错误、2 条既有警告；开发包签名校验通过。开发包 SHA-256 为 `3d9acc3bd6f49fe36adf0f2e357d2e9009dfb45c567996326edc4f1d6296a3ac`，签名 MD5 为 `47695d55708a06168886218f9e8ba954`，包名为 `com.xingyu.reply`；这些不是正式发行签名。工具链及未验证项见 `native-build-results.json`。

安装上述新版开发包后先填 HTTPS 根地址，或在 Android Debug 设置中选择“USB 本地调试”。USB 转发命令、Studio 内置设备镜像与演示步骤见 [本地部署与安卓演示](docs/本地部署与安卓演示.md)。旧版 APK 没有 USB 入口；本轮构建状态见 `QA.md`。当前没有可安装的 iPhone IPA，需在 Mac 上编译签名。

## 当前还需要的真实配置

用户已确认当前个人主体不能创建微信移动应用，因而缺少可用 AppID。只有后续具备合法主体资格时，才评估开放平台移动应用登记、Android 包名与签名、iOS Bundle ID 与 Universal Link；本阶段不继续要求申请。公众号/小程序 AppID 不能替代。

原开发电脑服务端已配置 DeepSeek，当前 LLM 可独立运行，Jev 密钥不再是生成前提；新克隆不含模型密钥。Android USB 演示无需公网：先运行电脑服务，再执行 `adb reverse tcp:4173 tcp:4173`，Debug 客户端才可使用固定回环地址。没有转发时不能把电脑的 127.0.0.1 当作手机地址。Release 与手机微信网页仍要求可访问 HTTPS；原环境免费隧道启动此前被自动审批拒绝，目前无公网地址，部署步骤见 WECHAT.md。

## 验证与限制

浏览器已经测试两种桥接消息格式、能力探测、精确文本、防重复、10 KiB UTF-8 上限、错误/取消/超时保稿、旧回调隔离及手机布局。浏览器中的 SDK 是模拟对象，不能证明微信真的收到消息。

客户端只允许配置的同源主页面使用原生接口，不向任意网页开放；外部导航、证书错误和来源不匹配均拒绝。Android 仅 Debug 为 USB 调试允许固定 HTTP 回环地址，Release 保持 HTTPS 限制，不放开任意 HTTP 或局域网地址。没有读取通讯录、指定好友 ID、后台发消息或自动确认发送的接口。

Android 使用 SDK transaction 关联回调。iOS SDK 响应没有 transaction：正常阶段只允许一个待返回请求；发生未知结果后停止归因迟到回调，后续仅报告 SDK 是否接受交接，避免把旧响应当成新分享结果。请以微信聊天中实际显示为准。

当前优先验收“真实生成 → 编辑 → 复制 → 微信粘贴 → 返回保稿”；SDK 选人验收等待合法主体及 AppID 条件满足。页面用 sessionStorage 暂存当前标签页文字草稿，刷新可恢复；附件文件不保存，操作系统清理、关闭 WebView 或更换服务地址后的恢复不作保证。当前没有连接 ADB 设备，尚未真机验证。

官方依据：[Android 接入与分享](https://developers.weixin.qq.com/doc/oplatform/Mobile_App/Share_and_Favorites/Android.html)、[iOS 接入与分享](https://developers.weixin.qq.com/doc/oplatform/Mobile_App/Share_and_Favorites/iOS.html)。
