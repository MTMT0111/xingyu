# 星语 Android 客户端

当前用户已确认个人主体不能创建微信移动应用，AppID 申请路线暂缓。以下 SDK 配置步骤仅供后续具备资格时使用；当前先使用页面复制回微信。这个客户端尚未实现接收微信“分享给其他应用”的内容。

加载你真实部署的 HTTPS 星语页面；Debug 开发版还支持通过 USB 连接电脑上的本地服务。服务地址和移动应用 AppID 在原生设置中填写；没有 AppID 时保留网页分析、编辑和复制，微信能力返回 `missing_app_id`，不会伪装已接入。具备有效 AppID 后，才可通过微信官方 OpenSDK 将编辑后的纯文本交给微信选人发送。

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
5. 输入一条父母消息 → 查看分析 → 编辑回复 → 复制 → 切回微信粘贴。图片选择、中文键盘与粘贴应在设备上验收。仅在有效 HTTPS 或 loopback 安全上下文中依赖网页剪贴板；不同系统 WebView 版本仍需真机确认。
6. 演示结束运行 `adb reverse --remove tcp:4173`，拔线并按需关闭 USB 调试。重新连接手机后需再次设置转发。

Debug 只允许这一个 HTTP origin；不支持任意局域网 HTTP 地址。Release 仍仅允许 HTTPS，且不包含 Debug 网络配置或 USB 快捷按钮。调试版用于开发演示，不作为正式发行包。没有连接设备时，APK 构建成功不等于完成了手机操作验收。

## 构建

Android Studio 可直接打开此目录。命令行需要 Android SDK 35、Build Tools 35.0.0、JDK 17 或兼容 JDK，以及 Gradle 8.10.2。项目固定 AGP 8.8.2、AndroidX WebKit 1.17.1、微信 SDK 6.8.40；最低 Android 7.0 / API 24。

```powershell
./gradlew.bat --no-daemon --max-workers=1 testDebugUnitTest testReleaseUnitTest assembleDebug lintDebug lintRelease
./gradlew.bat signingReport
```

项目已包含 Gradle 8.10.2 wrapper；macOS/Linux 使用 `./gradlew`。通过 `ANDROID_HOME` 或仅本机 `local.properties` 的 `sdk.dir` 指定 SDK。Debug APK 位于 `app/build/outputs/apk/debug/app-debug.apk`。Release 默认不配置签名，需要开发者自己的 keystore；release 禁止 WebView 调试。不要上传签名私钥。

Gradle 默认使用 1 GB 最大堆和一个 worker，不开启并行构建；命令行使用 `--no-daemon` 在构建结束后释放构建进程。9 个 JUnit 用例覆盖 HTTPS、Debug loopback、正式版拒绝 HTTP、桥接同源、AppID 和分享文字边界；分别运行 Debug/Release 两个变体。尚未完成设备验收；构建和单元测试不能证明微信真实分享已接通。页面进程终止的恢复属于设备验收项。

## 真实配置与验收

1. 部署已有星语服务到有效 HTTPS 域名，在手机可正常访问。服务地址必须是根地址，无账号、查询参数、片段或子路径；支持 ASCII 域名和 IPv4，以及明确端口。暂不支持 IPv6/Unicode 域名直接输入，可使用域名的 ASCII/Punycode 形式。
2. 安装 APK，设置服务地址。若服务启用 HTTP Basic 试用口令，客户端会弹出原生输入框，使用用户名 `trial`。口令不写入设置、不打印日志；Android WebView 可能在本进程认证会话中缓存凭据，应用不请求持久保存。
3. 在微信开放平台创建并审核移动应用。Android 包名默认 `com.xingyu.reply`；登记**实际安装 APK** 的 MD5 应用签名。Debug 和正式签名不同，更换包名/签名需同步开放平台配置。把真实 AppID 填入应用设置，无需 AppSecret。
4. 安装官方微信。生成并编辑一条回复，点击网页的微信按钮，确认微信显示选人及文字，手动选择联系人并确认发送；也应手动测试取消流程。客户端不读取通讯录、不选择联系人、不自动点发送。
5. 手动检查首次 HTTP Basic 登录、错误口令重试、图片/视频文件选择、离线重试，以及从设置/微信返回后草稿保留。切换服务地址会重载；同地址修改 AppID、原生返回键和打开设置复用原 WebView。系统终止进程后不保证未提交草稿恢复。

暂无真实 AppID、匹配签名和真机微信的环境，不能宣称已通过微信真机分享验收。单元测试只验证输入边界，APK 构建也不能替代真机验收。

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

能力结果含 `available`、`platform: 'android'` 与固定 `reason`：`ready`、`missing_app_id`、`wechat_not_installed`、`invalid_configuration`、`sdk_unavailable`。若系统 WebView 不支持 WebMessageListener，则不注入桥接，网页应使用已有复制方式。

分享先返回 `shareText / handoff` 或 `shareText / error`。SDK 回调可能追加 `shareResult`：`completed`、`cancelled`、`error`。`completed` 只对应官方 SDK 的成功回调，**不是收件人已收到或已读**。无法得到微信联系人身份。微信版本/进程生命周期可能导致无回调；120 秒后报告 `result_unknown`，不能据此认定未发送，用户可自行查看微信。此时客户端也不会自动重试。

文本完整保持用户编辑内容，UTF-8 最大 10,240 字节，拒绝纯空白、NUL 和无效 Unicode。回调只接受本次进程中两分钟以内的匹配随机交易 ID。设置更改、文档导航或进程销毁后旧交易失效。

## 安全边界

桥接同时检查官方 origin allowlist、主 frame、消息来源与当前 WebView origin。未采用 `addJavascriptInterface`。Release 只加载同源 HTTPS；Debug 额外放行 `http://127.0.0.1:4173`，网络配置只给 `127.0.0.1` 开启明文，应用层继续限制端口与来源。两种变体都阻止外部导航和弹窗，不绕过证书校验；文件选择使用系统文档选择器，只授权用户选中的文件，不请求联系人/存储权限。模型服务密钥留在服务器，客户端只保存非秘密服务地址和 AppID。

配置的整个 origin 视为可信应用代码；应仅填写你控制并保持安全的星语域名。桥接不替代服务端 XSS 防护。WebView 页面上的同源脚本可请求打开微信，最终联系人选择与发送确认由微信完成。

网页进程崩溃或被回收时，客户端移除并销毁失效 WebView，取消待处理桥接和文件选择，显示草稿可能丢失的重试页；用户点击重试后才新建页面。备份与设备迁移均排除应用数据。JavaScript 是现有网页功能的必要能力；Android 13 返回手势属性由较旧系统忽略，旧系统仍使用原生返回回调，这两项 lint 提示为有意配置。

## 官方依据

- [WebView 访问本地开发服务器](https://developer.android.com/develop/ui/views/layout/webapps/access-local-server)：ADB reverse 支持 USB 真机和模拟器，无需公网服务。
- [Android 真机调试](https://developer.android.com/studio/run/device)：USB 调试与设备授权。
- [Android 网络安全配置](https://developer.android.com/privacy-and-security/security-config)：按域名控制明文连接。
- [微信 Android 接入指南](https://developers.weixin.qq.com/doc/oplatform/Mobile_App/Access_Guide/Android.html)：AppID、注册、包可见性、回调 Activity。
- [微信 Android 分享与收藏](https://developers.weixin.qq.com/doc/oplatform/Mobile_App/Share_and_Favorites/Android.html)：`WXTextObject`、会话场景及 10 KB 限制。
- [微信 Android FAQ](https://developers.weixin.qq.com/doc/oplatform/Mobile_App/FAQ/Android.html)：审核、包名及应用签名一致性；`sendReq` 接受不等于完成发送。
- [微信 SDK Maven 元数据](https://repo.maven.apache.org/maven2/com/tencent/mm/opensdk/wechat-sdk-android/maven-metadata.xml)：固定实际发布的 6.8.40。
- [Android 安全 JavaScript 桥接](https://developer.android.com/develop/ui/views/layout/webapps/native-api-access-jsbridge)：`WebViewCompat.addWebMessageListener` 与允许来源。
- [AndroidX WebKit 发布说明](https://developer.android.com/jetpack/androidx/releases/webkit)：WebKit 1.17.1。
