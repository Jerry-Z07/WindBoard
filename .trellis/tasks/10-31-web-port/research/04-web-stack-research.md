# 调研 04：Web 技术选型

> 调研时间：2026-10-31。所有版本号与兼容性均为当日联网核实结果；未核实项在文末单列。
> 原始调研报告（含更细的引用与置信度标注）见 `04a-web-stack-raw-report.md`；本文是提炼版。

## 1. UI 库

**结论：`@fluentui/react-components` 9.x（MIT）。**

- 微软当前的 Web 设计系统，实现 **Fluent 2**——与 WinUI 3 同一设计语言，是"类 WinUI 3 前端库"的最优解。
- 最新稳定 `9.75.0`（2026-10-27）。来源：<https://registry.npmjs.org/@fluentui/react-components>、<https://github.com/microsoft/fluentui>、<https://react.fluentui.dev/>
- 控件映射近乎 1:1：

| WinUI 3 | Fluent UI v9 |
|---|---|
| NavigationView | `react-nav` |
| ContentDialog | `react-dialog` |
| InfoBar | `react-message-bar` |
| TeachingTip | `react-teaching-popover` |
| Flyout | `react-popover` / `react-menu` |
| CommandBar | `react-toolbar` |

- 注意：`InfoButton`、`Alert` 仍为 beta。
- 备选 `@fluentui/web-components`（v3.1.3，2026-08-25）仍存活，但微软概览页停在 2021-11，属二等投入，不选。

**材料缺口（必须接受）**：

- **Mica** 采样桌面壁纸（<https://learn.microsoft.com/en-us/windows/apps/design/style/mica>）→ 浏览器物理不可复刻，只能用不透明渐变近似。
- **Acrylic** = blur + exclusion + tint + noise（<https://learn.microsoft.com/en-us/windows/apps/design/style/acrylic>）→ 可用 `backdrop-filter` + 着色 + SVG 噪点近似。

**自定义标题栏**：WinUI 3 用 `AppWindow.TitleBar`；Web 等价物是 PWA 的 `window-controls-overlay`（Chrome 105+，Firefox/Safari 不支持）。

## 2. 为什么不用 Uno / Avalonia WASM

| 方案 | 事实 | 结论 |
|---|---|---|
| Uno Platform | 确实能在 WebAssembly 上跑 WinUI/UWP XAML（<https://platform.uno/uno-platform-for-web-webassembly/>），但**不公布体积** | 不选 |
| Avalonia WASM | 支持 WebGL2 / WebGL1 / Software2D（<https://docs.avaloniaui.net/docs/platform-specific-guides/webassembly>），体积同样未公布，Fluent 主题非像素一致 | 不选 |

理由：绘画优先的应用不能背 .NET 工具链 + 数 MB 启动开销，而且**仍然拿不到 Mica**——换来的只是 XAML 复用，代价过高。

## 3. 笔迹渲染

| 方案 | 许可 | 说明 |
|---|---|---|
| perfect-freehand 1.2.3（2026-02-01） | MIT | 把压感采样转成**填充轮廓多边形**，Excalidraw 固定用 1.2.0。来源：<https://github.com/steveruizok/perfect-freehand> |
| tldraw 5.2 自研算法 | 自定义 | 方向推导 + 单遍轮廓，宣称快 2x、SVG 路径小 57%（<https://tldraw.dev/blog/tldraw-sdk-5.2>）。**许可证禁止生产使用**，只借鉴思路 |

**本项目的选择**：不引入 perfect-freehand 的平滑，自己实现"骨架 → 变宽轮廓"（见 `design.md` D2），因为桌面 D2D 几何是"折线段 + 线性插值半径"，任何平滑都会偏离原始笔迹。

渲染后端：Canvas2D 足够数千笔迹；已提交笔迹放后备位图，只重绘活动笔迹；OffscreenCanvas + worker 按需（`transferControlToOffscreen`：Chrome 69 / Firefox 105 / Safari 16.4）。WebGL/WebGPU 只买到纹理笔刷，不解决正确性。

## 4. 指针事件

| 能力 | 兼容性 | 用途 |
|---|---|---|
| `pointerrawupdate` | Chrome 77+、Firefox 148、Safari 无 | 最高频采样 |
| `getCoalescedEvents()` | Chrome 58 / Firefox 59 / Safari 18.2 | 回退路径 |
| `pressure` | Chrome 55 / Firefox 59 / Safari 13 | 压感；鼠标按下报 0.5、触摸报 0/1，需归一化 |

**必须遵守的约束**：

- iOS canvas 上限 4096×4096（<https://developer.mozilla.org/en-US/docs/Web/HTML/Element/canvas#maximum_canvas_size>）→ 只能"视口大小画布 + 变换"，不能铺一张大位图。
- 按 DPR 放大后备存储，DPR 上限取 2。
- canvas 设 `touch-action: none`。
- `pointerType === 'pen'` 活动时忽略 `touch`（掌拒）。

## 5. 文件与压缩

| 需求 | 选择 | 许可 | 理由 |
|---|---|---|---|
| ZIP 读写 | `fflate` 0.8.3 | MIT | 8kB、流式，宣称比 pako 快至 50%（<https://github.com/101arrowz/fflate>） |
| ZIP 备选 | `@zip.js/zip.js` 2.24.0（2026-10-16） | BSD-3 | 仅在需要 >4GB / 加密 / 增量写时使用 |
| 避免 | JSZip | MIT | `generateAsync` 全量驻留内存（<https://stuk.github.io/jszip/documentation/limitations.html>） |
| PDF | `pdf-lib` 1.17.1 | MIT | 1.17.1 发布于 2021-11-06，2024-07 后停更但稳定（<https://github.com/Hopding/pdf-lib>）；备选 jsPDF 4.2.1（2026-03-17 活跃） |
| PNG | `canvas.toBlob('image/png')` | — | 需要 DPI 时按 `dpi/96` 放大渲染，并手动写入 `pHYs` 块 |

**File System Access API**：仅 Chromium 86+（Firefox/Safari 均为 false）。需要 transient user activation；用户取消抛 `AbortError` → 必须回退 `<a download>` / `<input type=file>` / 拖拽。

**持久化**：OPFS（Baseline 2023-03，同步句柄仅 worker 可用）存页面 blob 与自动保存；IndexedDB 存元数据与撤销日志。

## 6. 部署与可安装性

- 可安装条件：HTTPS + manifest（`name` / `icons` 192+512 / `start_url` / `display`）；**不强制 service worker**（<https://web.dev/articles/install-criteria>），但离线必须要有。
- `.wbix` 双击打开：manifest `file_handlers` + `launchQueue.setConsumer()`，Chrome/Edge 102+，Firefox/Safari 无且**不可 polyfill**，每次打开有权限提示（<https://developer.chrome.com/docs/capabilities/web-apis/file-handling>）。
- 拖拽打开全平台可用（`dragover` / `drop` + `preventDefault`）——作为主入口。

## 7. 许可证红线

| 项目 | 许可 | 处理 |
|---|---|---|
| tldraw | 自定义，"Not to use the Software in Production Environments"（<https://github.com/tldraw/tldraw/blob/main/LICENSE.md>） | 只借鉴思路，**不 vendor 代码** |
| Excalidraw | MIT | 可参考实现 |
| whitebophir | AGPL-3.0 | 避开 |
| perfect-freehand / fflate | MIT | 可用 |
| @zip.js/zip.js | BSD-3 | 可用 |
| comlink | Apache-2.0 | 可用 |

## 8. 未核实项（不要在实现中当事实使用）

1. Uno Platform / Avalonia WASM 的实际产物体积。
2. WinUI 3 的圆角常量（Learn 对应页面 404，网上流传的 4/8px 属传闻）→ 一律以当前 XAML 里的实际值为准。
3. iOS Apple Pencil 的压感取值分布与事件率。
4. Microsoft Whiteboard Web 版的工程实现笔记（无公开资料）。
5. 所有性能数字（"快 2x""小 57%""快 50%"）均为厂商或博客宣称，未经本项目实测。
