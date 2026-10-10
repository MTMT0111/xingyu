# Android 系统分享入口：设计

由 **Codex** 描述当前实现，待 Kiro 实际审阅；不是 Kiro 生成或实机运行记录。需求见 `requirements.md`。

## 现有组件的职责

| 文件 | 职责 |
| --- | --- |
| `clients/android/app/src/main/AndroidManifest.xml` | 为 MainActivity 声明单项 `ACTION_SEND` 与 `ACTION_PROCESS_TEXT` 的 MIME 范围 |
| `clients/android/app/src/main/java/com/xingyu/reply/InputRules.java` | 文字、MIME、媒体大小、文件名、服务来源与请求 ID 的边界 |
| `clients/android/app/src/main/java/com/xingyu/reply/MainActivity.java` | 处理新 Intent，读取临时授权 URI，按信任来源派发事件，确认后清理引用，主动返回来源 |
| `public/index.html`、`public/app.js`、`public/styles.css` | 入站确认条、草稿冲突、附件本地预览、手动生成与复制后返回入口 |
| `server.mjs` 与 `lib/` | 复用现有附件校验、媒体识别和 LLM 回复，不增加单独上传服务 |

## 数据流

系统 Intent → 原生校验单项输入 → 受信页面收到 `xingyu-inbound-share` → 展示确认条 → 用户接受后放入编辑区 → 用户点击生成 → 现有分析接口 → 编辑和复制 → 用户主动返回来源。

文字来自 `EXTRA_TEXT` 或 `EXTRA_PROCESS_TEXT`。媒体来自 `EXTRA_STREAM` / 单项 ClipData 的 `content://`；MIME 与流大小在后台线程检查，避免在 UI 线程读取整个文件。多项、类型冲突、超限或失效授权返回明确错误。

原生向当前配置 origin 的主页面派发 schemaVersion 1 事件，含随机分享 ID、`entry`（`share` / `process_text`）、文字及可选附件描述。附件 URL 是当前 origin 下随机令牌的临时路径，由 WebView 原生拦截并流式读取，不是上传到服务器；页面接受后才 fetch 用于本地预览。

## 草稿与原生确认

- 新内容先保持待确认；原文、附件、分析与已编辑回复仍保留。
- “继续原草稿 / 暂不使用”丢弃入站项；“改用新内容 / 放入编辑区”在附件读取成功后替换。读取失败时保留草稿和错误提示。
- 页面通过 `ackInboundShare` 携带当前 ID 与 `accepted` / `dismissed` 确认，原生释放本次附件引用。
- 复制后用户主动点击返回，通过 `finishShareEntry` 携带对应入口 ID 结束 Activity。普通桌面启动或错误 ID 不能触发该路径；不会发送消息或选择联系人。
- 页面 sessionStorage 继续只存当前标签页文字草稿，不缓存文件。原生进程被回收后临时授权/内容可能丢失，须重新分享。

## 信任与依赖

桥接限制精确 origin 与主 frame，禁止任意网页使用原生功能；输入按数据处理。Release 要求 HTTPS，Debug 的唯一 HTTP 例外是 USB 转发的回环地址。保持当前 Node / 原生 Android 技术栈，不加 SDK、插件、后台服务、存储权限或本地 LLM 权重。

系统分享入口与微信 SDK 分享是独立路径。前者不需 AppID；后者需要合法主体、移动应用 AppID 与正确包名/签名，目前受主体资格限制。LLM 直接分析仍可用，Jev 只在配置后参与，来源不能混淆。

## 验证边界

单元测试验证输入规则；浏览器测试模拟原生事件、确认、冲突、附件流、复制与返回消息，不能证明微信实际展示入口。APK 构建、lint 和回归须记录本轮结果；真机另验微信/其他来源应用、不同内容、临时授权、WebView 生命周期及来源返回。当前 ADB 无设备，这些实机项未完成。
