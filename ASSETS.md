# 知意 · 极简版视觉资产

## 当前界面

当前页面不使用装饰插画或外部图像。界面由 `public/styles.css` 实现，主色为米白、深绿与少量暖黄；favicon 与 Android 图标同样不使用星形或星球。整体保留年轻、温和的气质，但不复制《小王子》的书封、原插图或文字。

## 自托管网页字体

- 少量空状态与入站提示：使用 [LXGW WenKai](https://github.com/lxgw/LxgwWenKai) 的 Web 子集 `LXGWWenKai-Regular.woff2`。原作者 LXGW / Klee Project Authors，SIL OFL 1.1；完整许可在 `public/assets/OFL-LXGWWenKai.txt`。
- 标题、正文、输入框、按钮与其他界面文字：使用设备系统字体。
- 文楷字形轮廓未改绘；已做 WOFF2 转换、子集与元数据更名，内部名称为 Xingyu Hand Web，CSS 名称为 WenKai。文件覆盖 GB2312 常用汉字、常用拉丁字母及标点，缺字使用系统字体兜底。
- 字体从同源加载，不依赖第三方字体服务。保留随包许可，不将字体文件单独售卖。

字体原文件下载于 2026-10-09，来源为以上官方代码仓库。

## 历史素材

以下文件为旧版保留，当前 `index.html` 和 `styles.css` 不引用：

- `public/assets/rose-atelier.svg`：此前用原创 SVG 路径、油彩笔触与纹理绘制的玫瑰星球。
- `public/assets/MaShanZheng-Regular.woff2`：旧书法标题字体，来自 [Ma Shan Zheng](https://github.com/googlefonts/mashanzheng)，SIL OFL 1.1，许可为 `public/assets/OFL-MaShanZheng.txt`。Web 子集内部名称为 Xingyu Brush Web，字形轮廓未改绘。

`public/assets/starry-night.jpg` 是上一版使用的 Vincent van Gogh《The Starry Night》(1889)，本版页面已不再引用。来源 [Wikimedia Commons / Google Art Project](https://commons.wikimedia.org/wiki/File:Van_Gogh_-_Starry_Night_-_Google_Art_Project.jpg)，页面标记 Public Domain、PD-old-100-expired / PD-Art。保留以便旧截图和历史版本对照。

上一轮内置 ImageGen 返回 model_not_found，没有生成成功的素材。本版使用 CSS 和开源字体，不冒称调用过图像模型。
