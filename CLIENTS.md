# 星语 · 微信客户端接入

安卓与 iPhone 客户端复用这套网页和后端。Android 还支持系统分享接收与选中文字处理；AI 密钥留在服务器。网页的“妈妈/爸爸/其他长辈”只是回复称呼，真实收件人仍由用户在微信中选择。

**当前账号限制：**用户于 2026-10-10 在开放平台确认个人主体不能创建移动应用，本阶段暂停 AppID 申请。直接在页面调微信选联系人仍需合格主体的移动应用 AppID，以及匹配包名/签名；下面 SDK 流程不是当前可用状态。Android 系统分享接收、分析、复制和返回来源应用均不需要微信 AppID。

## Android 系统入口

- `ACTION_SEND`：接收一段纯文字、一张 JPG/PNG/WebP 图片或一段 MP4/WebM/MOV 视频；不接收多选内容。
- `ACTION_PROCESS_TEXT`：支持该入口的应用可将选中文字交给星语。微信是否露出分享/选中文字入口由其版本及 ROM 决定，不能保证所有聊天内容可直接导入。
- 页面先显示确认条和文字/文件信息；确认放入编辑区后才预览附件，主动点生成才上传和分析。已有输入、附件或回复时，必须选择保留原草稿或改用新内容。
- 复制回复后可主动“返回原应用”，使用系统返回栈恢复来源；不自动定位联系人、替换来源文字或发送消息。桥接不可用时使用系统返回键。

系统入口不安装额外插件，不申请存储/联系人权限，也没有常驻服务、悬浮窗、无障碍或剪贴板监听。媒体只读取本次授权的 `content://`，图片 4 MiB、视频 20 MiB/60 秒，文字 6000 个 Unicode 字符；来源撤回授权或进程被回收后可能需要重新分享。iOS 分享接收扩展仍未实现。

这套交互直接采用 Android 的成熟系统模式：[接收其他应用分享的数据](https://developer.android.com/develop/ui/compose/sharing/receive) 与 [`ACTION_PROCESS_TEXT`](https://developer.android.com/reference/android/content/Intent#ACTION_PROCESS_TEXT)。用户从原应用主动召唤、确认后才导入，完成后回到原任务栈；因此安装包更小，也不会要求敏感的悬浮窗或无障碍权限。

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

仓库的 [Android 开发版 APK](downloads/xingyu-android-debug.apk) 已更新为系统分享接收版本。本次 Debug / Release 均构建成功，各 12/12 项 JUnit 通过，lint 0 错误、2 条既有警告。Debug 包 1,319,223 字节，SHA-256 为 `46731c64d74d08782c28938f2f187fbd09c351936e16fa839655f3b4566299f9`；签名校验通过，证书 MD5 为 `47695d55708a06168886218f9e8ba954`。包名为 `com.xingyu.reply`，开发签名不作为正式发行签名；完整证据见 `native-build-results.json`。

安装上述新版开发包后先填 HTTPS 根地址，或在 Android Debug 设置中选择“USB 本地调试”。USB 转发命令、Studio 内置设备镜像与演示步骤见 [本地部署与安卓演示](docs/本地部署与安卓演示.md)。旧版 APK 没有 USB 入口；本轮构建状态见 `QA.md`。当前没有可安装的 iPhone IPA，需在 Mac 上编译签名。

## 当前还需要的真实配置

用户已确认当前个人主体不能创建微信移动应用，因而缺少可用 AppID。只有后续具备合法主体资格时，才评估开放平台移动应用登记、Android 包名与签名、iOS Bundle ID 与 Universal Link；本阶段不继续要求申请。公众号/小程序 AppID 不能替代。

原开发电脑服务端已配置 DeepSeek，当前 LLM 可独立运行，Jev 密钥不再是生成前提；新克隆不含模型密钥。Android USB 演示无需公网：先运行电脑服务，再执行 `adb reverse tcp:4173 tcp:4173`，Debug 客户端才可使用固定回环地址。没有转发时不能把电脑的 127.0.0.1 当作手机地址。Release 与手机微信网页仍要求可访问 HTTPS；原环境免费隧道启动此前被自动审批拒绝，目前无公网地址，部署步骤见 WECHAT.md。

## 验证与限制

浏览器已经测试两种桥接消息格式、能力探测、精确文本、防重复、10 KiB UTF-8 上限、错误/取消/超时保稿、旧回调隔离及手机布局。浏览器中的 SDK 是模拟对象，不能证明微信真的收到消息。

客户端只允许配置的同源主页面使用原生接口，不向任意网页开放；外部导航、证书错误和来源不匹配均拒绝。Android 仅 Debug 为 USB 调试允许固定 HTTP 回环地址，Release 保持 HTTPS 限制，不放开任意 HTTP 或局域网地址。没有读取通讯录、指定好友 ID、后台发消息或自动确认发送的接口。

Android 使用 SDK transaction 关联回调。iOS SDK 响应没有 transaction：正常阶段只允许一个待返回请求；发生未知结果后停止归因迟到回调，后续仅报告 SDK 是否接受交接，避免把旧响应当成新分享结果。请以微信聊天中实际显示为准。

当前优先真机验收“`ACTION_SEND` / `ACTION_PROCESS_TEXT` → 确认导入 → 真实生成 → 编辑 → 复制 → 返回来源粘贴”，并检查微信版本 / ROM 入口展示、临时 URI 授权与草稿冲突；SDK 选人验收等待合法主体及 AppID 条件满足。页面用 sessionStorage 暂存当前标签页文字草稿，刷新可恢复；附件文件不保存，操作系统清理、关闭 WebView 或更换服务地址后的恢复不作保证。ADB 设备仍为 0，尚未真机验证。

官方依据：[Android 接入与分享](https://developers.weixin.qq.com/doc/oplatform/Mobile_App/Share_and_Favorites/Android.html)、[iOS 接入与分享](https://developers.weixin.qq.com/doc/oplatform/Mobile_App/Share_and_Favorites/iOS.html)。
