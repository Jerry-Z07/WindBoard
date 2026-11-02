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
- **桌面没有 Ctrl+A / Esc / Delete 的键盘路径**（选择操作全是画布浮层按钮），故网页端不擅自加；
  `Duplicate` 的偏移是 `12 DIP ÷ zoom`（`BoardCanvasControl.Rendering.cs:510`）。
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

- [ ] **P5.1 视觉回归**：Playwright 截图基线入库；Mica/亚克力区域只比对结构
- [ ] **P5.2 性能**：1 万笔迹实测，必要时启用分块位图缓存；给出帧时间与输入延迟数据
- [ ] **P5.3 触屏**：iPad Safari 压感与掌拒实测；iOS canvas 尺寸限制处理
- [~] **P5.4 i18n**：`gen-i18n.mjs` + key 集合审计测试（基础设施已落地，界面文案迁移进行中）
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
