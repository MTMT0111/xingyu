# 知意 · 微信客户端接入

Windows 悬浮助手、微信小程序、安卓与 iPhone 客户端复用同一后端。Windows 端使用系统 UI Automation 单次读取当前微信可见内容；小程序使用原生组件；Android 还支持系统分享、处理所选文字、显式粘贴、启动器静态快捷方式和快捷设置磁贴；AI 密钥留在服务器。“妈妈/爸爸/其他长辈”只是回复称呼，不是微信联系人选择器。

**当前账号限制：**用户于 2026-10-10 在开放平台确认个人主体不能创建移动应用，本阶段暂停 AppID 申请。直接在页面调微信选联系人仍需合格主体的移动应用 AppID，以及匹配包名/签名；下面 SDK 流程不是当前可用状态。Android 通用系统分享收发、分析、复制和返回来源应用均不需要微信 AppID。

## 微信小程序入口

`clients/wechat-miniprogram` 是不依赖 `web-view` 的原生小程序。它声明 `video/*` 聊天素材入口，接收微信主动转交的视频（场景 `1173`），也通过 `wx.chooseMessageFile` 选择会话文件、`wx.chooseMedia` 访问相册/拍摄、用户点击读取剪贴板；随后调用现有 `/api/config` 和 `/api/reply`。结果只能由用户编辑并复制回聊天，不读取联系人、不自动收消息、不代发。聊天素材入口已写入代码并有自动测试，真实微信长按视频菜单仍需在当前体验版/真机验证。详细步骤见 [小程序 README](clients/wechat-miniprogram/README.md)。

个人主体无法创建移动应用不阻止注册小程序，但小程序可用类目和审核结果仍以微信公众平台实际账号为准。当前项目已配置小程序 AppID `wx44641c021b958fdd`，并在微信开发者工具完成正式导入与预览；体验版和真机仍待 HTTPS 服务就绪后验证。

## Android 轻量插件入口

- `ACTION_SEND`：接收一段纯文字、一张 JPG/PNG/WebP 图片或一段 MP4/WebM/MOV 视频；不接收多选内容。
- `ACTION_PROCESS_TEXT`：支持该入口的应用可将选中文字交给知意。微信是否露出分享/选中文字入口由其版本及 ROM 决定，不能保证所有聊天内容可直接导入。
- 原生“粘贴”：只在用户点击后读取剪贴板首项文本，并继续进入确认流程。
- 启动器静态快捷方式“分析剪贴板”：长按应用图标调用；支持的启动器可将它拖到桌面。
- 快捷设置磁贴“知意”：用户先在系统快捷设置编辑页手动添加；点击时生成并消费应用内部的一次性令牌，启动前台后只读取一次剪贴板。启动器静态快捷方式不带令牌，只打开 App，用户仍需点“粘贴”。
- Android 回复键盘：用户在系统输入法设置中启用后，可在微信当前输入框主动分析剪贴板，并用 `InputConnection.commitText` 插入确认后的回复。它不读取聊天记录、好友列表或输入框原文，不申请悬浮窗和无障碍权限。
- Android 微信悬浮球：用户在 App 内阅读数据说明并手动启用系统无障碍服务后，球只在微信前台显示。用户单击后，Android 11 及以上采集当前一帧屏幕并附上微信暴露的可见文字；Android 10 及以下只读取可见文字。密码字段跳过，文字最多 6000 字，截图最长边缩至 1920 像素且不超过 4 MiB。内容先进入知意确认页，确认后才上传分析。服务不监听聊天变化、不读取隐藏记录、不代发。
- 原生“发送”：从受信页面读取当前回复，用 `ACTION_SEND text/plain` 打开系统分享面板；没有微信 AppID 时仍可使用。
- Android 页面加载 `?host=android` 紧凑模式，隐藏装饰区，集中展示输入、分析和回复。
- 页面先显示确认条和文字/文件信息；从系统分享或快捷入口进入时，确认一次后自动上传并生成。普通页面输入仍由用户主动点生成。已有输入、附件或回复时，必须选择保留原草稿或改用新内容。
- 原生“复制返回”会把当前回复写入剪贴板并结束当前页面，使用系统返回栈恢复来源；不自动定位联系人、替换来源文字或发送消息。桥接不可用时使用系统返回键。

普通系统分享、选中文字和回复键盘入口不申请存储、联系人或传统悬浮窗权限，也不监听剪贴板。微信悬浮球是明确选择的例外：它使用系统 AccessibilityService overlay；启用后会收到前台窗口切换事件以控制悬浮球显隐，绝不在事件回调中采集内容。只有用户在微信前台单击球后，才执行一次可见文字读取；Android 11 及以上还会在隐藏悬浮球并再次确认微信仍在前台后截取当前一帧。单帧截图不能读取小视频声音、隐藏消息、好友列表或微信内部搜索数据；完整视频仍需用户分享或保存后导入。AccessibilityService 属高敏感系统权限；本构建用于本地/比赛演示，不应直接视为 Google Play 可发布版本。应用无自启，悬浮球服务由 Android 在用户启用期间托管；快捷设置的 `TileService` 由系统按需绑定，点击时只负责启动前台。媒体只读取本次授权的 `content://`，图片 4 MiB、视频 20 MiB/60 秒，文字 6000 个 Unicode 字符；来源撤回授权或进程被系统回收后可能需要重新分享。iOS 分享接收扩展仍未实现。

普通导入交互直接采用 Android 的成熟系统模式：[接收其他应用分享的数据](https://developer.android.com/develop/ui/compose/sharing/receive) 与 [`ACTION_PROCESS_TEXT`](https://developer.android.com/reference/android/content/Intent#ACTION_PROCESS_TEXT)。用户从原应用主动召唤、确认后才导入；微信悬浮球则是可单独关闭的高敏感实验入口。

## 已实现的流程

1. 在客户端配置知意服务的 HTTPS 根地址；Android Debug 可选“USB 本地调试”，通过 `adb reverse` 使用固定 `http://127.0.0.1:4173`。若 HTTPS 服务启用了邀请口令，会出现原生登录框；口令不作为明文配置保存。
2. 打开同一套单页界面，分析并编辑回复。
3. 微信 SDK 配置有效且安装了微信时，“去微信选人”可用；点击只把当前回复文本交给微信，再由用户选择联系人、确认。
4. 返回知意继续编辑。若从回复键盘进入，点“复制返回”后回到同一输入框，再点“插入回复”。取消、拒绝或未知结果不丢失当前草稿；页面不会声称消息已送达。

未配置微信时仍可打开已配置的知意服务、编辑、复制，并通过 Android 原生“发送”打开通用系统分享面板。“去微信选人”明确显示连接尚未配置，不用虚构 AppID。普通浏览器没有原生桥接时继续使用网页系统分享或复制。

## 客户端工程

| 平台 | 工程 | 构建方式 | 配置 |
| --- | --- | --- | --- |
| Windows | clients/windows | 系统 C# 编译器 + WinForms/UI Automation，无第三方依赖 | 本机服务默认 `http://127.0.0.1:4173`；远程服务只接受 HTTPS |
| 微信小程序 | clients/wechat-miniprogram | 微信原生 WXML/WXSS/JavaScript，无第三方插件 | 已配置小程序 AppID；发布还需 HTTPS `request` 合法域名和隐私指引 |
| Android | clients/android | Gradle，官方微信 SDK 6.8.40 | HTTPS 地址；Debug 另支持固定 USB 本地地址；AppID 仅 SDK 选人需要；包名 com.xingyu.reply |
| iOS | clients/ios | XcodeGen + CocoaPods，官方微信 SDK 2.0.9 | xcconfig、URL Scheme、Universal Links、Associated Domains 与 Apple 签名 |

Windows 使用步骤见 clients/windows/README.md；移动端步骤分别见 clients/android/README.md 和 clients/ios/README.md。安卓开发签名只供开发测试；正式版需使用自己持有的签名，并与微信开放平台登记的应用一致。iOS 必须在 Mac 上编译和签名，源码静态检查不是 iOS 编译通过。

仓库的 [Android 开发版 APK](downloads/xingyu-android-debug.apk) 已更新为带微信悬浮球与本机回复历史的 Debug 版：1,323,031 字节，SHA-256 `5b6b653061c656c96c1bdeccab035f351ec0d9dfbb13f353d22f872b78a98a97`，v2 签名校验通过；Debug/Release 各 16 个单元测试和 lint 通过。此前文字版已在 MEmu 的官方微信 8.0.79 登录页完成悬浮球闭环；当前 Android 11+ 单次截屏版仍待实体设备验收。

安装开发包后先填 HTTPS 根地址，或在 Android Debug 设置中选择“USB 本地调试”。USB 转发命令、Studio 内置设备镜像与演示步骤见 [本地部署与安卓演示](docs/本地部署与安卓演示.md)。当前没有可安装的 iPhone IPA，需在 Mac 上编译签名。

## 当前还需要的真实配置

用户已确认当前个人主体不能创建微信移动应用，因而缺少可用 AppID。只有后续具备合法主体资格时，才评估开放平台移动应用登记、Android 包名与签名、iOS Bundle ID 与 Universal Link；本阶段不继续要求申请。公众号/小程序 AppID 不能替代。

原开发电脑服务端已配置 DeepSeek，当前 LLM 可独立运行，Jev 密钥不再是生成前提；新克隆不含模型密钥。Android USB 演示无需公网：先运行电脑服务，再执行 `adb reverse tcp:4173 tcp:4173`，Debug 客户端才可使用固定回环地址。没有转发时不能把电脑的 127.0.0.1 当作手机地址。Release 与手机微信网页仍要求可访问 HTTPS；原环境免费隧道启动此前被自动审批拒绝，目前无公网地址，部署步骤见 WECHAT.md。

## 验证与限制

浏览器已经测试两种桥接消息格式、能力探测、精确文本、防重复、10 KiB UTF-8 上限、错误/取消/超时保稿、旧回调隔离及手机布局。浏览器中的 SDK 是模拟对象，不能证明微信真的收到消息。

客户端只允许配置的同源主页面使用原生接口，不向任意网页开放；外部导航、证书错误和来源不匹配均拒绝。Android 仅 Debug 为 USB 调试允许固定 HTTP 回环地址，Release 保持 HTTPS 限制，不放开任意 HTTP 或局域网地址。没有读取通讯录、指定好友 ID、后台发消息或自动确认发送的接口。

Android 使用 SDK transaction 关联回调。iOS SDK 响应没有 transaction：正常阶段只允许一个待返回请求；发生未知结果后停止归因迟到回调，后续仅报告 SDK 是否接受交接，避免把旧响应当成新分享结果。请以微信聊天中实际显示为准。

2026-10-11 已在 MEmu Android 9 通过 ADB 安装并验收旧版“`ACTION_SEND` / `ACTION_PROCESS_TEXT` → 确认导入 → 真实生成 → 编辑 → 复制 → 返回来源”闭环，运行记录见 `clients/android/usb-demo-test-results.json`。`native-build-results.json` 中的设备数量只是更早的构建时快照，不能用来判断后续运行时的设备连接状态。

2026-10-17 在同一 Android 9 / MEmu / WebView 68 环境安装当前包与官方微信 8.0.79：用户同意和系统授权流程通过；悬浮球只在微信前台显示；点击读取登录页暴露的“登录 / 注册 / 语言”，页面先显示未上传确认卡，确认后真实 DeepSeek 生成回复，“复制返回”结束知意并回到微信，悬浮球再次出现。运行记录见 `clients/android/wechat-bubble-test-results.json`。

实体 Android 手机、已登录微信聊天、视频号内容、Android Studio AVD 与微信 AppID 仍未验收；登录页实测不能证明聊天消息或视频可访问，也不能证明回复已发给联系人。SDK 选人验收等待合法主体及 AppID 条件满足。页面用 sessionStorage 暂存当前标签页文字草稿，刷新可恢复；附件文件不保存，操作系统清理、关闭 WebView 或更换服务地址后的恢复不作保证。

官方依据：[Android 接入与分享](https://developers.weixin.qq.com/doc/oplatform/Mobile_App/Share_and_Favorites/Android.html)、[iOS 接入与分享](https://developers.weixin.qq.com/doc/oplatform/Mobile_App/Share_and_Favorites/iOS.html)。
