# 言外 Android 客户端

当前用户已确认个人主体不能创建微信移动应用，AppID 申请路线暂缓。以下 SDK 配置步骤仅供后续具备资格时使用；当前使用 Android 通用系统分享和复制闭环。系统分享收发不需要微信开放平台 AppID。

加载你真实部署的 HTTPS 言外页面；Debug 开发版还支持通过 USB 连接电脑上的本地服务。Android 自定义回复键盘可在微信当前输入框内启动分析，并把确认后的回复插入输入框，不需要微信移动应用 AppID。服务地址和可选移动应用 AppID 在原生设置中填写；没有 AppID 时，官方 OpenSDK 选人能力保持禁用。

## 像插件一样调用

- 在微信或其他应用点系统**分享**，选择“言外”，通过 `ACTION_SEND` 导入一段纯文字、一个 JPG/PNG/WebP 图片或一个 MP4/WebM/MOV 视频；只接收单个内容项。微信是否露出入口及提供什么内容取决于版本与手机 ROM，没有入口时仍可复制或保存媒体。
- 在支持 Android“处理文字”（`ACTION_PROCESS_TEXT`）的应用中长按选中文字，也可选择“言外”。
- 打开言外后可点原生工具栏“粘贴”；应用只在这次点击后读取剪贴板首项文本。
- 长按启动器中的言外图标，点静态快捷方式“分析剪贴板”；它只打开 App，仍需点顶部“粘贴”，支持的启动器也可将快捷方式拖到桌面。
- 在系统快捷设置编辑页手动加入“言外”磁贴；点击时应用生成并消费一次性令牌，只有合法磁贴入口会启动前台并读取一次剪贴板。
- 在言外设置里点“开启微信悬浮球”，阅读数据说明并勾选同意后，再到 Android 无障碍设置手动开启“言外微信悬浮球”。悬浮球只在微信前台出现；Android 11 以上单击时截取一次当前屏幕并读取微信暴露的可见文字，低版本只读取可见文字。进入言外后仍需确认才发给已配置服务；最多读取 6000 字，跳过密码字段。它不持续录屏。
- 在 App 设置点“启用回复键盘”，在 Android 输入法设置中启用“言外 · 回复键盘”。回到微信输入框切换到该键盘：复制对方消息后点“分析剪贴板”，生成并点“复制返回”，再点“插入回复”写入当前输入框。
- 外部内容先显示确认；已有草稿不会被静默覆盖。从系统分享或快捷入口进入时，一次确认后会自动调用模型并生成回复，确认前不会上传。普通页面输入仍由用户主动点生成。
- 图片上限 4 MiB，视频上限 20 MiB / 60 秒，文字上限 6000 个 Unicode 字符。媒体只接受临时 `content://` 授权，以流方式读取；不申请联系人或存储权限，不复制到公共目录。
- Android WebView 使用 `?host=android` 紧凑页，隐藏装饰区并突出输入、生成与回复。
- 生成后可点原生工具栏“发送”，通过 `ACTION_SEND text/plain` 打开系统分享面板；也可点“复制返回”，由原生层写入剪贴板并结束当前页面。系统分享面板是否显示微信、能进入哪些微信场景由 Android、微信版本和手机厂商决定。
- 悬浮球使用系统 AccessibilityService overlay，不申请传统“显示在其他应用上层”权限。服务只用前台窗口切换事件显示/隐藏球，不在后台读取聊天节点；内容只在用户单击球时采集一次。服务由 Android 托管并在用户启用期间驻留后台；关闭球请在言外设置或 Android 无障碍设置中关闭服务。应用不监听剪贴板或自启。快捷设置的 `TileService` 由系统按需绑定，点击后只负责启动前台流程。

微信页面文字能否读取取决于其可访问节点；Android 11 以上的单次截图只表示点击那一刻的可见画面，不能代替完整视频。视频音频、隐藏消息、好友列表和微信内部搜索数据不会被读取；需要总结小视频时，继续使用微信系统分享、保存到相册后分享，或小程序的视频素材入口。Android AccessibilityService 属敏感能力，当前用于本地/比赛演示；不应把此构建直接视为 Google Play 可发布版本，发布前需重新评估平台政策与审核要求。

若要实现页面内的“直接去微信选联系人”，仍需有资格的企业等主体在微信开放平台创建并审核移动应用，登记包名与签名后取得 AppID。回复键盘、系统分享进入言外、页面分析、复制回复和返回原应用均不需要该接口。

## USB 本地演示（优先使用真机）

不需要第三方 Android Studio 插件，也不需要模拟器镜像。电脑负责运行 Node 服务并连接 LLM；手机只运行轻量 WebView，不加载本地大模型。Android Studio 如已安装，直接打开本目录、选择 USB 设备并运行 Debug 即可；也可以只用已有 Android SDK 命令行工具。

1. 按仓库根目录 README 启动本地服务，确认电脑能打开 `http://127.0.0.1:4173/`。
2. Android 手机开启开发者选项和 USB 调试，用数据线连接电脑，解锁并接受 RSA 授权。运行 `adb devices -l`，目标设备必须显示 `device`；空列表通常需检查数据线/驱动，`unauthorized` 需要手机确认授权。
3. 在本目录执行下列命令（`adb` 位于 Android SDK 的 `platform-tools`；多台设备时在每个命令中添加 `-s 实际设备序列号`）：

```powershell
adb reverse tcp:4173 tcp:4173
adb install -r ./app/build/outputs/apk/debug/app-debug.apk
adb shell am start -n com.xingyu.reply/.MainActivity
```

4. 在 App 设置里点 **USB 本地调试**。服务根地址固定为 `http://127.0.0.1:4173/`，无需填写 AppID。手机中的 127.0.0.1 会经 ADB 转发到这台电脑，LLM 密钥保留在电脑的服务端环境中。
5. 依次验证系统分享、`PROCESS_TEXT`、原生“粘贴”、长按图标的“分析剪贴板”、快捷设置磁贴、“复制返回”和原生“发送”。图片选择、中文键盘与粘贴仍应在目标实体设备上验收。
6. 演示结束运行 `adb reverse --remove tcp:4173`，拔线并按需关闭 USB 调试。重新连接手机后需再次设置转发。

Debug 只允许这一个 HTTP origin；不支持任意局域网 HTTP 地址。Release 仍仅允许 HTTPS，且不包含 Debug 网络配置或 USB 快捷按钮。调试版用于开发演示，不作为正式发行包。没有连接设备时，APK 构建成功不等于完成了手机操作验收。

### 2026-10-14 MEmu 运行结果（悬浮球加入前）

环境为 Android 9 / MEmu / WebView 68，测试包为加入悬浮球前的版本。`ACTION_SEND` 文字一次确认后真实调用 DeepSeek 并自动生成；原生“复制返回”成功写入剪贴板并结束页面；快捷设置磁贴读回同一句、打开确认卡并消费一次性令牌。随后无令牌 `QUICK_REPLY` 重放没有读取剪贴板。静态快捷方式 `clipboard_reply` 已注册且不带令牌，仍需点“粘贴”。此前原生“发送”已打开 Android `ChooserActivity`，但模拟器没有可接收的 App。

从文件管理器真实分享 `parent-message.mp4`（11.4 秒、约 0.16 MB）进入 App 后，一次确认并在约 30 秒内完成真实画面、语音分析和回复生成。系统分享、`ACTION_PROCESS_TEXT`、图片/视频入口仍工作。静态快捷方式本次只验证系统注册；实体手机、真实微信、微信 AppID 和 Android Studio AVD 仍未验证。

### 2026-10-17 文字版悬浮球运行结果

在同一 MEmu Android 9 / WebView 68 环境安装当时的文字版 APK 与官方微信 8.0.79，通过应用说明和 Android 系统确认手动启用无障碍服务。悬浮球仅在微信前台出现；点击“读取可见文字”读取登录页暴露的“登录 / 注册 / 语言”，言外先显示未上传确认卡，确认后真实 DeepSeek 生成回复。原生“复制返回”结束言外并回到微信，悬浮球再次出现。记录见 `wechat-bubble-test-results.json`。2026-10-18 新增的 Android 11+ 单次截屏入口已通过编译、单测和 lint，尚未在实体手机或模拟器执行。

## 构建

Android Studio 可直接打开此目录。命令行需要 Android SDK 35、Build Tools 35.0.0、JDK 17 或兼容 JDK，以及 Gradle 8.10.2。项目固定 AGP 8.8.2、AndroidX WebKit 1.17.1、微信 SDK 6.8.40；最低 Android 7.0 / API 24。

```powershell
./gradlew.bat --no-daemon --max-workers=1 testDebugUnitTest testReleaseUnitTest assembleDebug lintDebug lintRelease
./gradlew.bat signingReport
```

项目已包含 Gradle 8.10.2 wrapper；macOS/Linux 使用 `./gradlew`。通过 `ANDROID_HOME` 或仅本机 `local.properties` 的 `sdk.dir` 指定 SDK。Debug APK 位于 `app/build/outputs/apk/debug/app-debug.apk`。Release 默认不配置签名，需要开发者自己的 keystore；release 禁止 WebView 调试。不要上传签名私钥。

Gradle 默认使用 1 GB 最大堆和一个 worker，不开启并行构建；命令行使用 `--no-daemon` 在构建结束后释放构建进程。当前 Debug 与 Release 各有 16 个 JUnit 用例，覆盖配置、一次性快捷令牌、分享文本、媒体类型/大小、文件名边界、微信可见文字过滤及截屏的一次性内存令牌。当前 APK 已构建并通过两种变体的单测和 lint；上方模拟器记录只覆盖此前的文字版悬浮球。

## 真实配置与验收

1. USB 本地演示按上文使用固定 Debug 回环地址；脱离 USB 时再部署有效 HTTPS 服务。服务地址必须是根地址，无账号、查询参数、片段或子路径；支持 ASCII 域名和 IPv4，以及明确端口。暂不支持 IPv6/Unicode 域名直接输入，可使用域名的 ASCII/Punycode 形式。
2. 安装 APK，设置服务地址。若服务启用 HTTP Basic 试用口令，客户端会弹出原生输入框，使用用户名 `trial`。口令不写入设置、不打印日志；Android WebView 可能在本进程认证会话中缓存凭据，应用不请求持久保存。
3. 当前个人主体不能创建微信移动应用，系统分享接收、复制与返回来源均跳过 AppID 配置。只有后续具备合格主体资格并需要 SDK 直接选人时，才申请并审核移动应用，登记默认包名 `com.xingyu.reply` 和**实际安装 APK** 的 MD5 签名；Debug / Release 签名不同，变更需同步平台，配置真实 AppID，无需 AppSecret。
4. 优先在微信输入框切换到回复键盘完成“分析剪贴板 → 复制返回 → 插入回复”；也可从来源 App 分享或选中文字进入言外，或使用显式粘贴、启动器快捷方式和快捷设置磁贴。客户端不读取通讯录、聊天记录，不选择联系人，不自动点发送。

回复键盘是 Android 系统输入法服务，因此首次启用时系统会显示输入法隐私提示。当前实现没有字母键、不记录按键，也不读取输入框已有内容；“分析剪贴板”和“插入回复”都必须由用户点击。需要正常打字时点“切换键盘”回到原输入法。
5. 手动检查首次 HTTP Basic 登录、错误口令重试、`?host=android` 紧凑页、所有插件入口、原生系统分享回复、图片/视频文件选择、离线重试，以及从设置/微信返回后草稿保留。切换服务地址会重载；同地址修改 AppID、原生返回键和打开设置复用原 WebView。系统终止进程后不保证未提交草稿恢复。

暂无真实移动应用 AppID、匹配签名和实体手机环境，不能宣称已通过微信真机分享验收。当前模拟器只验证官方微信登录页，没有登录账号，也没有覆盖聊天消息、视频号、好友选择和发送。单元测试与模拟器运行不能替代目标设备验收。

## 网页桥接

仅配置的精确 origin 的主页面可使用 `window.XingyuWeChat`（HTTPS，或 Debug 的固定 USB loopback 地址）。页面监听该对象的 `message` 事件，再提交 JSON 字符串：

```js
window.XingyuWeChat.addEventListener('message', event => {
  const result = JSON.parse(event.data);
  // 按 result.id 关联请求；handoff 表示请求已交给微信，不能显示“已送达”。
});
window.XingyuWeChat.postMessage(JSON.stringify({ type: 'capabilities', id: 'check-1' }));
window.XingyuWeChat.postMessage(JSON.stringify({ type: 'shareText', id: 'reply-1', text: '妈，我晚点给你打电话。' }));
```

原生分享入口在受信页面加载后派发固定事件。`entry` 为 `share` 或 `process_text`；附件 URL 是当前服务 origin 下的随机、无缓存临时流：

```js
window.addEventListener('xingyu-inbound-share', async event => {
  // { schemaVersion: 1, type: 'inboundShare', id, entry, text?, attachment?: { url, name, mime, size, kind } }
  const share = event.detail;
  // 用户确认并完成附件 fetch 后再确认；放弃时把 accepted 改为 dismissed。
  window.XingyuWeChat?.postMessage(JSON.stringify({ type: 'ackInboundShare', id: share.id, action: 'accepted' }));
  // 用户复制完成并主动要求返回来源应用时再调用。
  window.XingyuWeChat?.postMessage(JSON.stringify({ type: 'finishShareEntry', id: share.id }));
});
```

`ackInboundShare.action` 只接受 `accepted` 或 `dismissed`，并且只认当前分享 ID。`finishShareEntry` 仅在本次 Activity 确由分享/处理文字入口打开、且 ID 匹配时返回来源应用；普通启动时忽略。WebMessage bridge 不可用时文字事件仍能导入，但页面无法发送确认或主动结束 Activity，用户可使用系统返回键；原生文字在事件交付后清理，媒体在页面读取完后清理，未读取媒体最多在当前进程保留五分钟。

能力结果含 `available`、`platform: 'android'` 与固定 `reason`：`ready`、`missing_app_id`、`wechat_not_installed`、`invalid_configuration`、`sdk_unavailable`。若系统 WebView 不支持 WebMessageListener，则不注入桥接，网页应使用已有复制方式。

分享先返回 `shareText / handoff` 或 `shareText / error`。SDK 回调可能追加 `shareResult`：`completed`、`cancelled`、`error`。`completed` 只对应官方 SDK 的成功回调，**不是收件人已收到或已读**。无法得到微信联系人身份。微信版本/进程生命周期可能导致无回调；120 秒后报告 `result_unknown`，不能据此认定未发送，用户可自行查看微信。此时客户端也不会自动重试。

文本完整保持用户编辑内容，UTF-8 最大 10,240 字节，拒绝纯空白、NUL 和无效 Unicode。回调只接受本次进程中两分钟以内的匹配随机交易 ID。设置更改、文档导航或进程销毁后旧交易失效。

## 安全边界

桥接同时检查官方 origin allowlist、主 frame、消息来源与当前 WebView origin。未采用 `addJavascriptInterface`。Release 只加载同源 HTTPS；Debug 额外放行 `http://127.0.0.1:4173`，网络配置只给 `127.0.0.1` 开启明文，应用层继续限制端口与来源。两种变体都阻止外部导航和弹窗，不绕过证书校验；文件选择使用系统文档选择器，只授权用户选中的文件。普通入口只申请联网权限，不请求联系人、存储或传统悬浮窗权限；实验悬浮球是用户单独同意并在系统设置中开启的 AccessibilityService。剪贴板只在用户点“粘贴”，或快捷设置磁贴携带有效一次性令牌时读取一次，不做后台监听；启动器静态快捷方式只打开 App。磁贴服务由系统按需绑定。入站媒体实际读取到非空且不超过上限后才交给页面，不调用持久 URI 授权；桥接可用时确认或放弃会立即清理引用。进程被系统回收后不能保证临时 URI 仍可读取，客户端会明确要求重新分享。模型服务密钥留在服务器，客户端只保存非秘密服务地址和 AppID。

配置的整个 origin 视为可信应用代码；应仅填写你控制并保持安全的言外域名。桥接不替代服务端 XSS 防护。WebView 页面上的同源脚本可请求打开微信，最终联系人选择与发送确认由微信完成。

网页进程崩溃或被回收时，客户端移除并销毁失效 WebView，取消待处理桥接和文件选择，显示草稿可能丢失的重试页；用户点击重试后才新建页面。备份与设备迁移均排除应用数据。JavaScript 是现有网页功能的必要能力；Android 13 返回手势属性由较旧系统忽略，旧系统仍使用原生返回回调，这两项 lint 提示为有意配置。

## 官方依据

- [Android 接收系统分享](https://developer.android.com/training/sharing/receive)：按 action 与 MIME 声明接收范围，并让用户确认内容。
- [ACTION_PROCESS_TEXT](https://developer.android.com/reference/android/content/Intent#ACTION_PROCESS_TEXT)：系统选中文字处理入口。
- [WebView 访问本地开发服务器](https://developer.android.com/develop/ui/views/layout/webapps/access-local-server)：ADB reverse 支持 USB 真机和模拟器，无需公网服务。
- [Android 真机调试](https://developer.android.com/studio/run/device)：USB 调试与设备授权。
- [Android 网络安全配置](https://developer.android.com/privacy-and-security/security-config)：按域名控制明文连接。
- [微信 Android 接入指南](https://developers.weixin.qq.com/doc/oplatform/Mobile_App/Access_Guide/Android.html)：AppID、注册、包可见性、回调 Activity。
- [微信 Android 分享与收藏](https://developers.weixin.qq.com/doc/oplatform/Mobile_App/Share_and_Favorites/Android.html)：`WXTextObject`、会话场景及 10 KB 限制。
- [微信 Android FAQ](https://developers.weixin.qq.com/doc/oplatform/Mobile_App/FAQ/Android.html)：审核、包名及应用签名一致性；`sendReq` 接受不等于完成发送。
- [微信 SDK Maven 元数据](https://repo.maven.apache.org/maven2/com/tencent/mm/opensdk/wechat-sdk-android/maven-metadata.xml)：固定实际发布的 6.8.40。
- [Android 安全 JavaScript 桥接](https://developer.android.com/develop/ui/views/layout/webapps/native-api-access-jsbridge)：`WebViewCompat.addWebMessageListener` 与允许来源。
- [AndroidX WebKit 发布说明](https://developer.android.com/jetpack/androidx/releases/webkit)：WebKit 1.17.1。
