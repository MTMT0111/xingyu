import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const demoUrl = process.env.WECHAT_DEMO_URL || 'https://mtmt0111.github.io/haohaohui-demo/';
const output = fileURLToPath(new URL('../wechat-web-test-results.json', import.meta.url));
const screenshotDir = fileURLToPath(new URL('../screenshots/core/', import.meta.url));
const profiles = [
  {
    name: 'Android WeChat',
    viewport: { width: 390, height: 844 },
    userAgent: 'Mozilla/5.0 (Linux; Android 14; Pixel 7 Build/UQ1A.240205.004; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/122.0.0.0 Mobile Safari/537.36 MicroMessenger/8.0.50.2701(0x2800323B) WeChat/arm64'
  },
  {
    name: 'iPhone WeChat',
    viewport: { width: 375, height: 812 },
    userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148 MicroMessenger/8.0.50 NetType/WIFI Language/zh_CN'
  }
];

const browser = await chromium.launch({ headless: true, ...(process.env.BROWSER_EXECUTABLE ? { executablePath: process.env.BROWSER_EXECUTABLE } : {}) });
const results = [];
try {
  await mkdir(screenshotDir, { recursive: true });
  for (const profile of profiles) {
    const errors = [];
    const context = await browser.newContext({ userAgent: profile.userAgent, viewport: profile.viewport, isMobile: true, hasTouch: true, locale: 'zh-CN' });
    await context.addInitScript(() => {
      Object.defineProperty(navigator, 'clipboard', { configurable: true, value: undefined });
      Object.defineProperty(navigator, 'share', { configurable: true, value: undefined });
      Object.defineProperty(navigator, 'canShare', { configurable: true, value: undefined });
      document.execCommand = () => false;
    });
    const page = await context.newPage();
    page.on('pageerror', error => errors.push(error.message));
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
    const response = await page.goto(`${demoUrl}?wechat-smoke=${Date.now()}`, { waitUntil: 'networkidle' });
    assert.equal(response.status(), 200);
    await page.evaluate(() => localStorage.clear());
    await page.reload({ waitUntil: 'networkidle' });
    assert.equal(await page.locator('#hero-title').innerText(), '知意');
    assert.match(await page.locator('#mode-label').innerText(), /本地示例/);
    await page.locator('#source-text').fill('天凉了，记得穿暖一点，也要按时吃饭。');
    await page.locator('#recipient-select').selectOption('mother');
    await page.locator('#tone-select').selectOption('brief');
    await page.locator('#translate-button').click();
    await page.locator('#result-content').waitFor({ state: 'visible' });
    assert.match(await page.locator('#reply-text').inputValue(), /^妈，/);
    assert.match(await page.locator('#share-note').innerText(), /当前在微信内.*左上角返回聊天/);
    await page.locator('#reply-text').fill('妈，知道啦，我会照顾好自己，你也要注意保暖。');
    await page.locator('#copy-reply').click();
    assert.match(await page.locator('#copy-status').innerText(), /已选中文字.*长按/);
    assert.equal(await page.locator('#reply-text').evaluate(element => element.selectionStart === 0 && element.selectionEnd === element.value.length), true);
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    assert.ok((await page.locator('#copy-reply').boundingBox()).height >= 44);
    await page.reload({ waitUntil: 'networkidle' });
    await page.locator('#history-panel summary').click();
    assert.equal(await page.locator('.history-item').count(), 1);
    await page.locator('.history-open').click();
    assert.equal(await page.locator('#reply-text').inputValue(), '妈，知道啦，我会照顾好自己，你也要注意保暖。');
    if (profile.name === 'Android WeChat') {
      // Playwright full-page capture paints off-screen fixed skip links inside the stitched image.
      await page.addStyleTag({ content: '.skip-link { display: none !important; }' });
      await page.screenshot({ path: `${screenshotDir}/wechat-web-390.png`, fullPage: true, animations: 'disabled' });
    }
    assert.deepEqual(errors, []);
    results.push({ profile: profile.name, viewport: profile.viewport, passed: true, checks: ['load', 'generate', 'edit', 'manual-copy', 'history-restore', 'no-overflow', '44px-copy-target'] });
    await context.close();
  }
} finally {
  await browser.close();
}

const report = { date: new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Shanghai' }).format(new Date()), url: demoUrl, profiles: results, physicalDevice: false, messagesSent: false };
await writeFile(output, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify(report));
