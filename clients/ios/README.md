# 好好回 iOS 客户端源码

当前用户已确认个人主体不能创建微信移动应用，AppID 申请路线暂缓。以下 SDK 配置步骤仅供后续具备资格时使用；当前先使用网页复制回微信。此工程未实现接收外部分享的 iOS Share Extension。

最低 iOS 14。Swift UIKit + WKWebView 承载现有好好回页面，Objective-C 适配层只调用微信官方 OpenSDK 的纯文本会话分享。**当前交付是源码：在 Windows 完成静态检查，尚未用 Xcode 编译、签名或真机运行，没有可安装 IPA。没有微信 AppID 时不会冒充接入成功。**

## 已实现

- 未配置服务时显示简洁原生配置页；Debug 可保存服务 HTTPS 根地址、AppID 和 Universal Link 三项非密钥值。服务地址不接受用户名、密码、路径、查询或片段，可使用有效的自定义端口。Release 忽略用户保存值，只读取签名包的配置。
- 连接现有 HTTPS 后端，私有试用 HTTP Basic 原生弹窗默认用户名 `trial`；密码只用于非持久 WebKit 数据存储的一次会话，不存入 UserDefaults，不记录日志。
- 网页 `<input type=file>` 保持 WKWebView 系统选择器行为，支持用户主动选择照片或视频；隐私说明包含相册、拍摄与录音。实际文件格式和大小仍由网页及服务端校验。
- 分享只传入最后编辑的原样回复，不带原消息、分析、附件或网页 URL。微信自行提供选择联系人和确认发送的界面；客户端不读取联系人、不预选联系人、不代发。
- 返回应用、SDK回调、关闭设置页时保留原 WKWebView，不自动刷新草稿。同一服务只修改 AppID/Universal Link 也复用原页面和附件；只有更换服务来源才重新加载。操作系统终止页面进程时明确提示草稿可能丢失，用户手动重试；没有宣称跨进程草稿恢复。
- Bridge 只接受主 frame，且文档 URL、frame URL、frame securityOrigin 均与配置的 HTTPS 来源及有效端口匹配。外部导航、重定向和弹窗被拒绝；TLS 证书交给系统校验，没有 ATS 例外或跳过证书验证。

## 在 Mac 上生成工程与构建

需要当前 Xcode、XcodeGen 和 CocoaPods。此仓库不安装这些系统工具，不含 Pods、微信二进制、签名证书或密钥。进入本目录后：

```sh
cp Config/Local.xcconfig.example Config/Local.xcconfig
xcodegen generate
pod install
open Xingyu.xcworkspace
```

`project.yml` 生成标准 Xcode 工程，CocoaPods 锁定官方 `WechatOpenSDK-XCFramework` **2.0.9**（腾讯规格最低 iOS 12，本项目最低 iOS 14）。Debug/Release 自定义 xcconfig 已包含对应 Pods 配置。构建后应保留新生成的 `Podfile.lock`，不要在缺少实际 `pod install` 的情况下伪造锁文件。

在 Xcode 选择 **Xingyu** scheme、模拟器或自己的设备。首次只填服务 URL 即可检查网页；AppID 留空会明确不可用并保留复制。要使用微信必须选择已安装微信的真机并完成下方登记。当前默认 Bundle ID 为 `com.xingyu.reply`，它只是源码的应用标识，不表示已有登记或签名；正式签名前将 `project.yml` 的 Bundle ID 改为自己已登记的值，重新执行 `xcodegen generate` 和 `pod install`。

填充 `Config/Local.xcconfig`：

| 配置 | 真实值要求 |
| --- | --- |
| `XINGYU_APP_URL` | 已部署的好好回 HTTPS 根地址，与后端 PUBLIC_ORIGIN 对应 |
| `XINGYU_WECHAT_APP_ID` | 微信开放平台审核通过的 iOS 移动应用 AppID；留空时不分享 |
| `XINGYU_UNIVERSAL_LINK` | 平台登记完全相同的 HTTPS Universal Link，以 `/` 结尾 |
| `XINGYU_ASSOCIATED_DOMAIN` | `applinks:` 加 Universal Link 的真实域名，不含协议、路径、端口 |
| `DEVELOPMENT_TEAM` | 你自己的 Apple Team ID |

xcconfig 中 `//` 会开始注释，URL 应写成 `https:/$()/你的域名/`；构建展开后才是 `https://你的域名/`。不要把 API Key、微信 AppSecret、服务试用密码填在这里。URL Scheme 直接由真实 AppID 生成；Debug 修改 AppID 并不能改写已签名包的 Scheme 或 Associated Domains，若不匹配会保持不可用。发布版不会采用 Debug 的本地覆盖值。

空 AppID/Associated Domain 的工程只适合未接入状态的模拟器检查；上真机和发行前必须补全签名及关联域名配置。生成工程是可复现的输入，不代表当前环境已经证明可编译。

## Universal Links 与回调

在自己的 HTTPS 域名通过 `/.well-known/apple-app-site-association`（无扩展名、无重定向）提供 JSON。`Resources/apple-app-site-association.example` 中 `appID` 必须填真实 `AppleTeamID.BundleID`，`paths` 改成 Universal Link 对应前缀，如 `/wechat/*`。该文件应可公开读取，不能被好好回 HTTP Basic 口令保护；仅此静态关联文件公开，不要开放分析 API。

微信开放平台填写完全相同的 Universal Link，并确认 Apple 开发者签名启用了 Associated Domains。`Resources/Xingyu.entitlements` 从构建变量填入真实域名。AppDelegate 同时处理 URL Scheme 与 Universal Link 回调；项目没有 Scene manifest，保持 AppDelegate 生命周期。不要只改代码而漏掉签名、域名文件或平台登记。

## 网页桥接协议

网页发送对象：

```js
window.webkit.messageHandlers.xingyuWeChat.postMessage({
  type: 'capabilities', id: crypto.randomUUID()
});
window.webkit.messageHandlers.xingyuWeChat.postMessage({
  type: 'shareText', id: crypto.randomUUID(), text: editedReply
});
window.addEventListener('xingyu-wechat', event => {
  // event.detail 为原生回调对象；不要将交接成功当成送达回执。
});
```

请求 ID 限 8–100 位 ASCII 字母、数字、`_`、`-`；文本 trim 后不能空，不能含 NUL，原样 UTF-8 长度不超过 10240 字节。输出用 `callAsyncJavaScript` 的 arguments 字典传值，不拼接用户文本到可执行 JavaScript。

能力返回 `{type:'capabilities',id,available,reason,platform:'ios'}`；reason 为 `ready / missing_app_id / wechat_not_installed / invalid_configuration / sdk_unavailable`。分享由 SDK 接受时只返回 `{type:'shareText',id,status:'handoff'}`；失败为 `status:'error',code`。错误代码为 `invalid_text`、上述不可用原因、`busy` 或 `sdk_rejected`。

异步 `{type:'shareResult',id,status,code}` 的 status 为 `completed / cancelled / error`，code 为 `sdk_completed / user_cancelled / wechat_sdk_error / result_unknown`。`completed` 仅表示 SDK 回调成功，仍不能证明联系人已阅读或送达。iOS 官方 2.0.9 的 BaseReq/BaseResp 没有 transaction 字段，因此正常情况下只允许一个 pending 分享，并将回调交给原文档的当前请求。

120 秒未收到结果时，旧请求得到 `error/result_unknown`，pending 被释放。自首次超时起，这个进程永久忽略所有后续 onResp，以防迟到回调被错归到新请求；后续仍可分享，每次 SDK 接受后只报告 `handoff` 并立即释放 pending，由用户在微信确认。**异常后只报告已交接，重开应用恢复结果回调。** SDK 接受操作本身也有同样的超时上限，不会永久卡在 busy。

## 验证状态与下一步

本机可运行 `python scripts/validate-source.py` 检查 plist、空配置、SDK版本、纯文本路径与桥接安全约束；输出 `source-validation-results.json` 明确 `iosCompiled:false`、`xctestsExecuted:false`。这是静态检查，不能证明 Swift、Objective-C 或 UIKit 行为在 Xcode 中通过。

已写 `Tests/ClientPolicyTests.swift` 和 `Tests/ShareCorrelationTests.m`，覆盖 HTTPS 根地址、拒绝跨来源/子frame、UTF-8/NUL边界、请求ID、缺少AppID、分享超时释放、迟到回调永久丢弃和旧定时器不影响新请求。Mac 上运行 **Product → Test**；也可用 `xcodebuild -workspace Xingyu.xcworkspace -scheme Xingyu -destination 'platform=iOS Simulator,name=你已安装的模拟器名称' test`。当前未执行这些 XCTest。

真机验收必须覆盖：初次配置与错误配置、有效/无效/取消试用口令、证书错误、图片/视频选择及拒绝权限、微信未安装、AppID未配、取消分享与SDK错误、连续点击、返回应用后草稿仍在、只传编辑后的文字、Universal Link和Scheme回调、跨域跳转/iframe/popup无法调用bridge。没有 AppID、可控HTTPS域名、Mac/Xcode和真实签名时，不能宣称微信官方交接闭环已通过。

官方依据（2026-10-10核对）：[微信iOS接入](https://developers.weixin.qq.com/doc/oplatform/Mobile_App/Access_Guide/iOS.html)、[官方iOS SDK资源](https://developers.weixin.qq.com/doc/oplatform/Mobile_App/Downloads/iOS_Resource.html)、[2.0.9 CocoaPods规格](https://trunk.cocoapods.org/api/v1/pods/WechatOpenSDK-XCFramework/specs/latest)、[腾讯2.0.9 SDK包](https://dldir1.qq.com/WechatWebDev/opensdk/XCFramework/OpenSDK2.0.9.zip)、[Apple WKWebView](https://developer.apple.com/documentation/webkit/wkwebview)、[Apple认证挑战](https://developer.apple.com/documentation/foundation/handling-an-authentication-challenge)。实际查阅了腾讯包内 WXApi.h 与 WXApiObject.h 的注册、发送、回调和纯文本字段。
