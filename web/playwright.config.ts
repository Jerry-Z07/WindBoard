/**
 * Playwright 视觉回归配置（P5.1）。
 *
 * 设计取舍：
 * - **不下载浏览器**：用 `channel: 'chrome'` 直接驱动本机已装的 Chrome。
 *   `pnpm add -D @playwright/test` 时用 `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1`，
 *   避免为了跑几张截图多下一个 Chromium（约 130MB）。
 * - **跑生产构建**（`pnpm build` + `vite preview`）而不是 dev server：
 *   dev 构建带 React StrictMode 双渲染与未压缩代码，截图基线跟线上不一致没有意义。
 * - **固定设备参数**：视口 1200×800、DPR 1、浅色配色、禁用动画 —— 截图基线对这三项敏感。
 * - **字体仍是本机字体**：中文字形随系统字体版本变化，所以基线只适合在开发机/同镜像 CI 上比对；
 *   跨机器差异用 `maxDiffPixelRatio` 吸收一部分，真正的跨平台一致性不在本阶段目标内（见 implement.md）。
 */

import { defineConfig, devices } from '@playwright/test'

/** 预览服务器端口（与 `pnpm preview` 默认端口一致，避免和 dev server 的 5173 打架）。 */
const PREVIEW_PORT = 4173

export default defineConfig({
  testDir: './e2e',
  // 截图基线放在 e2e/__screenshots__/，按平台分目录会让 CI 与本地各存一份，这里统一单目录
  snapshotPathTemplate: '{testDir}/__screenshots__/{testFileName}/{arg}{ext}',
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  reporter: [['list']],
  timeout: 60_000,
  expect: {
    timeout: 8_000,
    toHaveScreenshot: {
      // 抗锯齿与文字次像素渲染在不同运行之间会有极小抖动；0.2% 像素差以内视为一致
      maxDiffPixelRatio: 0.002,
      animations: 'disabled',
      caret: 'hide',
    },
  },
  use: {
    ...devices['Desktop Chrome'],
    channel: 'chrome',
    // 注意用 localhost 而不是 127.0.0.1：`vite preview` 默认只监听到 localhost（IPv6 ::1），
    // 写 127.0.0.1 会连不上（实测 curl 返回 000，webServer 直接超时）。
    baseURL: `http://localhost:${PREVIEW_PORT}`,
    viewport: { width: 1200, height: 800 },
    deviceScaleFactor: 1,
    colorScheme: 'light',
    locale: 'zh-CN',
    timezoneId: 'Asia/Shanghai',
    trace: 'off',
  },
  webServer: {
    command: `pnpm build && pnpm preview --port ${PREVIEW_PORT} --strictPort`,
    url: `http://localhost:${PREVIEW_PORT}`,
    reuseExistingServer: !process.env.CI,
    timeout: 240_000,
  },
})
