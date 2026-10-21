# 本机微信视频号连接器补丁

上游：[nobiyou/wx_channel](https://github.com/nobiyou/wx_channel)，MIT 许可见同目录 LICENSE。
基线：`434bb4df364b9e9a69e10f42cfe94e63c5eee13d`（v5.7.13）。第三方二进制、编译工具、微信缓存和令牌不随本仓库提交。

本机安装原始发布包后，实际视频页未建立 API 连接。检查发现启用 `secret_token` 后，视频页脚本使用 `window.__WX_LOCAL_TOKEN__`，但上游没有赋值；此外代理仅按 `/api/` 路径匹配，会拦截微信浏览器中的其他网站。

`local-compatibility.patch` 做以下修改：

- 在视频页 API 脚本前安全注入本机令牌，保留鉴权。
- 只处理相关微信域名和本机接口，其他网站直接透传。
- 修改视频 HTML 后清除旧长度、编码和缓存响应头。
- 将控制端口绑定到 `127.0.0.1`。
- 添加令牌初始化顺序、HTML 转义及无关域名透传测试。

## 构建

取得上游固定提交，在上游目录执行 `git apply <本项目路径>/integrations/wx-channel/local-compatibility.patch`。
需要 Go 1.24.3 和 C/C++ 编译器，`CGO_ENABLED=1`。上游 Windows CI 使用 MinGW GCC；本机使用 Zig 0.13 的 `cc/c++`。

```sh
go test -tags=zombiezen_sqlite ./internal/handlers ./internal/router
go build -tags=zombiezen_sqlite -o wx_channel-local.exe .
```

本机配置使用 `config.yaml` 的 `secret_token` 与 `web_console_token`，设置相同随机令牌；本项目 `.env` 中的 `CHANNELS_BRIDGE_TOKEN` 必须匹配。不要把令牌放入文档、截图或提交。
`cloud_enabled`、`radar_enabled`、`auto_open_channels` 设为 `false`。

首次运行涉及安装 SunnyRoot 根证书与微信进程代理。本机用户已于 2026-10-20 明确授权，并亲自确认了 Windows 证书提示。其他设备应由设备所有者同意后安装。

本项目对真实视频链路的验证结果以 [微信链接读取](../../docs/微信链接读取.md) 为准；构建成功不代表可读取任意未打开的聊天卡片。
