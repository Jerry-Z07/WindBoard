# 实施计划：WindBoard Web

阶段门禁：每个阶段末尾必须跑通该阶段验收，否则不进入下一阶段。P0 是硬门禁——互操作不通，后续全部作废。

图例：`[ ]` 未开始 / `[~]` 进行中 / `[x]` 完成

---

## P0 契约先行（互操作测试台）

目标：在没有 UI 的情况下，证明 `.wbix` 双端读写语义一致。本阶段不写任何绘画代码。

- [x] **P0.1 仓库骨架**
  - `web/` 初始化（Vite + React + TS + Fluent UI v9 + vitest + Playwright），版本冻结进 `package.json`
  - `web/.editorconfig` 覆盖根配置（LF、2 空格）；根 `.gitignore` 增加 `node_modules/ dist/ .interop/`
  - `web/README.md` 说明定位与命令；根 `AGENTS.md` 的 Docs 列表增加指向
  - 验收：`pnpm install && pnpm build` 通过；`git status` 干净（除预期新增）
- [x] **P0.2 数值与文本格式探针**
  - 实测 .NET `float.ToString("R")` 对 `0.1f / 1e-7f / 3.4028235e38f / 1f/3f / -0f / 1234567.8f` 的真实输出
  - 实现 `persistence/wbix/f32-format.ts`（最短往返 float32）与 `json-options.ts`（2 空格缩进、`\uXXXX` 转义规则）
  - 单元测试：对上述探针值逐一断言字符串相等
  - 验收：TS 与 .NET 输出逐字符一致；不一致项记录为已知差异并降级为语义比对
- [x] **P0.3 WBIX 读**
  - `manifest.ts` / `page-payload.ts` / `ink-codec.ts`（v1/v2/v3 + shape 嗅探 + `kind` 缺省）/ `paths.ts`（`\`→`/`、前缀校验、禁 `..`）/ `errors.ts`
  - 容忍策略与桌面一致（跳过坏元素、未知类型、超大资源）
  - 单元测试：每个容忍分支一条用例
- [x] **P0.4 WBIX 写**
  - 固定 v3；条目顺序 pages → assets → manifest 最后；`null` 成员输出；`page-{i:000}.json` 命名；`assets/elements/{id:N}{ext}`
  - 验收：写出的包能被桌面版打开（人工验证一次并记录）
- [x] **P0.5 语料与一致性测试台**
  - 语料目录 `WindBoard.Tests/Fixtures/wbix/`（单一事实源，web 用相对路径读取，不复制）
  - `model/*.json`：空文档、单笔单点、变宽多段笔、四种形状、双图层元素、多页、Unicode 文本、极端坐标（±1e6）
  - `*.wbix`：桌面版真实导出（v1/v2/v3 各若干）
  - C# 侧 `WindBoard.Tests/Board/Persistence/WbixInteropDumpTests.cs`（环境变量驱动目录）
  - `web/scripts/interop.mjs` 五步 A~E 全绿
  - 验收：AC3 达成；任何一步 diff 失败时脚本以非零码退出并打印首个差异路径
- [x] **P0.6 设置格式调研**
  - 读 `WindBoard/Settings/` 源码，确认设置导出文件的结构与字段名，写入 `research/05-settings-format.md`
  - 验收：文档给出字段表与一份真实样例
- [x] **P0.7 版本契约：读侧分派**（桌面端，见 design D11）
  - 引入 `IWbixPageReader` + `WbixPageReaderV1V2` / `WbixPageReaderV3` + `WbixPageReaderRegistry.For(version)`
  - `InkItemSnapshotJsonConverter` 增加读模式（`LegacyFlat` / `Wrapped`），由 options 实例注入，不改公开 API
  - v3 保留扁平条目兜底并 Warn 一次；未知版本沿用 `Wbix_VersionNotSupported_Fmt` 文案
  - 新增测试：v1 扁平、v2 扁平、v3 包装、v3 含扁平条目（兜底生效）、v4 拒绝
  - 验收：AC11；既有 50 个 `.wbix` 测试全绿且一行未改
- [x] **P0.8 字节级 golden**（桌面端，见 design D12）
  - `WindBoard.Tests/Fixtures/wbix/expected/entries.txt` + `expected/*.golden.json`
  - 断言 zip 条目名与顺序、每条解压文本逐字节相等；`createdUtc` 先正则校验格式、再归一化后比对
  - 反向验证一次：人为改动缩进/键序/数字格式，测试必须失败
  - 验收：AC12
- [x] **P0.9 笔迹几何规范**（文档）
  - 新建 `docs/dev/guides/ink-rendering.zh-CN.md`：折线段、每段线性插值半径、圆头圆角、单点圆、箭头翼、绘制顺序、常量表
  - `docs/dev/guides/wbix.zh-CN.md` 增加"版本契约"小节并交叉链接；根 `AGENTS.md` 的 Docs 列表补一行
  - 验收：AC13；文档描述与 31 张渲染快照基线一致

**P0 出口**：`pnpm verify` 全绿 + AC3 / AC11 / AC12 / AC13 达成 + 设置格式有据可查。

**P0 完成记录**（2026-10-31）：出口已满足。桌面端 Release 构建 0 警告 0 错误，`dotnet test` 649 通过 / 1 跳过 / 0 失败；网页端 `pnpm verify:all` 182 例单测 + A~E 五步全绿。
- AC3：`pnpm interop` 的 B 步比对 10 个包（7 个网页端产出 + 3 个桌面语料）逐字段零差异；C 步网页端写出的包与 `Fixtures/wbix/expected/**` 逐字节相同。
- AC11：`WbixPageReaderRegistry` 按版本区间分派，v1/v2 走扁平读器、v3 走包装读器（保留扁平兜底并每文件告警一次），既有 50 个 `.wbix` 测试一行未改。
- AC12：golden 锁定解压后条目文本 + 条目名与顺序；反向验证 4 组篡改（缩进、`24`→`24.0`、键序、f32 精度）全部被捕获。
- AC13：`docs/dev/guides/ink-rendering.zh-CN.md` 落地（238 行）。
- 实现与计划的偏差：P0.1 的 Playwright 推迟到 P5.1（P0 不需要浏览器）；P0.4 的"人工验证一次"由 `pnpm interop` 的 D/E 步自动化取代；P0.7 用版本区间（`MinVersion`/`MaxVersion`）而非单值 `SupportedVersion`；`JsonSerializerOptions.NewLine` 显式钉死 CRLF（原为平台默认）。
- 遗留：`wbix.en-US.md` 尚未同步 zh-CN 新增小节；语料 regen 先写临时目录再原子替换（防并发）；`LocalizationKeyAuditTests.CreateSkipException` 的既有反射缺陷（见交付说明）。

---

## P1 画布内核（能画、能缩、能撤）

- [x] **P1.1 域模型与命令栈**：`document/page/workspace/viewport/session` + 五类命令，粒度与桌面一致
- [x] **P1.2 几何与轮廓**：`geometry.ts`（圆头折线、变宽、箭头翼）+ `outline.ts`（展开 + zoom 分桶缓存）
- [x] **P1.3 场景渲染**：绘制顺序、背景、单点圆、实时预览层（`rasterizer.ts` 供导出复用 → 随 P4 落地）
- [x] **P1.4 帧调度**：rAF、DPR 后备存储（上限 2）（书写局部重绘、运动 0.95 降质 → 留钩子，P5 落地）
- [x] **P1.5 指针路由**：决策表纯函数 + 表驱动测试；笔/鼠标/触摸；右键平移；滚轮锚点缩放
- [x] **P1.6 工具**：笔（`0.5/zoom` 采样阈值、压感 clamp）、形状（两点定义）、橡皮（像素 + 整笔 + 滑动清屏，一次手势一条命令）
- [x] **P1.7 快捷键**：`Ctrl+Z`/`Ctrl+Y` 与改绑模型
- 验收：能画能缩能撤；保存 `.wbix` → 重新载入 → 视觉一致；桌面渲染快照对比无结构性差异

### P1 完成记录（2026-11-01）

- 验收证据：`pnpm verify` → tsc 0 错误 + **25 文件 / 690 例** + 构建（43 modules，js 263.12 kB / gzip 83.08 kB）；`pnpm lint` 0/0（76 文件）；`pnpm interop` A~E 全绿（双端 `.wbix` 读写仍逐字节一致）。
- "保存 → 载入 → 视觉一致"由 `web/tests/unit/board-roundtrip.test.ts` 覆盖：模型层比对快照，渲染层比对 `FakeContext2D` 的绘制调用序列，并额外断言"再次写出字节与首次完全相同"。
- 实现与计划的偏差：
  1. `rasterizer.ts`（P1.3 的导出复用模块）推迟到 P4.2/P4.3，与导出实现一起写——P1 不需要它。
  2. 书写局部重绘与运动 0.95 降质只留了 `enableDirtyRect` / `interactiveQualityScale` 钩子（默认关闭/1），P5 再做；原因是浏览器 Canvas2D 与桌面 D3D11 的脏矩形收益模型不同，需要先有真实绘制量数据。
  3. DOM 事件类型改用 `src/canvas/dom-types.ts` 的结构化视图（`DomPointerLike` 等），而非 DOM 类型名——`web/tsconfig.test.json` 的 `lib` 只有 ES2023，写 DOM 类型名会让测试工程编译失败（与 `src/render/canvas-types.ts` 同一处理方式）。
  4. 帧率上限 60 与 DPR 上限 2 按桌面常量实现，但未做桌面那种"渲染缩放随交互态切换"（0.95/0.25），见偏差 2。
- 遗留：
  1. 浏览器手工验证缺失：Chrome MCP 在本机不可用（`Browser.setContentsSize` 协议错误），P1 只用 node 单测覆盖了 DOM 适配器，真实浏览器端到端留到 P5.1 引入 Playwright 时补。
  2. 元素卡片只做到"结构一致"（无位图/图标用 Unicode 占位），位图与真实图标在 P2.4 补齐。
  3. 运行时保留双精度浮点，WBIX 读入值经 f32 量化（见 design.md D13）。

---

## P2 选择、元素与多页

- [x] **P2.1 命中测试**：笔迹/形状/元素，容差 8 DIP；marquee 阈值 6 DIP
  （`board/editing/` 的 `ink-bounds` / `pick-test` / `rect-select` / `element-pick`，逐条对齐 `InkItemPickTest.cs`、`InkItemRectSelectTest.cs`、`ElementPickTest.cs`、`ElementRectSelectTest.cs`）
- [x] **P2.2 选择工具**：点击/框选、移动、双指矩阵变换、组合命令
  （`tools/select-tool.ts` 1103 行对齐 `SelectTool.cs` 910 行；手柄拖动缩放**未做**——桌面手柄只对"单选元素"显示且元素不支持旋转/非均匀缩放，网页端同理只做了选择框与元素手柄的视觉）
- [x] **P2.3 覆盖层 UI**：选择框、8×14 DIP 手柄、操作条（置顶/复制/删除）、形状属性快捷面板
  （canvas 画框与手柄：`render/selection*.ts`；DOM 浮层：`ui/overlay/*`；形状属性走 `applySelectedShapeGeometry` 命令栈）
- [x] **P2.4 元素卡**：文本/链接/媒体/文件四类渲染 + 双击打开（500ms/10 DIP）
  （`render/element-card.ts` 537 行对齐 `BoardSceneRenderer.cs:554-945`；双击判定与外部打开在 `board/editing/element-open.ts` + `open-element-external.ts`）
- [x] **P2.5 多页**：增删切换、恒 ≥1 页、每页独立撤销栈（P1 已完成，`board/workspace.ts` + 控制器 `addPage`/`removeCurrentPage`/`setCurrentPageIndex`）

实现要点与踩坑（P2 记录）：

- **编辑层不反向依赖渲染层**：`rect-select.ts` 自带私有 `intersectsAabb`（与 `render/bounds.ts` 同式），
  避免 `board/**` import `render/**`；`getInkItemsScreenBounds` 收 `BoardViewport` 实例（域对象，不是渲染对象）。
- **框选是"相交"不是"包含"**，且桌面存在归一化不对称（快速剔除用原始 min/max、几何判定内部才归一化）——
  照抄并在 JSDoc 写明"调用方必须传已归一化 min/max"，测试用 3 例固定该行为。
- **单选元素才画手柄**（`SelectionOverlay.showHandles`），选中笔迹只画框，与 `BoardCanvasControl.Rendering.cs:200-234` 一致。
- **桌面选择覆盖层不是 D2D 绘制而是 XAML 浮层**：真源在 `Controls/BoardCanvasControl.xaml:13-45`，
  所以手柄 14×14、选择框 2px 圆角 8、框选矩形无填充（网页端加了 12% 填充，常量可一键还原）。
- **`order` 单边修复**：桌面 `BringElementToFrontCommand` 只动列表顺序、不动 `Order` 字段，
  导致"置顶→保存→重开"在桌面本身就保存过期 order；网页端在重排命令里把受影响层的 `order` 置 `-1`，
  让写侧的 `order < 0 ? 层内下标 : order` 兜底生效（见 `board/commands.ts` 注释）。
- **`getState()` 缓存导致 React 覆盖层不刷新**（本轮实测踩到）：命令只改"选中对象的内容"而不改选中集时，
  `refreshSnapshot` 因引用未变判定"没变化"，形状属性浮层停在旧数值上（canvas 因为按需重绘反而正确）。
  修法：`SelectTool.notifySelectionContentChanged()` + 控制器在几何/变换命令后显式调用；
  `board-controller-shape-geometry.test.ts` 用一条断言锁住"selection 必须是新对象"。
- **桌面没有 Ctrl+A / Esc / Delete 的键盘路径**（选择操作全是画布浮层按钮）。P2 当时因此没有加；**P2 之后用户明确要求补键盘入口**，已在 `interaction/shortcuts.ts` 实现 `selectAll` / `clearSelection` / `deleteSelection` 并由 `canvas/board-controller.ts` 的 `handleKey` 接入（仅在 `tool === 'select'` 生效），属于**有意偏差**，登记在 `docs/dev/guides/web-port.zh-CN.md` §6。
- `Duplicate` 的偏移是 `12 DIP ÷ zoom`（`BoardCanvasControl.Rendering.cs:510`）。
- 双击打开在浏览器里的取舍：`window.open(url,'_blank')` + 立即 `opener = null`（带 `noopener` 时返回值恒为 null，
  与"返回 null 即被拦"互斥）；本地文件/媒体在浏览器拿不到路径，返回明确提示（P4 资源层就绪后可接 blob URL）。
- 验收：AC1 中的元素与多页部分；选择变换可撤销且重做正确

---

## P3 UI 外壳（1:1 还原）

- [x] **P3.1 标题栏**：自定义 TitleBar、全屏隐藏（`window-controls-overlay` 未用，浏览器原生标题栏已够）
- [x] **P3.2 Dock**：三段式布局、60×52 / 20px 图标 / 11px 文字 / **底板 14 圆角 + 按钮 10 圆角** / 0.85 底板 / 强制浅色；页面指示 `n / m` 单页隐藏
- [x] **P3.3 弹出面板**：笔（**二次点击**才弹）、橡皮（两模式 + 滑动清屏）、形状（四项）、页面管理（宽 280、项宽 240、104px 缩略图）、更多菜单
- [x] **P3.4 提醒栈**：右上 InfoBar，≤3 条，自动关闭 6/8/10s
- [x] **P3.5 设置窗口**：侧栏导航 + 设置卡片，6 页（常规/外观/书写/快捷键/Dock/关于）
- [x] **P3.6 对话框**：导出、导入、通用确认（真实文件能力属 P4）

### P3 完成记录（2026-11-01）

- 验收证据：`pnpm verify` → tsc 0 错误 + **31 文件 / 872 例**（P1 收尾时 690 例，新增 182 例）；`pnpm lint` 0 警告 0 错误（115 文件）；`pnpm build` 通过（js 734.41 kB / gzip 210.41 kB）；`pnpm interop` A~E 全绿。
- 令牌来源改为 **WinUI 主题字典**而非观感（design D14）：底板 `#F2F2F2`@85%、前景 `#000000`、分隔线 40% 黑、选中 `#1976D2` + 白字，均取自 `generic.xaml` 的 Light 字典与 `MainWindow.xaml:57-70` 的资源覆盖。
- 实现与计划的偏差：
  1. **底板圆角 14、按钮圆角 10**（原计划写的"14px 圆角"指底板）；页面 Dock 按钮是 52×52、指示器最小 84 宽 12 号 SemiBold、页面管理项是**宽 240**（不是行高 240，行高由内容撑开）。
  2. 设置窗口是 **6 页**不是 5 页（常规/外观/书写/快捷键/Dock/关于），且用模态 `Dialog` + 侧栏导航表达桌面的独立 `SettingsWindow`（NavigationView）。
  3. 粗细预设与滑条是**二选一**（`MainWindow.xaml.cs:481-494`），不是"预设常显 + 额外滑条"。
  4. "最小化"在浏览器无对应能力：保留按钮以维持三段式布局，点击弹一条解释性提醒（不置灰也不隐藏，否则要么无法解释、要么破坏布局）。
  5. 选择工具桌面的 `TouchPointer` 图标在 Fluent 图标库无同名项，取 `CursorRegular` 并在注释说明。
  6. 页面管理面板以页面指示器为锚点（桌面以整个右侧 Dock 为锚点），右边缘内缩约一个按钮宽。
- 遗留：
  1. **未做浏览器冒烟**：Chrome MCP 在本机不可用（`Browser.setContentsSize` 协议错误），P3 全部由 tsc + 单测 + 逐条桌面出处核对保证。合入后需人工 `pnpm dev` 看一眼三处：Popover 是否贴在 Dock 上方、滑动清屏把手是否跟随指针、删页后当前页落点。
  2. InfoBar 卡片内部度量是近似值（内置控件模板不在仓库）；Fluent 的 Blue 只有 `Background2`，信息级蓝底比 WinUI 浅蓝更饱和。
  3. 首屏 js 734 kB / gzip 210 kB，Fluent UI 与图标全量进主 chunk → P5.2 做 vendor 拆分（当前 Vite 有 `>500 kB` 提示）。

#### 浏览器复核（2026-11-01，Chrome DevTools MCP 打通后）

前置：MCP 之前一直报 `Browser.setContentsSize`，根因是它自己那份持久化 Chrome 配置里
`~/.cache/chrome-devtools-mcp/chrome-profile/Default/Preferences` 的
`browser.window_placement.maximized = true`——Chrome 每次恢复成最大化窗口，而 MCP 传的
`--viewport 1600x1000` 要走 `Browser.setContentsSize`，Chrome 对最大化窗口一律拒绝。
把该标记改成 `false` 并把窗口收进工作区后即可连上。

在真实浏览器里逐项点过后发现并修掉 6 个缺陷（**全部是「接线/样式口径」问题，单测覆盖不到**）：

1. **整页白屏**：`BoardControllerProvider` 只包在 `BoardSurface` 自己的 children 上，
   而 Dock 是它的兄弟节点 → `useBoardController` 抛错、React 卸载整棵树。
   改为 `AppShell` 统一持有控制器状态并包住 Dock。
2. **画布完全无输入**：`attachBoardInput` 在 P1 的临时 `App.tsx` 里调用，换成外壳后
   **一处调用都没有**，控制器只建模型和渲染、不碰 DOM 事件。改为在 `BoardSurface`
   的挂载 effect 里与控制器同生共死（`detach()` 进 cleanup）。
3. **默认笔色错误**：照抄了 `BoardCanvasControl` 字段初始化的黑色兜底值，
   而桌面启动时 `MainWindow.ApplyInitialPenColor()` 会把笔色改成**色板首个可解析色**
   （默认 `#FFFFFF`）。深色画布上黑笔几乎不可见。新增纯函数
   `resolveInitialPenColorHex()`（含 3 例单测）并把结果传给 `createBoardController`。
4. **单页时右侧 Dock 整体消失**：桌面 `showPager = total > 1` 只折叠「上一页/指示器/下一页」，
   **「+」始终可见**；整块隐藏后用户没有任何入口新增第二页。改为只过滤翻页三件套。
5. **Dock 悬停态变成青色**：`--wb-dock-hover: #14ffffff` / `--wb-dock-pressed: #22ffffff`
   是 XAML 的 **ARGB**（alpha 0x14 / 0x22 的白），写进 CSS 会被读成 `#14FFFF`（不透明青）。
   改为 `rgb(255 255 255 / 7.8%)` / `13.3%`，并在 tokens.css 注明这个坑。
6. **设置/导出/导入对话框塌掉**：Fluent v9 用原子类下发 `width`/`max-width: 600px`/`height`/
   `display: grid`，与单类选择器同权重但注入在静态样式表之后，`.wb-settings-surface` /
   `.wb-settings-body` 全被盖掉（表现为窗口被压到 600 宽、卡片文字一字一行、关闭按钮跑到面板外）。
   改用 `.fui-DialogSurface.wb-settings-surface` 双类提高权重；`DialogTitle` 的 `action`
   是标题的**兄弟节点**，DialogBody 改成 flex 后需自己接管 `fui-DialogTitle__action` 的绝对定位。
   另修：`ExportDialog` / `ImportDialog` 渲染在 provider 之外 → 点导出直接白屏，已一并包进 provider。

另外修正三处口径：擦除模式卡漏了浅色底（桌面 `SystemControlBackgroundChromeMediumLowBrush`）；
页面列表项 `width: 240` 未声明 `box-sizing: border-box`（XAML 的 Width 含 padding）导致多出横向滚动条；
滑动清屏的阈值判定读的是渲染闭包里的 `offset`，快速拖动时最后一帧会丢（改为 `offsetRef` 实时值），
并把 `setPointerCapture` 的 `NotFoundError` 一并兜住。

复核通过的行为：绘制（含压感粗细）、撤销/重做态、笔面板（白色为选中）、橡皮面板（空页时整条置灰、
拖过 90% 清屏并自动收起、清屏入撤销栈）、形状面板、页面管理（新增页后出现 `2 / 2`、删页后回退并把
面板收起）、更多菜单、设置 6 页、导出/导入对话框、提醒栈（InfoBar 蓝色信息条 + 自动关闭）。

**仍未做**：页面缩略图是占位块（显示页码，不是页面渲染快照）；`pnpm dev` 下的全屏切换未验证
（`requestFullscreen` 需要真实用户手势）。
- 验收：AC6、AC7；Playwright 截图与桌面参考图结构一致

#### 浏览器复核第二轮（2026-11-01 晚，界面还原优先）

第一轮遗留的三项已消化：页面缩略图改为真实渲染（`4925764`）；全屏切换经 MCP 真实点击验证
（`document.fullscreenElement` 非空、标题栏隐藏、退出后恢复）；设置 6 页逐页点过。

本轮按桌面 XAML 补齐四块界面并修掉 5 个缺陷：

1. **设置搜索框**（`SettingsWindow.xaml:23-44` 的 `AutoSuggestBox`）：`settings/search-targets.ts`
   的卡片清单 + 纯匹配函数（空白不出建议、`OrdinalIgnoreCase` 子串、最多 8 条），
   `SettingsSearchBox` 负责交互；选中后切页、`scrollIntoView` 居中并高亮 1.2s。
   卡片清单与页面组件共用一个 `SETTINGS_ANCHORS` 常量，避免两边字符串漂移。
2. **外观页改回"可点卡片 + 二级弹窗"**（桌面 `AppearanceSettingsPage.xaml:61-124`）：
   画布背景颜色卡 → `CanvasColorDialog`（取色器 + HEX + 恢复默认，**即时写入**、取消回滚）；
   元素卡片主题卡 → `ElementCardThemeDialog`（**确定才写入**，与前者语义相反，桌面就是这么设计的）；
   Dock 设置卡 → 跳转 Dock 页（桌面是 `Frame.Navigate`）。`SettingsCard` 新增 `onClick`，
   有 `onClick` 时渲染成真 `<button>`（键盘/读屏语义交给浏览器）。
3. **Dock 页排序编辑器**（桌面 `DockSettingsPage.xaml:47-229` 的四组 `ListView`）：
   指针拖动 + 每项「上移/下移」按钮（键盘可达），落盘只写发生变化的那一组；
   `settings/dock-order.ts` 是纯函数（`moveItem` / `moveItemUp` / `moveItemDown` / `normalizeOrder`）。
   实测拖动「新增」到首位后主 Dock 立即跟随、`localStorage` 同步、恢复默认可用。
4. **导出对话框页范围**改回桌面的**单个自由文本框**（`ExportDialog.cs:41-45`）：始终可见、
   仅 `指定范围` 时可用，解析逻辑照搬 `PageRangeParser.cs`（不用 `Number()`，改用 `^[+-]?\d+$`
   以保持 C# `int.TryParse` 的严格性），主按钮文案 `Common_Next` = 「下一步」，补 `Export_Hint_Text`。

缺陷修复：

5. **取色器把色域/滑条挤到容器外**（本轮最隐蔽的一个）：`@fluentui/react-color-picker` 的
   `HsvColor` 里 `s`/`v` 是 **0~1 的小数**（`useColorArea.js:34-35` 才 `× 100` 转百分比，
   拖动时按 `deltaX / 100` 增量并夹到 `[0,1]`），最初按 0~100 传，导致进度变量变成
   `--fui-AreaX--progress: 980%`、内容被撑到 3633px 宽、色域缩略图定位在 `x = -2643`（整块看不见）。
   改为小数后 `10% / 20%` 正确，`scrollWidth == clientWidth`。
6. `--wb-dock-foreground` 拆成两个 token：浮层文字（桌面显式 `SystemControlForegroundBaseHighBrush`
   = `#000000`）与 Dock **按钮**文字（`DockButtonStyle` → `DefaultButtonStyle` →
   `TextFillColorPrimary` = `#E4000000`，即 89.4% 黑）。实测 Dock 标签 `rgba(0,0,0,0.894)`。
7. 删掉三处死样式：`.wb-flyout-surface`、`.wb-settings-swatches/.wb-settings-swatch/.wb-settings-color-field`
   （外观页不再铺色板）、`.wb-dialog__range`（页范围改单框）、`.wb-settings-body .fui-DialogTitle__action`
   （关闭按钮搬进标题行 flex）。清 `.wb-settings-swatch` 时漏删一行 `outline-offset` 让
   lightningcss 在 `pnpm build` 阶段报 `Invalid token in pseudo element`，已修。
8. 设置搜索的样式 import 原先追加在 `main.tsx` 末尾（子代理为避免并行冲突），改回顶部 import 区。

状态：`pnpm verify` 976 例全绿、`pnpm lint` 0/0、`pnpm build` 781.86 kB（gzip 223.89 kB，取色器带入体积，
Vite 仍报 >500 kB 警告 → P5.2 拆包）。

**仍未做**：组件级 DOM 冒烟测试（`vitest` 的 `test.include` 目前不含 `.tsx`，
要补需引入 jsdom + @testing-library/react，属 P5 范围）；`--wb-dock-foreground` 之外的其它 WinUI
前景色（如 Secondary/Tertiary 文本）尚未逐一对齐。

#### 仍在差异清零（2026-11-01 深夜）

上一节列的三个遗留项已全部处理：

1. **拆包**（`ef1866f`）：Vite 8 已切 Rolldown，`manualChunks` 不再支持，改用
   `build.rolldownOptions.output.codeSplitting.groups`。781.86 kB 单包 →
   业务 107.37(gzip 33.97) + react 218.75(68.27) + fluent 441.56(116.77) + icons 14.24(5.39)，
   >500 kB 提示消失；三方库与业务代码分开缓存。`fflate` 暂不建组（WBIX 读写还没被入口引用，P4 再接）。
2. **组件级冒烟测试**（同提交）：引入 jsdom + @testing-library/react（devDependency），
   `test.include` 加 `.test.tsx`，`tsconfig.test.json` 补 DOM lib 与 `jsx`（默认环境仍是 node，
   组件测试在文件头切 jsdom）。`tests/unit/ui-shell-smoke.test.tsx` 7 例覆盖外壳挂载、
   单页「+」保留、工具二次点击开面板、设置窗口 6 分类、外观页 3 张可点卡片、
   导出对话框能开不抛错（最后一条正是当初整页白屏的回归面）。
   顺带修 `useFullscreen` 把 `undefined` 当"已全屏"导致标题栏误隐的缺陷。
3. **InfoBar 度量**（`dbf489c`）：`chrome.css` 的 `.wb-reminder*` 按 `generic.xaml` 的 InfoBar
   默认模板逐条重写（min-height 48、左内边距 16、图标 16 + `16/14/16` 边距、14px/20px 正文、
   关闭按钮 38×38 + margin 5、圆角 4、1px 描边），四组 severity 配色取主题字典原值。
   又一次踩到 XAML ARGB 的坑：`#80F6F6F6`/`#E4000000`/`#08FFFFFF` 直接进 CSS 会变成青色/不透明红，
   全部改成 `rgb(r g b / a%)`。
4. **次级前景色**：新增 `--wb-text-primary/secondary/tertiary/disabled`（浅色 `#E4000000 / #9E000000 /
   #72000000 / #5C000000`，深色 `#FFFFFF / #C5FFFFFF / #87FFFFFF / #5DFFFFFF`）+
   `--wb-dock-text-primary`（Dock 区强制浅色，**不随主题切换**，否则深色下白字压浅底板），
   `dock.css`/`settings.css`/`dialogs.css` 共 30 余处文字色改引 token。
   判定不按 Fluent 层级机械平移：Fluent 的 `#616161` 等效 62% 黑，正好是 WinUI 的 **Secondary**
   而不是 Tertiary。
5. 顺手修：设置主题只在挂载时读一次 `matchMedia`，系统切深浅色后界面不跟随
   （桌面 WinUI 是实时跟随的）；改为订阅 `change` 事件。

**仍是真差异（桌面行为/资源本身如此，故保留）**：
- 提醒条图标：桌面用 Segoe Fluent Icons 的"实心圆 + 符号"双层字形，网页端无该字体，
  只对齐尺寸与 severity 颜色；信息级图标桌面取用户强调色，网页端用 Fluent 品牌色。
- `InfoBarPanel` 横排/纵排切换阈值在 WinUI 原生代码里（包内读不到），按提醒栈 `MaxWidth=420` 走纵排。
- 桌面 InfoBar 的 `AutomationProperties.IsDialog` / `LandmarkType=Custom` 无障碍语义未复刻。

---

## P4 文件能力

- [x] **P4.1 WBIX 导入流程**：`.wbix` 字节 → 页面快照 → 追加到工作区末尾并切到导入包记录的相对页
  （`features/import/wbix-import.ts` + 真实可用的 `ImportDialog`）。
  桌面唯一活路径是 `ImportFlow.cs:99-101` 的 `AppendAfterLastPage`——"覆盖整个工作区"只存在于
  已下线对话框的 resw（C# 零引用），所以网页端只实现追加，`BoardWorkspace.appendPages()` 保留已有页的撤销栈。
  拖拽 / `launchQueue` 未做（浏览器权限与安装态相关，留待 P5）。
- [x] **P4.2 PNG 导出**：固定画幅（方形/4:3/16:9 × 720/1080/2160）+ 自适应画幅（内容 + 2×padding）、
  DPI 折算与 `maxEdge` 钳制、`pHYs` 块写入、多页进 `WindBoard-{date}` 目录、命名规则对齐桌面
- [x] **P4.3 PDF 导出**：每页一张整页位图（`FlateDecode` RGB 流），最小 PDF 结构
  （catalog/pages/page/content/image + xref），页面尺寸 `px*72/dpi`
- [x] **P4.4 PlatformIO**：FSA 优先（`showSaveFilePicker` / `showDirectoryPicker`）、
  `<a download>` 与 `<input type=file>` 回退、多页 PNG 无目录权限时降级为 ZIP
- [x] **P4.5 自动保存与恢复**：WBIX 字节存 OPFS + 元数据单独成文件，2s 节流、页面隐藏时 flush，
  启动时若有存档弹 `RecoveryDialog`（恢复 = 解析存档替换工作区，等价于导入自己刚存的 WBIX）。
  **注：桌面没有自动保存功能**（只在用户显式保存时写文件），这是网页端增强、不是移植。
- [x] **P4.6 设置导入导出**：`settings-file.ts` 白名单序列化 + 容忍注释/尾逗号的解析（对齐桌面
  `ReadCommentHandling=Skip` + `AllowTrailingCommas`），未知字段忽略、非法值回落默认并给出说明；
  关于页三张卡全部接通（走 `platform/io.ts`）。
- 验收：AC2、AC4、AC5；导出文件能在桌面版/系统阅读器中正常打开

#### P4 记录：实现要点与踩坑（2026-11-02）

- **`OffscreenCanvas` 与 `HTMLCanvasElement` 的取字节方式不同**（`convertToBlob` vs `toBlob`）。
  最初把编码放在工厂里、对目标对象做 `as unknown as` 转型，运行时直接报
  `canvas.toBlob is not a function`；改为让 `RasterRenderTarget` 自带 `encodePng()` / `readRgba()`，
  把两种画布的差异封在创建处。
- **PNG 的 DPI 必须自己写**：`canvas.toBlob('image/png')` 不带分辨率，桌面 WIC 会
  `SetResolution(dpi, dpi)`。补 `pHYs` 块（96 DPI = 3780 像素/米）插在 `IHDR` 之后；
  重复调用要先剔除旧的 `pHYs`，否则会叠加。
- **导出画幅只统计笔迹**（`BoardRasterExporter.TryGetDocumentBounds:222-260`），形状与元素不参与——
  这是桌面既有口径，跟它才能得到同样的裁切。
- **多页 PNG 与单页命名不同**：目录 `WindBoard-{date}`、文件 `{date}-{页码:000}.png`
  （`BoardExportService.cs:126-130`）；单页是 `WindBoard-{date}-{HHmm}.png`。
- 浏览器失败给的是英文 `DOMException`，`platform/io.ts` 按 `name` 归类加了中文前缀
  （实测踩到 `File picker already active.` 直接抛给用户的突兀感）。

浏览器实测（Chromium，注入假 picker 捕获字节）：
单页 PNG `WindBoard-2026-11-02-1238.png` 53522 B（PNG 签名 + IEND，含 1 个 `pHYs`）；
PDF `WindBoard-2026-11-02-1238.pdf` 8254 B（`%PDF-1.4` → `%%EOF`）；
多页 PNG 写进 `WindBoard-2026-11-02/` 的 `2026-11-02-001.png` / `-002.png`；
WBIX `2026-11-02-1238.wbix` 1514 B（zip 头 `PK\x03\x04`，无 `WindBoard-` 前缀）。

P4.5 / P4.6 浏览器实测（2026-11-02）：
- 画一笔后等 2 秒，OPFS 出现 `autosave.wbix`(829 B) + `autosave.meta.json`（1 页 / 1 条目）；
- 刷新页面弹出「发现未完成的编辑（刚刚，1 页、1 个条目）」，点「恢复」后笔迹回来、存档被清除；
- 「关于 → 导出设置」产出 `windboard-settings.json`（general / appearance / dock / writing /
  keyboardShortcuts 五个分区、2 空格缩进，与桌面 `WriteIndented` 一致）。

---

## P5 打磨

- [x] **P5.1 视觉回归**：Playwright 截图基线入库（不下载浏览器，用本机 Chrome）
- [x] **P5.2 性能**：1 万笔迹实测 + 静态层缓存已落地 + 输入延迟实测（见 P5.2 记录）
- [x] **P5.3 触屏**：触屏 e2e 回归（单指画线/双指手势/捏合缩放）+ iOS 画布面积上限防护（真机项记为受限）
- [x] **P5.4 i18n**：`gen-i18n.mjs` + key 集合审计测试 + 全部界面文案迁移（含网页独有词条目录，2026-11-02 收尾）
- [x] **P5.5 PWA**：manifest、图标（192/512）、SW 预缓存与静默更新、`file_handlers`
- 验收：AC8、AC9；Lighthouse PWA 可安装

### P5.4 记录：i18n 基础设施（2026-11-02）

已落地（可验证）：

- **文案表生成**：`web/scripts/gen-i18n.mjs`（`pnpm i18n`）解析
  `WindBoard/Strings/{zh-CN,en-US}/*.resw`（各 18 个文件、512 个 key），产出
  `web/src/i18n/locales/{zh-CN,en-US}.generated.ts`。生成时做两件事的守卫：
  同一 key 跨 resw 取值冲突直接报错；两个语言的 key 集合不一致直接报错。
- **运行时**：`web/src/i18n/index.ts` —— `LOCALES`/`DEFAULT_LOCALE`(zh-CN)、
  `MESSAGES_BY_LOCALE`、`normalizeLocale`（`zh-Hans-CN`→`zh-CN`、`en-GB`→`en-US`）、
  `resolveLocale(preference, systemLanguages)`、`formatMessage`（.NET 风格 `{0}` 占位符）、
  `translate`/`createTranslator`（回落：目标语言 → zh-CN → key 本身）。
- **React 接线**：`web/src/i18n/context.ts`（`createContext` + `useI18n`，**未包 Provider 时
  回落默认语言而不抛错**——语言只是显示层，不该让组件单测或装配顺序调整变成白屏）
  与 `web/src/i18n/I18nProvider.tsx`（读 `settings.general.languagePreference`，`system`
  时按 `navigator.languages` 解析；同步写 `<html lang>`）。`App.tsx` 把 `I18nProvider`
  套在 `AppServicesProvider` 内、`AppShell` 外。
- **迁移示例**：`ui/shell/TitleBar.tsx`（`MainWindow_Title`）、
  `ui/overlay/SelectionActionBar.tsx`（`Common_BringToFront` / `Common_CancelBringToFront` /
  `Common_Copy` / `Common_Delete`）。
- **守卫测试**：`web/tests/unit/i18n.test.ts` 16 例——语言归一/解析/占位符/回落，
  以及**生成物与 resw 的一致性**（重新解析 resw 逐 key 比对；resw 改了却忘了 `pnpm i18n`
  会直接失败）。另断言两语言 key 集合相同、没有空文案。

浏览器实测：把 `settings.general.languagePreference` 设为 `en-US` 后刷新 →
标题变 `WindBoard`、`<html lang>` 变 `en-US`；Dock 文案仍是中文（尚未迁移，符合预期）。

未完成（渐进迁移，按页面推进）：Dock 标签、各弹出面板、设置页、对话框、提醒条的文案
仍是硬编码中文。迁移方式已定型：把该组件的 `TEXTS` 常量换成 `useI18n().t('Key')`，
key 一律取 resw 里的原名，不做新造。

### P5.1 记录：Playwright 视觉回归（2026-11-02）

**为什么不下载浏览器**：`pnpm add -D @playwright/test` 时设
`PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1`，配置里 `channel: 'chrome'` 直接驱动本机已装的 Chrome。
省掉一个 ~130MB 的 Chromium 下载，代价是依赖本机 Chrome 版本（对同一台开发机/同一镜像 CI 够用）。

**被测对象是生产构建**：`webServer` 跑 `pnpm build && pnpm preview --port 4173`。
dev 构建带 StrictMode 双渲染与未压缩代码，基线跟线上不一致没有意义。
固定 `viewport 1200×800`、`deviceScaleFactor: 1`、`colorScheme: 'light'`、`locale: 'zh-CN'`、
`timezoneId: 'Asia/Shanghai'`、`animations: 'disabled'`、`maxDiffPixelRatio: 0.002`。

**8 张基线**（`web/e2e/__screenshots__/visual.spec.ts/`）：空画布、Dock 左段、Dock 中段、
笔迹与四种形状的绘制结果、画笔面板、擦除面板、形状面板、页面管理面板、设置窗口、导出对话框。
两处需要控制时间/选择器的细节：

- 导出对话框的默认文件名带日期，用 `page.clock.setFixedTime` 固定；
- 工具按钮的规则是「未激活时第一次只切工具、第二次才开面板」，而画笔是**默认激活**的，
  所以画笔面板只需点一次；形状按钮是纯图标（只有 `aria-label`），且选中后浮层自动关闭，
  因此每个形状都要重新点开一次浮层。

**踩到的两个坑**：
1. `vite preview` 只监听 `localhost`（IPv6 `::1`），配置写 `127.0.0.1` 会连不上，
   `webServer` 直接 240s 超时；改成 `localhost` 后正常。
2. e2e 需要独立 `tsconfig.e2e.json`（`lib` 加 DOM）：`page.evaluate` 的回调体是在浏览器里跑的，
   但测试文件本身跑在 Node 上。放进 `tsconfig.node.json` 会让 `scripts/` 误用 DOM API。

**稳定性**：连续两次运行基线文件 sha256 完全一致；`pnpm e2e` 约 15s（含一次构建）。

**已知局限**：截图里含中文字形，基线只适合同一开发机/同一镜像 CI；跨机器的字体差异
不在本阶段目标内（要跨平台稳定得把字体打包进页面，属于设计变更）。

**命令**：`pnpm e2e`（比对）、`pnpm e2e:update`（重建基线，基线变更须在提交说明里给理由）；
`pnpm verify:all` = `typecheck + vitest + build + e2e + interop`。

### P5.2 记录：静态层缓存与帧时间实测（2026-11-02）

**测量方法**：浏览器里用注入脚本替换 `requestAnimationFrame`（在 React 挂载前装好，
这样帧调度器抓到的是包装版），记录每次 rAF 回调的实际耗时；数据全部来自
`http://localhost:5173`（dev 构建）+ Chrome MCP，视口 1600×795、DPR 1.5（软件光栅化）。

**基线（未做优化时的问题）**：整帧重绘的成本几乎只跟**采样点数**走，约 0.24µs/点。
1 万条 × 12 点（120k 点，全部落在可见区 → AABB 剔除帮不上忙）时每帧 p50 ≈ 23ms，
已经超出 60fps 预算（16.7ms），书写时肉眼可见掉帧。

**做法**：`web/src/render/static-cache.ts` + `scene.ts` 拆分 + `render-handle.ts` 接线。
把「背景 + 元素 + 文档墨迹」画进离屏画布，每帧只做「1:1 贴图 + 实时预览 + 覆盖层」。
与桌面 `DxSwapChainPanelRenderer` 的静态缓冲同思路；脏矩形优化（`DirtyRectExtraPixels`）
解决的是局部失效，仍是 P5 之后的独立方向。

失效判断用**场景指纹**（`computeSceneSignature`，FNV-1a 混合变换矩阵 / 画布尺寸 / DPR /
背景 / 卡片主题 / 每个条目的类型、端点、点数、颜色、线宽）而不是在几十处命令调用点手工
`invalidate()`：散点式失效最容易漏，漏一次就是画面停在旧内容。指纹是 O(n) 轻量哈希，
1 万条约零点几毫秒，相对省下的十几毫秒可以忽略。

**效果**（同一次会话内 A/B；「缓存未命中帧」= 每帧都平移视图，等于无缓存时的每帧成本）：

| 点数   | 缓存命中（贴图） | 缓存未命中（整帧重绘） |
|--------|------------------|------------------------|
| 12k    | —（低于门槛）    | 1.9 ms                 |
| 30k    | 2.4 ms           | 5.6 ms                 |
| 120k   | 6.7 ms           | 23.1 ms                |

**门槛为什么是 30000 点**：两条曲线相交在约 13k 点；取 30000 是因为**贴图成本随画布
像素数走、重绘成本随点数走**——窗口放大到 4K 时贴图会涨几倍而点数不变。门槛按点数而不是
条目数，因为条目数相同的两页点数可能差两个数量级。

**正确性验证**：
- 单测 `web/tests/unit/render-static-cache.test.ts`（9 例）：低于门槛不缓存、首帧重画后
  只贴图、内容/视图/实时笔迹/背景/DPR 变化对失效的影响、环境不支持离屏画布时退回整帧重绘。
- 浏览器像素级验证：6000 条笔迹下取画布像素，提交新笔迹后同一坐标从背景色 `46,47,51`
  变为笔迹色 `255,26,26` → 静态层确实按内容变化重画了。
- 另修了一处 `tsc -b` 与 `oxlint` 才暴露的问题：`tsconfig.test.json` 缺 `vite/client` 类型
  （测试会 typecheck 到 `src/**` 里的 `import.meta.env`）；`useStaticCache` 被 oxlint
  误判成 React Hook 而改名 `shouldUseStaticCache`。

**输入延迟实测**（2026-11-02 补）：新增 `web/scripts/measure-perf.mjs`（`pnpm perf`），
`@playwright/test` 的 `chromium.launch({ channel: 'chrome' })` 直连本机 Chrome
（`vite` dev server，`__windboardDev` 句柄只在这个模式挂载），视口 1600×795、DPR 1.5。
先按档位灌入笔迹（真实 `PointerEvent` 派发），再按住笔连续移动 80 次，每次测
「派发 pointermove → 该点像素真的变了」的耗时——直接轮询画布像素，
不用 “dispatch → 下一个 rAF” 近似（帧调度器把绘制放在自己的回调里，
拿 rAF 时间戳只能测到调度开销）。

| 采样点 | p50 | p95 | max |
|--------|-----|-----|-----|
| 12k    | 37.6 ms | 44.6 ms | 178.8 ms |
| 30k    | 46.0 ms | 58.0 ms | 810.8 ms |
| 120k   | 55.9 ms | 65.1 ms | 66.0 ms |

读法：
- 数字里含**最多一帧（16.7ms）的轮询粒度偏差**（像素检查发生在 rAF 回调里，
  绘制可能刚好错过这一拍），扣掉后约 21~39ms，即 1~2 帧，属于书写跟手的正常区间；
- 延迟随点数缓增（37→56ms）的原因不是重绘，而是静态层**指纹是 O(n)**（每帧都要扫
  一遍条目算哈希）加上贴图成本随画布像素数走；要做进一步优化的话，方向是把指纹
  的计算摊到“内容真的变了”的时刻，而不是每帧一次；
- p95 偶发的 500ms 级尖峰只出现在一次性灌入上千条笔迹之后（V8 GC + 轮廓缓存懒构建），
  与交互绘制无关：120k 档完全没有这个尖峰。

测量脚本本身做了三处抗噪：每档重开页面（上一档的轮廓缓存与 GC 会污染下一档）、
第一档 6000 点整档丢弃（V8 冷启动编译开销）、每步移到没画过的位置
（保证“像素变了”只可能由这一笔造成）。

触屏与 iOS 尺寸限制见 P5.3。

### P5.4 记录（二）：界面文案迁移（2026-11-02）

第一批（标题栏、选择操作条）之后，本轮把**面向用户的固定文案**整体迁到 resw 词表：
23 个组件、约 130 处调用点，key 一律取桌面 resw 原名，`t('Key')` / `t('Key', 参数)`。

| 区域 | 文件 | 处数 |
|------|------|------|
| 外壳与浮层 | `ui/shell/{DockBar,ReminderStack,BoardSurface}.tsx`、`ui/settings/SettingsSearchBox.tsx`、`ui/overlay/ShapePropertiesPanel.tsx` | 23 |
| 对话框 | `ui/dialogs/{ExportImport,ImportDialog,CanvasColorDialog,ElementCardThemeDialog,ConfirmDialog,SettingsWindow}.tsx` | 约 73 |
| 浮层 | `ui/flyouts/{Pen,Eraser,Shape,Pages}Flyout.tsx` | 14 |
| 设置页 | `settings/pages/{General,Appearance,About,Shortcuts,Writing,Dock}Page.tsx` | 78 |

**写法上的两条约定**（避免在模块顶层调用 `t`、避免文案与 key 漂移）：

- 「选项数组 / 映射表」这类模块级数据不再存中文，改存 `MessageKey`（如
  `Choice.labelKey`、`SHAPE_ITEMS.labelKey`、`DOCK_ITEM_META.labelKey`、
  `FORMAT_LABEL_KEYS`），渲染处 `t(...)`；
- 需要参数的地方用词表里的 `*_Fmt`（占位符 `{0}`/`{1}`），例如
  `Export_PngSizeItem_Fmt`、`Settings_Shortcuts_Error_Conflict_Fmt`、
  `Settings_About_SettingsManagement_Exported_Fmt`。

**迁移后可见文案的唯一变化**：语言下拉的「简体中文」→「中文」，因为桌面
`Settings_General_Language_Chinese` 的值就是「中文」（以桌面原文为准）。
另外快捷键的非法输入提示改用桌面 `Settings_Shortcuts_Error_Invalid`
（语义等价、措辞更短）。

**仍然保留中文的部分**（都记了原因，不是漏改）：

1. **网页端独有文案**：`RecoveryDialog` 整个自动保存恢复流程（桌面无此功能）、
   `BoardSurface` 的三条“用 WindBoard 打开文件”提醒、提醒条里的“浏览器不支持最小化窗口”、
   设置页里解释浏览器能力边界的长句（Dock 页“网页端 v1 暂未渲染快捷入口…”等）。
2. **resw 没有对应 key 的可访问性文案**：取色器的饱和度/明度/色相 `aria-label`、
   设置窗口的「关闭设置」「设置分类」`aria-label`、页面管理面板的「删除该页」、
   搜索框的「搜索建议」「没有匹配的设置项」、色板序号类 `aria-label`
   （保留序号而不是复用 `Settings_Pen_ClearSwatch_Button`，否则多个按钮同名，读屏更难用）。
3. **写域外的纯数据模块**（下一轮可接）：
   - `settings/search-targets.ts` 的 `cardTitle`（搜索建议里显示的卡片名）；
   - `settings/window-model.ts` 的 6 个分类名与描述（导航项）；对应 key
     `SettingsWindow_Nav_General/_Appearance/_Writing/_Shortcuts/_About` 已存在，
     `dock` 页桌面没有、保持 "Dock"；
   - `features/export/page-range.ts` 的 `PAGE_RANGE_MESSAGES` 与
     `features/export/export-options.ts` 的 7 条 `issues.push('中文…')`：
     `Export_PageRange_*` 与 `Export_NoPages_Message` 等 key 已存在，注册表里
     注释已写好对应关系，但这两个模块是纯函数（返回 string 供 UI 直接显示），
     要改成返回 key 才能翻译，属于接口调整。

**验证**：`tsc -b` 0 错误、`vitest` 57 文件 / 1406 例全绿、`oxlint` 0/0（190 文件）、
`playwright test` 11 例全绿（8 张视觉基线未变，说明这次只换了文案来源、没改布局）。

### P5.4 记录（三）：i18n 收尾 —— 网页独有词条与剩余界面文案（2026-11-02）

上一轮留下的三类“未接”里，本轮把前两类接完（第三类见文末的明确例外）。

**1. 新增网页独有词条目录 `web/src/i18n/web-strings/`**

有一批文案只描述浏览器里的东西（自动保存恢复、Service Worker、把设置导出成 JSON、
“浏览器不支持最小化窗口”等），桌面永远用不到。塞进 `WindBoard/Strings/*.resw` 会让桌面
背一批死 key，还会干扰桌面侧的 key 审计（那里要求 key 必须被 C# 引用）。

做法：目录下按区域拆多个 JSON（`shell.json` / `settings.json` / `dialogs.json` /
`export.json` / `runtime.json`），每份形如
`{ "zh-CN": { "Web_X": "…" }, "en-US": { "Web_X": "…" } }`；**key 一律 `Web_` 前缀**，
一眼能看出不是桌面词条。`gen-i18n.mjs` 读全部 `*.json` 合并，校验前缀、同文件内不重复、
**与 resw key 撞名直接抛错**。最终 621 key（resw 512 + 网页独有 109），两语言集合一致。

**2. 纯函数返回 key 而不是中文：新增 `web/src/i18n/message.ts`**

`features/export/page-range.ts`、`export-options.ts` 这类纯逻辑原来返回中文串，
等于把语言钉死在逻辑层。改为返回

```ts
interface LocalizedMessage { key: MessageKey; args?: readonly (string | number)[] }
```

由 UI 侧 `formatLocalized(t, message)` 渲染。`localized()` 不传实参时省略 `args` 字段，
方便测试用 `toEqual` 断言。`MessageTranslator` 刻意不用 React 侧的 `Translator` 类型，
避免把 React 拖进纯逻辑的依赖图。

`page-range.ts` 的报错优先级与 C# 分支顺序一字未改，只是把中文换成了 key。

**3. 剩下的界面文案**

- 导出链路：对话框的校验问题、动态提示（页数/尺寸/多页目录/WBIX 全工作区）、范围框标签；
- 外壳：更多菜单（`MoreMenuEntry.label` → `labelKey`，删掉 `DockBar` 里按 id 覆盖文案的
  `menuLabel()`）、新增页标题、选中项操作条的无障碍标签；
- 浮层：形状属性字段（`ShapePropertyField.label` → `labelKey`，删掉
  `ShapePropertiesPanel` 里“中文原文 → key”的映射表）；
- 元素双击打开：`element-open.ts` 的错误文案改成 `LocalizedMessage`，删除 `formatOpenText()`；
  控制器新增 `translate` 选项，外壳用读取 ref 的包装函数注入，**切语言后下一次提醒就是新语言**；
- 画布上的元素卡：`element-card.ts` 的 8 条文案 + 16 种扩展名改 `MessageKey`
  （`ElementCard.resw` 的 19 个 key 已够用，新增 0 个 key），`RenderSources.t` 透传；
  导出图里的卡片文字同样跟随语言（`ExportRunnerDeps.t`）。
- 语言切换后画布要重画：`BoardSurface` 里 `useEffect(() => controller?.requestRender(), [controller, t])`，
  否则元素卡文案要等下一次交互才更新。

**明确不接的部分（都是开发者诊断，不进界面）**：`persistence/wbix/*` 的解析错误文本、
`app/hooks.ts` 的 provider 不变量异常、`board/workspace.ts` 的 `RangeError`、
`platform/{pwa,file-handlers}.ts` 与 `ui/shell/BoardSurface.tsx` 的 `console.warn`。
桌面端对应的异常文本同样不本地化，保持一致。

**验证**：`tsc -b` 0 错误、`vitest` 57 文件 / 1409 例全绿、`oxlint` 0/0（192 文件）、
`playwright` 11 例全绿（`settings-general` 基线按新提示文案重建，其余 10 张未变）、
浏览器实测 en-US：标题 `WindBoard`、Dock 十个标签、更多菜单三项、设置导航六项、
常规页卡片与提示、画笔面板「Pen settings / Thickness」、擦除面板「Point / Stroke / Slide to clear」、
导出对话框全部字段与主按钮「Next」均为英文，`<html lang>` = `en-US`。

### P5.3 记录：触屏回归与 iOS 画布上限（2026-11-02）

**桌面事实先对齐**（避免“优化”出不存在的功能）：

- 桌面 `WindBoard/` 全仓 grep `palm`/掌拒 **零命中**：桌面版没有掌拒逻辑，只有按
  `PointerDeviceType.Touch` 分流（`BoardInputController.Pointer.cs:28-37`）+ 双指计数
  ≥2 才进入视口操作（`BoardInputController.Manipulation.cs:211-252`）。
- **已知的有意偏差**：网页端在 `web/src/interaction/pointer-router.ts:263` 加了一条
  “笔按下期间忽略触摸”（`ctx.penActive`）的护栏，代码注释里已标明是「浏览器补充（桌面无对应分支）」。
  保留理由：移动端手掌压屏的代价是不画线也会起笔，比桌面端（键盘/鼠标为主）高得多；
  若将来要求逐字节等价桌面行为，删掉这一个 `if` 即可，不影响其它路径。
- 压感归一化：非笔恒 1.0、笔 clamp(0.1,1)，网页端 `normalizePressure` 已有单测。

**触屏 e2e**（`web/e2e/touch.spec.ts`，`test.use({ hasTouch: true })`）：
用 CDP `Input.dispatchTouchEvent` 派发**真实触摸**，让浏览器自己合成 `pointerType === 'touch'`
的指针事件，因此覆盖的是完整链路（`touch-action: none`、指针捕获、双指门禁），而不是
手工构造 `PointerEvent` 绕过一半逻辑。三个用例：

1. **单指触摸可以画出一笔** —— 24 次 `touchMove` 后统计画布像素（与左上角背景色比较），
   墨迹像素 > 200、包围盒宽度在 300~700 DIP 之间，且「撤销」按钮变可用（进了撤销栈）。
2. **第二根手指按下会结束单指笔画并转入平移** —— 单指先画 2 个点，第二指落下后双指同向
   移动 180 DIP，断言内容包围盒中心右移 140~230 DIP。这条锁的正是 P1 那个
   “先把触摸来源的单指操作结束掉、再求值手势门禁”的时序。
3. **双指捏合可以缩放画布** —— 先画一条 400 DIP 长的横线当量尺，双指从相距 200 张开到
   300（缩放 1.5 倍），断言包围盒宽度比在 1.3~1.75 之间。

**iOS 画布面积上限**（`web/src/render/render-handle.ts`）：WebKit 把单张 canvas 的像素总数
限制在 16 777 216（4096×4096），**超限不报错、直接空白**。新增
`MAX_CANVAS_AREA_PIXELS` + `resolveCanvasDevicePixelRatio(reported, sizeDip)`：
只在 iOS（`isLikelyIos()`：UA 含 iPhone/iPad/iPod，或 iPadOS 13+ 的 `MacIntel` + 触摸点数 > 1）
且真的超限时，按面积反算一个安全 DPR；其它平台当时沿用原规则（`MAX_DEVICE_PIXEL_RATIO = 2`）。
> 2026-11-04 更新：那个固定上限已删除（它正是高 DPI 屏上笔迹发糊的根因），现按后备存储的
> 单边与面积反算，见「P7 笔迹锐利度」记录。
刻意不把「夹取」做成无条件行为——否则会把浏览器给的小于 1 的 DPR 抬回 1，反而更糊。
单测 `web/tests/unit/render-canvas-limit.test.ts`（10 例）覆盖：非 iOS 不受影响、iOS 未超限
保持原值、超限按面积反算、极端尺寸停在 1、尺寸未知不夹取、无 `navigator` 返回 false。

**受限项（真机不可得，如实记录）**：
- 没有 iPad/Apple Pencil，**压感曲线与笔锋在 iOS Safari 上的实际表现未实测**；代码层面
  只保证了「非笔恒 1.0、笔 clamp(0.1,1)」与桌面一致。
- 掌拒按上面的事实**本就不该有**，不算缺口。
- iOS 上限的具体数值随 WebKit 版本变动（历史上 4096×4096 之外还有总内存限制），
  这里按公开的 16 777 216 像素做保守防护，真机回归留给有条件时补。

### P5.5 记录：PWA 落地（2026-11-02）

实现要点：

- **图标自绘**：新增 `web/scripts/generate-icons.mjs`（`pnpm icons`），零依赖——解析式圆角矩形
  覆盖度 + 沿贝塞尔曲线盖圆盘求线宽覆盖度，最后用 Node 内置 `zlib.deflateSync` 编 PNG
  （自带 CRC32 与 chunk 拼装）。产出 `public/{icon-192,icon-512,icon-maskable-512,apple-touch-icon}.png`。
  不引 `canvas`/`sharp` 是因为图形只有「圆角方块 + 一笔曲线」，为 4 张图装原生依赖不值得；
  图标本身是产物，入库后平时无需重生成。
- **manifest**：`web/public/manifest.webmanifest`，`display: standalone` + `display_override`
  （`window-controls-overlay` 优先，桌面安装态更像原生窗口），`background_color` 取画布默认
  `#2E2F33`、`theme_color` 取品牌蓝 `#1976D2`，`icons` 三档含 `purpose: maskable`，
  `file_handlers` 声明 `.wbix`，`launch_handler.client_mode = ['focus-existing','auto']`
  （已开窗口时聚焦而非新开）。
- **Service Worker**：`web/public/sw.js`，手写不引 `vite-plugin-pwa`。策略是
  「导航请求网络优先 + 静态资源缓存优先」：构建产物文件名带内容哈希，缓存 HTML 后按需缓存即可，
  因此 SW 里不需要维护一份会过期的资源清单（这是不引插件的主要理由）。
  新版本**不自动接管**（`skipWaiting` 只在收到 `SKIP_WAITING` 消息时调用），
  因为「后台换掉正在书写中的页面」比「晚一个版本」危险得多。
- **注册**：`web/src/platform/pwa.ts`，只在 `import.meta.env.PROD` 且支持 SW 时注册
  （dev 下注册会让热更新失效）；页面重新可见时主动 `registration.update()`。
- **`file_handlers` 接线**：`web/src/platform/file-handlers.ts` —— `attachLaunchQueue()` 在
  `main.tsx` 渲染前调用，读 `window.launchQueue` 的文件字节；用「队列 + 消费者」两段式结构
  处理启动顺序（文件可能比 React 挂载先到）。`BoardSurface` 注册消费者，复用
  `applyImportedWorkspace`（与导入对话框同一语义：追加到最后一页之后），成功/失败各发一条提醒。
- **`index.html`**：补 manifest/apple-touch-icon/apple-mobile-web-app-* 三条 iOS 私有 meta、
  `theme-color`、`color-scheme`；标题由过时的 `WindBoard Web` 改回桌面一致的「轻风白板」
  （`WindBoard/Strings/zh-CN/MainWindow.resw` 的 `MainWindow_Title`）。
- **`.gitattributes`**：补 `web/**/*.png binary`。`web/** text eol=lf` 会覆盖前面的
  `*.png binary`（同名属性后出现者优先），不钉一次就可能把 PNG 当文本转换行尾而损坏。

验证：

- 单元测试 `web/tests/unit/file-handlers.test.ts` 7 例（无 API 时不抛、字节读取、先到先排队、
  多文件顺序、单文件失败不影响其余、清除消费者后回队列、空列表不触发）。
- 生产构建 + `vite preview` 实测：manifest 可解析（name/display/3 icons/1 file_handler），
  三张图标都能 `createImageBitmap` 解码且尺寸为 192/512/512，SW `activated` 且页面
  `controller` 非空。
- **离线实测**：停掉 preview 服务后刷新页面 → 标题、10 个 Dock 按钮、画布均正常渲染，
  `navigation.transferSize === 0`（来自 SW 缓存）；此时再 `fetch('/sw.js', {cache:'no-store'})`
  失败，证明断网确实生效、页面确实由缓存提供。
- 回归：`npx tsc -b` 0 错误、`vitest` 54 文件 / 1371 例全绿、`oxlint src tests scripts` 0/0。
- 未覆盖：Lighthouse 可安装性评分、真实"安装到桌面后双击 `.wbix` 打开"（需要安装态，
  Chrome MCP 无法模拟），这两项留作人工验收。

---

## P6 双端一致性守卫与 CI（并入 develop 前）

P0~P5 完成后，`docs/dev/guides/web-port.zh-CN.md` 的 §7.1 列出了一批「改了桌面不会有任何
测试变红」的缺口。P6 把其中两类最容易漂移的契约机械化，并补上网页端的 push/PR 闸门。

### P6.1 设置默认值共享语料

两端默认值此前各写各的单测，没有共享事实源（不像 WBIX 有 golden 语料）。

- 语料 `WindBoard.Tests/Fixtures/settings/defaults.json`（LF、2 空格缩进）只包含两端共有的
  子集：`general` / `appearance` / `dock`（四组顺序 + 两个开关）/ `writing.pen` /
  `keyboardShortcuts`。桌面独有的 `camouflage`、`updates`、`diagnostics`、`announcements`
  不入语料——网页端不实现这些能力，放进去只会让语料变成"桌面完整设置"的假承诺。
- 桌面守护测试 `WindBoard.Tests/Settings/SettingsDefaultsGoldenTests.cs`：把 `new AppSettings()`
  用 `JsonObject` 显式投影成语料形态（固定键序），与语料做 `JsonNode.DeepEquals` 比对；
  `WINBOARD_REGEN_SETTINGS_GOLDEN=1` 重建，写入时补尾随换行。
- 网页端守护 `web/tests/conformance/settings-defaults.test.ts`（4 例）：语料存在、
  `DEFAULT_SETTINGS` 与语料逐字段一致、`normalizeSettings({})` 回落一致、
  顶层与 `dock`/`keyboardShortcuts` 键集合同构。
- 负向验证：改语料 `#2E2F33` → `#101010`，网页端红；改 `PenSettingsDefaults.cs` 色板
  `#FF3B30` → `#FF3B31`，桌面红。两边都验证后已还原。

### P6.2 XAML → CSS 令牌校验

`styles/tokens.css` 的每个变量原本只**注释**了桌面出处，没有校验。

- `web/tests/conformance/desktop-tokens.test.ts`（25 例）**直接读桌面 XAML 源码**（不复制语料），
  用规则表抽取 `MainWindow.xaml` / `App.xaml` / `BoardCanvasControl.xaml` 的数值与颜色，
  与 `styles/*.css` 的令牌比对：`LENGTH_RULES` 20 条（Dock 底板圆角/内边距/间距、按钮
  60×52 圆角 10、分隔线 30、页码按钮 52、指示器 84、页面面板 280 与条目 240、缩略图 104、
  删除按钮 24、选择手柄 14、提醒栈 420 等）+ `COLOR_RULES` 4 条（hover 7.8% 白、
  pressed 13.3% 白、checked `#1976D2`、手柄白）。
- 颜色比对走 `parseXamlColor()` 按 **ARGB** 解析、`parseCssColor()` 按 CSS 语义解析，
  再比通道（alpha 容差 1/255）——把「XAML 8 位色是 ARGB、CSS 是 RGBA」这个已经踩过两次的
  坑变成用例。
- 锚点缺失即抛错，XAML 结构变化会立刻暴露；踩过的坑：`App.xaml`/`BoardCanvasControl.xaml`
  的样式用 `<Setter Property="X" Value="N" />` 语法，不是属性语法，首版两条规则因此取错值。
- 负向验证：`DockButtonStyle` 的 `CornerRadius` 10 → 12，测试红，已还原。
- 顺带删除 `tokens.css` 里零引用且语义错标的 `--wb-pages-flyout-row-height`（240 是条目宽度，
  已在 `dock.css` 有 `--wb-pages-flyout-row-width`）。

### P6.3 网页端 CI

`.github/workflows/web.yml`：

- `verify`（ubuntu-latest，工作目录 `web`，20 分钟上限）：checkout → pnpm 12.14.0 →
  node 22（cache pnpm）→ `pnpm install --frozen-lockfile` → `lint` → `typecheck` → `test`
  → `build` → `pnpm i18n` 后检查 `src/i18n/locales` 无漂移。触发路径含 `WindBoard/**`、
  `WindBoard.Tests/**`、`.gitattributes`，因为 P6.1/P6.2 的一致性测试直接读桌面文件。
- `interop`（windows-latest，仅 `workflow_dispatch` 勾选）：额外装 .NET 10 跑 `pnpm interop`。
  **默认不进 PR 闸门**：它要 `dotnet build WindBoard.slnx -p:Platform=x64`（WinUI 解决方案），
  在 GHA 上未经验证，自动跑容易产生与代码无关的红。
- `pnpm e2e` 也不进 CI：视觉基线含中文字形，换机器即不可比，仍是本地闸门。
- pnpm 版本钉在 workflow 的 `version:` 而不是 `package.json` 的 `packageManager` 字段——
  后者会让 pnpm 12 把自身写进 `pnpm-lock.yaml` 的 `packageManagerDependencies`，凭空多出一整段
  `importers`/`packages`，锁文件变脏。

### 门禁结果（并入 develop 前实测）

- 网页端：`pnpm lint` 0/0（194 文件）、`pnpm verify` **59 文件 / 1438 例** + 构建通过、
  `pnpm e2e` 11 例全绿（3 触屏 + 8 视觉，10 张基线）、`pnpm interop` A~E 五步全绿。
- 桌面侧：`dotnet build WindBoard.slnx -c Release -p:Platform=x64 -p:CodeAnalysisTreatWarningsAsErrors=true`
  0 警告 0 错误；`dotnet test WindBoard.slnx` **650 通过 / 1 跳过**（新增的默认值语料测试 +1）。

## P7 笔迹锐利度：DPR 上限与轮廓容差（2026-11-04）

用户反馈：**「网页版的笔迹相较桌面端更模糊且锯齿严重（尤其是放大后）」**。

### 取证（先量化，再动手）

- **桌面参照**：用既有离屏 harness 渲染**同一几何**（桌面真源是 `ID2D1Ink` + `DrawInk`，
  由 D2D 在设备分辨率上重新细分），临时测试输出到 `WindBoard.Tests/artifacts/aa-dump/`
  （该目录被 gitignore，测试文件用完即删，不入库）。
- **测量口径**：把像素值换算成覆盖度 `c = (v - 46) / 209`，在**像素中心**（`index + 0.5`）上
  线性插值求 50% 覆盖点。第一版把像素索引当连续坐标，残差虚高约 3 倍、读数不可用——
  记在这里，因为"边缘在哪"的全部结论都建立在口径正确之上。
- **结论一**：DPR ≤ 2 时网页与桌面**几乎逐像素一致**。圆弧（r=10、baseSize=3）在 zoom 8 下
  `meanAbsDiff 0.025`、差值 > 16 的像素占比 `0.07%`、> 64 的一个也没有；细笔迹（3 DIP 折线）
  网页端边缘略软 7~10%、总墨量少 3~5%，并排 6 倍放大肉眼无差别。
  → 「轮廓折线化导致锯齿」**被否证**：相对理想圆的折线误差在 DPR1 是 max 0.207 / median 0.069
  设备像素，DPR1.5 是 max 0.434，都在亚像素级。
- **结论二（真因）**：`MAX_DEVICE_PIXEL_RATIO = 2` 把 DPR 2.5 / 3 的屏幕夹到 2 倍后备存储，
  浏览器再把整块画布放大 1.25 / 1.5 倍，**边缘过渡从 1 设备像素变成 2 像素**——这才是"糊"。
  同一脚本实测 CSS 900×587 的画布：修复前后备存储 1800×1174（= CSS × 2），修复后 2250×1468
  （= CSS × 2.5）；DPR 1/1.5/2/2.5/3 的边缘过渡宽度修复前是 1/1/1/2/2 像素，修复后全是 1。
- **结论三（顺带）**：轮廓细分容差只按 `zoom` 算、不按 DPR，DPR 3 时折线误差会被放大到
  0.75 设备像素（分桶在桶顶最差），必须一起改。

### 改动

- `web/src/render/render-handle.ts`：删除 `MAX_DEVICE_PIXEL_RATIO`，改为**只在真的越过浏览器
  上限时才夹取**——`MAX_CANVAS_BACKING_DIMENSION = 16 384`（Canvas2D 单边上限，超限整块画布不显示）
  与 `MAX_CANVAS_BACKING_AREA_PIXELS = 67 108 864`（8192²，RGBA 约 256 MiB）。`resolveDevicePixelRatio`
  只做非法值回退 1；`resolveCanvasDevicePixelRatio` 用
  `min(sqrt(预算 / 面积), 边长上限 / 最长边)` 得 `limitDpr`，只有 `limitDpr < devicePixelRatio` 时才降级，
  **且绝不把 < 1 的 DPR 抬高**（抬高反而更糊）。
- `web/src/ink/outline.ts`：新增 `outlineScale(zoom, devicePixelRatio)`，容差与分桶都改用
  `zoom × DPR` 的**乘积**（容差只取决于乘积，所以同桶内换 DPR 仍可复用缓存，不必存 dpr 字段）。
- `web/src/render/scene.ts`：`drawInkItem` / `drawStroke` 增 `devicePixelRatio` 形参，
  `drawSceneBase` 与 `drawLivePreview` 都传 `surface.devicePixelRatio`。
- `web/src/ui/shell/PageThumbnail.tsx`：缩略图同样走 `resolveCanvasDevicePixelRatio`。

### 代价与取舍

- 后备存储随 DPR 线性增长（内存与填充率）。DPR 3 + 1600×900 视口、24k 点实测整帧
  p50 6.9ms / p90 10.4ms（DPR1 为 3.3ms），仍在 60fps 预算内；4K 屏 DPR 2 时面积预算
  67M 像素（约 268 MiB）会先触发，按面积反算降级——这是有意的上限，不是缺陷。
- **不做无条件夹取**：宁可多吃内存，也不让浏览器去放大一张小画布。

### 验证

- `npx tsc -b` 0 错误、`npx oxlint src tests` 0/0、
  `pnpm verify` 59 文件 / 1442 例、`pnpm e2e` 11 例、`pnpm interop` A~E 全绿。
- 浏览器复测 DPR 1 / 1.5 / 2 / 2.5 / 3 的边缘过渡宽度全为 1 设备像素；
  压感笔迹在 DPR 3 + zoom 10 下目视平滑。
- 单测锁定：`render-handle.test.ts` 的「上限为 2」改为「DPR 3 按 3 倍后备存储（2400×1800）」；
  `render-canvas-limit.test.ts` 拆成 DPR3 原样、非 iOS 只受单边/面积约束、单边超限按边长反算三条；
  `ink-outline.test.ts` 新增「容差按设备像素」「分桶用 zoom×DPR 乘积」两条。

### 仍未覆盖

- 没有 DPR > 2 的实机（含手机 PWA）人工确认，只有 Playwright 的 DPR 仿真。
- 两端仍**没有自动的像素级交叉比对**：桌面 D2D 与网页 Canvas2D 的抗锯齿策略不同，
  改几何或改后备存储策略都必须人工按 `docs/dev/guides/ink-rendering.zh-CN.md` 复核。

### 第二轮：小数缩放下的整设备像素对齐（2026-11-04 晚补）

用户确认「看到问题时的 Windows 显示缩放是 100%~150%」——DPR 上限修复（只影响 >200%）
**解释不了他的观感**，于是继续查小数缩放路径，最后定位到两处独立的重采样。

**取证手段（真机，非 Playwright 截图）**：用 `chrome --force-device-scale-factor=1.5/1.25`
起真实窗口，CDP 注入同一支笔迹，再用 PowerShell `CopyFromScreen` 抓**真实屏幕**，
在物理像素上量笔迹上边缘的过渡宽度（`strong` = 值落在 90~210 的行数，1 行为清晰）。

| 场景 | runMean | strongMean |
|---|---|---|
| DSF 1（基线） | 1.039 | 0.675 |
| DSF 1.5，修复前 | 1.894 | 1.141 |
| DSF 1.5，修复后 | **1.165** | **0.694** |
| DSF 1.25，修复后 | 1.052 | 0.649 |

**真因（两条叠加）**：

1. `syncSurface` 用 `clientWidth/clientHeight`（**四舍五入后的整数**）算后备存储。DSF 1.5 下
   画布布局盒是 1086×710.667，`clientHeight` 报 711 → 后备存储 1067 行，而实际设备高度是
   `710.667 × 1.5 = 1066` 行。差 1 行 = 整张图缩放 0.094%，相位沿纵向漂移，中段变成
   2 像素过渡。DSF 1 下 CSS 尺寸正好整数，所以从没暴露。
2. 画布原点落在半设备像素上时，Chromium 会把整张合成层双线性重采样（
   <https://issues.chromium.org/40431598>）。裸 canvas 扫描证实：DPR 1.5、`top` 为整数设备像素
   （0 / 34 / 36 → 设备 0 / 51 / 54）时过渡 1 行，`top: 33`（设备 49.5）时 2 行；`<img>` 同样如此，
   与 canvas 无关。

**改动**：

- `web/src/render/render-handle.ts`：新增 `computeDeviceGridAlignment(layout, dpr)` 与
  `snapToDevicePixel(offsetDip, dpr)`。`syncSurface` 改用 `getBoundingClientRect()` 的小数尺寸，
  算整数设备像素 → 写后备存储 → 把 CSS 尺寸钉成 `设备像素 / DPR` → 用 `translate` 把原点补到整设备像素。
  用 `round` 而不是 `ceil`：写回后下一帧会重新读到这个值，`ceil` 在"乘积刚好略大于整数"时会每帧
  加 1 个设备像素（实测 1066.00003 → 1067 → 1068），`round` 是幂等的。
  已写入的位移会从下一帧的 `getBoundingClientRect()` 里减掉，避免把位移当成布局变化反复横跳。
- `web/src/render/render-handle.ts`：`ResizeObserver` 改为观察**父元素**——画布尺寸已被钉成具体
  像素，不再跟随 `width: 100%` 伸缩，观察自己收不到窗口变化。
- `web/src/styles/shell.css`：`.wb-surface` 加 `overflow: hidden`（裁掉不到 1px 的溢出）。
- `web/src/ui/shell/BoardSurface.tsx`：`.wb-surface` 内联画布背景色，少掉半个像素的缝露出的是
  同色背景，不会出现异色细线。
- 新增 `web/tests/unit/render-device-grid.test.ts`（13 例）：位移补偿、多 DPR 下的尺寸换算、
  **换算幂等性**、小数布局盒下的内联样式、连续多帧稳定、视口尺寸取对齐后的 CSS 尺寸、未布局时保留旧尺寸。

**仍未覆盖**：只实测了 Windows Chrome；Firefox / Safari 的合成器行为未验证（代码路径与浏览器无关，
对齐规则同样适用，但"是否真会重采样"没有实测）；iOS 真机仍缺。

---

## P8 大量笔迹性能复测 + 手机端适配（2026-11-11）

用户指令（原文）："那先搁置，然后确认：性能优化情况（大量笔迹下的表现）、手机端适配（从此处开始使用playwright-cli进行调试）"
——笔迹锯齿议题搁置，本节只记录这两项。

### 性能复测：瓶颈在"整帧重绘"，不在静止

方法：`playwright-cli` 开 headless Chrome，替换 `requestAnimationFrame` 记录回调耗时，每帧先 `controller.requestRender()`
再等下一帧。视口 1280×687、DPR 1、本机软件光栅化（rAF 上限 ~7.8ms ≈ 128Hz）。

| 笔迹 / 点数 | 平移（p50 / p90 / max，>33ms 帧数） | 静止 |
|---|---|---|
| 1000 / 12k | 7.7 / 8.2 / 8.7，0/89 | 7.8 |
| 5000 / 60k | 8.1 / 16.0 / 23.4，0/89 | 7.8 |
| 10000 / 120k | **30.9 / 45.9 / 54.2，32/89** | 8.0（p99 30.8） |
| 20000 / 240k | **70.6 / 78.7 / 538.6，88/89** | 8.0（p99 55.5） |

- 结论一：**静止不动时静态层缓存生效**，1 万笔迹仍在 8ms/帧（≈125fps）；这也验证了 P5.2 的门槛选择。
- 结论二：**平移/缩放每帧整帧重绘**是瓶颈（10k ≈ 31ms/帧 ≈ 32fps，20k ≈ 71ms/帧 ≈ 14fps）。
  根因在 `render/static-cache.ts` 的 `computeSceneSignature` 把变换矩阵混进了指纹——视图一变就失效，
  而平移恰恰是"内容没变、只有变换变"的场景。
- 结论三：在 1 万笔迹之上实时书写时 mean 15.5ms / p90 30.2ms，书写本身不受影响（只走 `drawLivePreview`）。
- 未做优化（记入待办）：把指纹拆成"内容指纹"与"视图变换"，命中内容指纹时用 `drawImage` 的 `setTransform`
  复用静态层贴图。移动端小视口 AABB 剔除后可见笔迹少，实测 20k 平移 mean 24.8ms，不是同一基准，不能拿来当"手机更快"。

### 手机端：三个必须修的问题（实测）

`playwright-cli open --mobile`（Pixel 10，360×732，DPR 3）下逐项取证：

1. **Dock 三段互相压叠、按钮点不到**。`click` 直接报 `... from .wb-dock-layer__center subtree intercepts pointer events`。
   三段实测宽度：左 198（更多/最小化/导入）、中 395（四工具 + 撤销/重做）、右 62（单页时只剩"+"）。
   360 宽时中段 x=-17 压住左段；**800px 的桌面窗口同样压叠**（左段到 212，中段起于 203）。
2. **设置页卡片坍缩**。360 下 `.wb-settings-card__text` 宽度为 **0**，标题竖排成一字一行，控件列 250 溢出。
3. **无安全区处理**。全仓没有 `env(safe-area-inset-*)`，`viewport` 也没有 `viewport-fit=cover`。

顺带确认：触屏书写本身可用（派发 `pointerType:'touch'` 的 PointerEvent → 页面上出现 1 条笔迹）；
导出对话框（312×726）与画笔浮层（190×315）在 360 下都不溢出。

### 改动

- `ui/shell/use-narrow-layout.ts`（新增）：`useSyncExternalStore` + `matchMedia('(max-width: 700px)')`。
  用 `useSyncExternalStore` 而不是 `useState`+`useEffect`，既避开 oxlint 的 `react(set-state-in-effect)`，
  也少一帧闪跳（先渲染宽屏再切窄屏）。
- `ui/shell/dock-layout.ts`：`DockPlacement` 增加 `'narrow'`。
- `ui/shell/DockBar.tsx`：把 `CenterDock` 拆成 `ToolButtons` / `UndoRedoButtons` 两个片段供复用；
  新增 `NarrowDock`（四工具 + 分隔线 + 撤销/重做 + 「更多」）与 `NarrowMoreButton`
  （受控 `Popover`，内含页面行、动作网格、可展开的内嵌 `PagesFlyout`）。撤销/重做补 `title`，
  因为窄屏隐藏了文字标签，无障碍名不能丢。
- `styles/shell.css`：`html, body { overflow: hidden }`（理由见下）、标题栏/Dock 层/提醒栈加安全区、
  `@media (max-width: 900px) and (min-width: 701px)` 把中段从绝对居中改成不脱流的居中、
  `@media (max-width: 700px)` 压小 Dock 令牌并隐藏按钮文字。
- `styles/dock.css`：`.wb-sheet*` 浮层样式（固定 288 宽、48×48 触摸目标、内嵌面板限高 40vh）。
- `styles/settings.css`：窄屏卡片改纵向堆叠（标题在上、控件占满宽）。
- `index.html`：`viewport-fit=cover`。
- `web/tests/unit/ui-narrow-shell.test.tsx`（新增 4 例）：装 `matchMedia` 桩，锁住窄屏分支的按钮集合与浮层入口。

### 踩坑：布局视口会被撑宽且不收回

内嵌页面管理面板展开后，`document.documentElement.scrollWidth` 从 360 变成 **427**，`innerWidth` 同步变 427，
`position: fixed` 的外壳与 Dock 一起被推着变宽（`.wb-app` 宽 427）。加 `html, body { overflow: hidden }` 后降到 413 仍不达标。
最终定因：**浮层宽度随内容变化**——Fluent 按旧尺寸算好位置，再塞进更大的内容，右边缘瞬间冲出视口，
移动端 Chrome 就把布局视口撑开了。把 `.wb-sheet` 宽度钉成 `min(288px, calc(100vw - 32px))` 后回到 360，收工。

### 验证

- 360×732：紧凑栏 7 个 44×44 按钮，Dock 占 8..349，无压叠；浮层 `[61,349,288]`，展开页面面板后 `innerWidth` 仍 360。
- 800×700：三段 `[65,263] [271,666] [674,736]`，`overlap:false`（改前是重叠的）。
- 732×360 横屏：仍走三段（732 > 700），`[31,229] [237,632] [640,702]`，无压叠。
- 设置窗口 360：卡片 text 宽 180（原 0），标题与控件纵向排布可读。
- 浮层「新增页」实测生效（指示器 `1 / 2` → `2 / 2`）；触屏书写仍可画出笔迹。
- 门禁：`tsc -b` 0、`oxlint` 0/0（192 文件）、`vitest` **61 文件 / 1459 例**、`pnpm e2e` 11 例（10 张基线未变，1200×800 走宽屏分支）、`pnpm verify` 通过。

**仍未覆盖**：iPad / iOS Safari 真机（安全区与 `dvh` 只在桌面 Chrome 的移动仿真里验证）；
横屏 + 软键盘同时出现的场景；窄屏下拖拽框选的触摸手势体验未做专门调优。

---

## P9 去掉顶部栏（2026-11-11）

用户指令（原文）："去掉顶部栏"。网页端原本照搬了桌面 `MainWindow.xaml:25-37` 的 `TitleBar`
（图标 + 标题 + 1px 分隔线，32 DIP），但那段 XAML 是给**无边框窗口**做拖拽区与标题显示的：
浏览器窗口自带标题栏，PWA 装到桌面后由系统绘制，网页端再画一条纯属重复，手机上是整整一行。

改动：

- 删除 `web/src/ui/shell/TitleBar.tsx`；`AppShell.tsx` 不再渲染它，顶部注释写明这是有意偏差。
- 删除 `styles/shell.css` 的 `.wb-titlebar*`、`styles/chrome.css` 的标题栏视觉细节、
  `styles/tokens.css` 的 `--wb-titlebar-height` / `--wb-titlebar-background` / `--wb-titlebar-foreground`。
- 安全区不丢：原来由标题栏的 `padding-top: env(safe-area-inset-top)` 吸收，现在由 `.wb-body` 承担
  （绝对定位子元素以 padding box 定位，画布与浮层一起让开刘海；上方窄条由 `.wb-app` 的画布底色填充，无缝）。
- 窗口标题仍在 `index.html` 的 `<title>` 与 PWA `manifest.webmanifest` 的 `name`/`short_name` 里。

验证：`ui-shell-smoke.test.tsx` 的断言从「有标题栏」改成「没有 `.wb-titlebar`」；
`tsc -b` 0、`oxlint` 0/0（191 文件）、`vitest` 61 文件 / 1459 例、
`pnpm e2e` 11 例（`canvas-empty` / `canvas-strokes-and-shapes` 两张基线因画布高了 32px 重建，
其余 8 张是元素级截图、不受影响——反过来印证这次只挪了顶部，没动 Dock 与浮层）。

---

## P10 网页端收紧：右键菜单与桌面专属入口（2026-11-25）

用户指令（原文）："1.取消浏览器自带右键菜单，它会与右键拖动冲突 2.删掉浏览器内仅桌面端可用的功能（如最小化等）"。

改动：

- **画布取消右键菜单**：`canvas/dom-input.ts` 给画布绑定 `contextmenu` 并 `preventDefault()`，
  detach 时同步解绑。右键拖动平移（桌面 `ShouldStartPan`）在按键抬起时原本会弹出浏览器菜单；
  触控笔笔杆键与移动端长按同理。只绑画布，设置窗口的输入框保留原生复制/粘贴菜单。
  依据 MDN：`contextmenu` 可取消；Firefox 下 Shift+右键不派发该事件，属无法拦截的浏览器保留行为。
- **删除最小化**：左 Dock 只渲染「更多 / 导入」（新增 `settings/model.ts` 的 `WEB_LEFT_DOCK_ITEMS`），
  窄屏「更多」浮层删除最小化按钮与 `Web_MinimizeUnsupported_Message`；Dock 设置预览过滤该 id，
  重排写回时用 `mergeVisibleOrder` 把它留在原位置。P3 偏差 #4「保留按钮弹解释提醒」就此作废。
  设置模型与默认值里的 `minimize` **不动**：`settings-defaults` 跨端一致性测试要求
  `DEFAULT_SETTINGS` 与桌面 `WindBoard.Tests/Fixtures/settings/defaults.json` 逐字段相同。
- **删除两个浏览器无效的设置项**：常规页「启动时窗口形态」（`general.startupWindowMode`）、
  Dock 页「显示快捷入口 Dock」（`dock.isShortcutDocksVisible`）。字段仍读写并随设置文件导出，
  只是网页端不暴露 UI（详见 `docs/dev/guides/web-port.zh-CN.md` §3.8）。

验证：`pnpm verify`（tsc 0 错误、vitest **61 文件 / 1460 例**、build 通过）；
`pnpm lint` 0 错误（仅 `BoardSurface` 的存量 warning）；`pnpm e2e` 13 例通过，
重建 4 张基线（`dock-left`、`settings-general`，以及叠加了 Dock 的 `canvas-empty` /
`canvas-strokes-and-shapes`）。`pnpm i18n` 重新生成双语词表。

---

## P11 视觉体系统一：移植 WinUI 3 主题（2026-11-27）

用户反馈（原文）："当前项目网页端设计存在问题：风格杂糅。大部分风格是 win10 那种 fluent design，这与我们当前项目的 winui3 设计不符，需要调整。"

### 决策

- 不直接依赖 `@pstrikez/fluent-winui-theme@0.2.0`：0.2.0、单维护者、3 stars、每周 13 次下载、peer 锁 `@fluentui/react-components ~9.74.4`（本项目 9.75.0）、解包 2.46 MB、自带 Meslo 字体并从微软 CDN 拉 Segoe UI Variable，不适合作为核心 UI 依赖。
- 以 MIT 方式移植它的 `--winui-*` 令牌字典与必要的 Fluent 组件覆盖；保留 `NOTICE` 与来源注释，不引入 AppShell/GradientBackground/toaster，不新增 `package.json` 依赖。
- 只改 `web/`；不改桌面 XAML、WBIX、渲染、输入、设置模型。

### P11.1 主题地基（先做，独立可回滚）

- [x] 新增 `web/src/styles/winui-tokens.css`：Light/Dark 两套 `--winui-*` 字典，值按 `design.md` §14.3；文件头注明来源 `@pstrikez/fluent-winui-theme@0.2.0`、MIT、上游 commit 与 `generic.xaml` 出处。
- [x] 新增 `web/src/app/theme.ts`：`winuiLightTheme`、`winuiDarkTheme`、`winuiLightThemeForced`；映射表按 `design.md` §14.4；`fontFamilyBase` 使用系统字体栈。
- [x] `web/src/App.tsx` 改用 `winuiLightTheme/winuiDarkTheme`；`DockBar.tsx`、`SelectionActionBar.tsx`、`ShapePropertiesPanel.tsx` 的嵌套 Provider 改用 `winuiLightThemeForced`。
- [x] `web/src/styles/tokens.css`：文字色别名到 `--winui-text-fill-*`；新增 `--wb-accent-selection: #0078D4`（选区）与 `--wb-accent-fill: var(--winui-accent-fill-default)`（设置/对话框控件）；保留 `--wb-dock-checked: #1976D2`；删除与 `--winui-*` 重复的颜色字面量；Dock 几何常量保持不变。
- [x] 新增 `web/tests/unit/winui-theme.test.ts`：断言三套主题存在、关键 token 指向正确的 `--winui-*`、没有遗漏的 Fluent 品牌色；断言 `package.json` 未新增参考包。
- 验收：`pnpm typecheck && pnpm test`；页面能启动，Dock 仍强制浅色，深色系统下设置窗口跟随深色。

### P11.2 设置窗口

- [x] `SettingsWindow.tsx`：导航图标 24 → 16；标题 20/28；关闭按钮 20 图标；页头标题 28/36、描述 12/16。
- [x] `SettingsCard.tsx` 增加可选 `icon`；六个设置页按桌面 `HeaderIcon` 语义补图标（常规/外观/书写/快捷键/Dock/关于）。
- [x] `settings.css`：`.wb-settings-surface` 背景 `--winui-solid-background-fill-base`；卡片 16 内边距、68 最小高、4px 圆角、Card 填充/描边；导航项 36px、4px 圆角、3×16 指示条，未选中文字用 `TextFillColorPrimary`；搜索框/下拉/开关随主题。
- [x] `settings-search.css`：建议列表 8px 圆角、卡片表面、14/12 字号；高亮动画改用 `--winui-accent-*`。
- 验收：设置六页在浅色/深色下与桌面截图结构一致；窄屏（≤720px）不回归。

### P11.3 对话框

- [x] `dialogs.css`：`.wb-dialog` 背景 `--winui-solid-background-fill-base`、8px 圆角、24 内边距、标题 20/28、按钮间距 8；导出/导入/确认/取色器/元素主题/恢复对话框统一。
- [x] 输入框、下拉、按钮、开关的视觉由 `winui-controls.css` + 主题 token 统一；只在几何与 Fluent 内部布局冲突时写双类覆盖。
- 验收：导出对话框表单密度、主按钮、取消按钮、输入框聚焦态与 WinUI ContentDialog 一致。

### P11.4 Dock、浮层与覆盖层

- [x] `dock.css`：`--wb-dock-checked` 保持桌面固定 `#1976D2`（不并入 accent fill）；选中底纹/描边继续按 #1976D2 的透明度变体；其余 14/10 圆角、60×52、0.85、强制浅色不变。
- [x] `overlay.css`：选中项 Dock 与形状属性浮层的文字/交互态改用 `--winui-*`；不改变 14px 底板与 8px 偏移。
- [x] `chrome.css`：提醒条 severity 色保留 WinUI 原值；信息级图标色改为 `--winui-accent-fill-default`。
- [x] `winui-controls.css`：覆盖 `Button`、`DialogSurface`、`Input`、`Dropdown`、`SpinButton`、`MenuPopover`、`MenuItem`、`Card`、`Tooltip`；`Switch`/`Slider`/`Radio` 由主题 token 生效。
- 验收：暗色系统下 Dock 仍是浅色；笔/橡皮/形状/页面面板的控件不再出现 Fluent 品牌蓝 `#0F6CBD`，改用 WinUI `AccentFillColorDefault`；Dock 选中仍是桌面 `#1976D2`；菜单/弹出层为 8px 圆角、无 Fluent 投影。

### P11.5 图标、焦点与文档

- [x] 图标尺寸统一：设置导航 16、卡片 20、对话框关闭 20、Dock 20；不引入 24px 图标。
- [x] `:focus-visible` 双描边：Dock 按钮、sheet 按钮、设置卡片、浮层控件；Fluent 组件用 `colorStrokeFocus1/2`。
- [x] 更新 `docs/dev/guides/web-port.zh-CN.md` §3.6/§6：登记主题层、强调色分层、不伪造 Mica、移植来源与 MIT 署名。
- [x] 新增 `web/src/styles/winui/NOTICE.md`：MIT 全文/链接、上游版本 `0.2.0`、commit、移植范围。

### P11.6 验证与门禁

提交前在 `web/` 下执行：

```powershell
pnpm lint
pnpm typecheck
pnpm test
pnpm build
pnpm e2e
```

- [x] `pnpm e2e` 首次会因字体/表面变化出现差异；逐张确认是预期变更后再 `pnpm e2e:update`，并在提交说明列出重建的基线。
- [x] 手工检查：浅色/深色设置窗口、导出/导入/确认对话框、笔/橡皮/形状/页面面板、Dock 选中态、键盘焦点、1200×800 与 360×732。
- [x] `git diff --check`；确认 `package.json`/`pnpm-lock.yaml` 无变化。
- 验收：AC14–AC19。

### P11 完成记录（2026-11-27）

- **主题地基**：新增 `web/src/styles/winui-tokens.css`（Light/Dark `--winui-*` 字典）、`web/src/app/theme.ts`（`winuiLightTheme`/`winuiDarkTheme`/`winuiLightThemeForced`）、`web/src/styles/winui-controls.css`；`App.tsx`、Dock/浮层/覆盖层的嵌套 Provider 全部改用 WinUI 主题。`tokens.css` 的文字/表面令牌别名到 `--winui-*`，保留 Dock 几何常量与桌面固定色。
- **强调色分层**：设置/对话框用 `AccentFillColorDefault`（浅色 `#0067C0`、深色 `#4CC2FF`）；选区保留 `#0078D4`；Dock 选中保留 `#1976D2`。没有把三套蓝强行统一，因为后两者是桌面明确写死的值。
- **设置窗口**：导航图标 16、导航项 36px、设置卡片 16px 内边距 / 68px 最小高 / 4px 圆角 / 20px 图标、页面标题 28/36、对话框底 `SolidBackgroundFillColorBase`；六个设置页补齐桌面语义的卡片图标。
- **对话框**：`DialogSurface` 统一为 `SolidBackgroundFillColorBase`、8px 圆角、SurfaceStroke 边框；导出/导入/确认/取色器/恢复对话框随 WinUI 主题；关闭图标 20、InfoBar 关闭图标 16。
- **Dock/浮层**：尺寸、14/10 圆角、0.85 透明度、强制浅色全部不变；只换内部 Fluent 控件主题与菜单/弹出层表面。
- **参考实现**：`@pstrikez/fluent-winui-theme@0.2.0` 的令牌与规则移植说明在 `web/src/styles/winui/NOTICE.md`；`package.json`/`pnpm-lock.yaml` 未新增依赖。
- **验证**：`pnpm lint` 0 errors（1 条 `BoardSurface` 存量 warning）、`pnpm typecheck` 0、`pnpm test` **63 文件 / 1483 例**、`pnpm build` 通过、`pnpm e2e` **20 例**通过（重建 `settings-general.png` 与 `dialog-export.png` 两张基线）。浅色/深色设置窗口与导出对话框已人工截图核对。
- **有意偏差**：不伪造 Mica（设置窗口用实心底色）；固定默认蓝调色板（浏览器读不到系统强调色）；字体只用系统栈，不打包/不请求 CDN。均已登记到 `docs/dev/guides/web-port.zh-CN.md` §3.6/§6。

### Modern Web Guidance 审查（2026-11-27）

- 已修：`:root` 补 `color-scheme`（跟随 `data-theme`）；`user-select: none`/`touch-action: none` 从 `.wb-app` 下放到画布与 Dock 层，设置/对话框恢复文字选中与触摸滚动；自定义焦点环改用 `CanvasText`；补 `prefers-reduced-motion`、`accent-color` 与 `color-mix` 的不透明回退。
- 已修（左列对比度）：设置导航未选中项从 Fluent subtle Button 的 `colorNeutralForeground2`（62% 黑）改为 WinUI `NavigationViewItemForeground` 的 `TextFillColorPrimary`（89.4% 黑）；选中态不加粗（WinUI `FontWeight="Normal"`），只靠背景与 3×16 指示条。计算色实测 `rgba(0,0,0,0.894)`，浅色下对比度约 15:1。
- 有意不采用：
  - `font-size-adjust: from-font`：系统字体没有 Web Font 换字导致的 CLS，且它会按 Segoe UI Variable 的 x-height 缩放中文回退字体，实测只改变文字像素、破坏 WinUI 基线。
  - 原生 `<dialog>`：Fluent Dialog 已提供 `role="dialog"`、`aria-modal`、焦点陷阱、Esc 与 inert 背景；换原生需要重写整个弹窗层。
  - `@layer`：Fluent Griffel 以未分层样式注入，放进 layer 会失去优先级；继续用双类选择器（见 web-port 指南 §3.6）。
  - 完整 Forced Colors：项目明确排除高对比度；焦点环已用 `CanvasText`，完整高对比度样式留作后续。
- 待办（可选）：字号仍为 `px`；若要响应浏览器默认字号，需要把自定义 CSS 与 Fluent `fontSizeBase*`/`lineHeightBase*` 换成 `rem`/无单位并重跑视觉基线。

### 回滚点

- P11.1 完成后是一个独立回滚点：若主题层出现全局回归，删除 `winui-tokens.css`、`theme.ts`、`winui-theme.test.ts`，恢复 `App.tsx` 与三个嵌套 Provider 的旧导入即可。
- P11.2/P11.3/P11.4 按区域独立提交；某一区域回滚不影响主题层。
- 不涉及数据格式与设置模型，回滚不需要迁移。

---

## 依赖与顺序

```
P0 ──▶ P1 ──▶ P2 ──▶ P3 ──▶ P4 ──▶ P5
        └────────────▶ P4.2/P4.3 只依赖 P1.3 的 rasterizer，可在 P3 前并行
P0.6 ──▶ P4.6
P0.4 ──▶ P0.7 / P0.8（先有稳定的写侧，才有分派与 golden 的判据）
```

- P1.2（几何）与 P0 可并行，但 P1.3 依赖 P0 的 f32 约定
- P0.9 是纯文档，任何时刻可做；建议在 P1.2 之前完成，作为几何实现的依据
- P0.7 与 P0.8 都只动 `WindBoard/Board/Persistence/Wbix/` 与 `WindBoard.Tests/`，与 web 端完全解耦，可与 P0.1~P0.3 并行
- P2.3 覆盖层与 P3.2 Dock 可并行（不同文件）
- P5 全部依赖前序阶段完成

## 里程碑判据

| 里程碑 | 判据 |
|---|---|
| M0 契约冻结 | P0 出口全部满足；此后 `.wbix` 写出格式由 golden 测试强制锁定，任何格式变化必须同步更新 golden 并在提交说明中给出理由 |
| M1 可书写 | P1 出口：能画能缩能撤，重载一致 |
| M2 可编辑 | P2 出口：选择变换与多页完整 |
| M3 可用 | P3 + P4 出口：AC1/AC2/AC4/AC5/AC6/AC7 全绿 |
| M4 可交付 | P5 出口：AC8/AC9 全绿（AC10 是全程约束，每阶段回归验证） |
