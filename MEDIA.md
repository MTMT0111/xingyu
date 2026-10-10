# 星语 · 文章、图片与小视频

## 输入范围

- 文字及公开公众号正文：最多 6000 字。只读取 HTTPS `mp.weixin.qq.com/s` 文章，不跟随重定向、不携带登录凭据；读取上限 8 秒、2 MiB。超长、验证码、删除或不可见正文均明确报错，不截断后冒充完整阅读。文章内图片和嵌入视频不会自动读取。
- 图片：一张 JPEG、PNG 或 WebP，最多 4 MiB。服务端检查实际文件头，不依赖扩展名或客户端 MIME。
- 视频：一个 MP4/MOV 或 WebM，最多 20 MiB、60 秒、4K；具体编码仍取决于解码器。实际提取 6 帧、完整音轨，再合并画面识别与语音转写。

选择附件只创建本地预览，点击生成才上传。所有媒体只走真实模式；识别失败不以模板替代。无可辨语音时明确披露；音轨处理失败则阻止生成。抽帧可能遗漏短暂画面，语音转写可能有同音字错误，结果中可展开核对。

## 已搭建的本地环境

本机已安装 faster-whisper 1.2.1、PyAV 16.1.0、CTranslate2 4.8.2，并下载 `Systran/faster-whisper-base` 多语言模型。Python、依赖和模型位于工作目录，实际路径已写入本机 `.env`，不包含在交付 ZIP 中。使用 CPU int8、4 线程；运行时禁止下载模型，无需额外语音云服务密钥。

新机器可在专用 Python 环境中安装：

```sh
python -m pip install -r scripts/requirements-media.txt
python -c "from huggingface_hub import snapshot_download; snapshot_download('Systran/faster-whisper-base', local_dir='./models/whisper-base', allow_patterns=['config.json','model.bin','tokenizer.json','vocabulary.txt'])"
```

在 `.env` 设置 `MEDIA_PYTHON` 为实际 Python 可执行文件绝对路径，`WHISPER_MODEL_PATH` 为包含 `model.bin` 的模型目录。常规虚拟环境安装依赖时 `MEDIA_PYTHONPATH` 留空；仅使用独立依赖目录时填写它。

配置有效的 `LLM_API_KEY`、`LLM_BASE_URL`、`LLM_MODEL` 即可启用真实分析；`TYPESAFE_API_KEY` 是可选分类器，不是媒体处理的前提。官方 DeepSeek Flash 自动启用视觉；其他模型只有确认支持兼容的图片输入后才设置 `LLM_VISION=1`，这个开关不能让纯文本模型获得识图能力。只做文字或图片演示时不必安装 Python、语音模型或视频依赖。

## 实际处理与耗时

视频上传 → 本地 PyAV 抽帧与 Whisper 转写 → 视觉模型读取画面 → 可选 Jev 判断话题及健康说法 → LLM 生成摘要与回复。LLM 独立模式的图片或视频生成涉及视觉与回复两个云请求，配置 Jev 后增加一次分类。当前视觉接口不直接接收视频或音频；Jev 启用时接收合并后的文本证据。

浏览器等待上限：普通文字 32 秒、文章 60 秒、媒体 180 秒；本地视频子进程最多 120 秒。取消请求会终止子进程。服务最多同时处理两次真实生成，公网邀请模式每进程每小时最多 30 次尝试。

视频使用独立随机临时目录，正常结束、失败和取消都会清理；强制结束进程可能留下文件，部署环境应管理临时目录的保留期限。

## 验证边界

`npm run test:video` 上传真实 11.417 秒中文合成语音视频，实际抽帧、ASR、取消子进程和清理临时文件；云模型使用明确标注的测试数据。结果见 `local-video-test-results.json`。原始语音识别已有同音误字，不能将其当逐字准确的字幕。

`npm run test:live` 调用配置的真实云服务并检查文字、图片、视频与编辑复制闭环。报告明确区分 `llm_only` 与 `jev_llm`，仅 LLM 通过不代表 Jev 已测试；缺少 LLM 凭据或媒体环境时为 blocked。本轮结果见 `live-test-results.json` 与 `QA.md`，历史本地识别通过不等于真实云端全链路通过。微信真机、HTTPS 及不同手机编码仍需实际验证。

协议依据：[DeepSeek 视觉](https://api-docs.deepseek.com/guides/vision/)、[DeepSeek 文件接口](https://api-docs.deepseek.com/guides/files_api/)、[TypeSafe 文本状态](https://docs.typesafe.ai/concepts/state)。
