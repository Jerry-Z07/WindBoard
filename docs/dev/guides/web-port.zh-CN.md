# 网页端移植与双端同步规范（web-port）

`web/`（网页端 PWA）与桌面主程序之间的**对应关系与同步义务**，落在两个问题上：

1. 桌面侧改了某处，网页端的对应实现落在哪个文件？
2. 用什么命令证明两端没有漂？

适用场景：修改桌面主程序后判断网页端是否需要跟进；修改 `web/` 后判断是否破坏了与桌面的一致性；评审两端相关 PR 时核对清单。

> 事实源优先级：**桌面 C# / XAML / `.resw` 源码 > 本文 > `web/` 代码注释 > `.trellis/tasks/10-31-web-port/` 下的调研文档**（那里是立项期的一次性调研，不含后续变更）。
> `docs/assets/images/` 下的截图是历史版本，不作为 UI 依据。

## 1. 定位与边界

`web/` 是 WindBoard 的**网页端移植**，目标是：

- 与桌面版在 `.wbix` 文件上**双向无损互通**；
- 最大化还原桌面版的操作方式与界面（几何、阈值、文案、布局尺寸都以桌面源码为准）。

技术形态：纯静态、**本地优先**的 PWA（React 19 + TypeScript + Vite + Fluent UI v9 + Canvas 2D）。无后端、无账号、无云同步。

**不在网页端范围内**的桌面能力（不要为对齐而实现）：

| 桌面能力 | 网页端处理 |
|---|---|
| 窗口伪装（Camouflage）、托盘、开机启动 | 不做，设置里不出现 |
| 屏幕批注（ScreenAnnotation） | 不做 |
| 自动检查更新、下载源选择 | 由 Service Worker 静默更新替代，设置页不出现 |
| 「更多」菜单里的「屏幕批注」「退出」 | 菜单只保留 设置 / 导出 / 全屏 |
| 多窗口（`AppWindow`）与窗口置顶 | 不做 |

工程约定：

- `web/` **不属于** `WindBoard.slnx`，独立用 pnpm 管理，不参与 `dotnet build`。
- 但跨端一致性测试会**直接读取** `WindBoard.Tests/Fixtures/wbix/` 的 golden 语料（单一事实源，不复制副本）。
- `web/**` 的行尾是 **LF**、缩进 **2 空格**（根 `.gitattributes` 与 `web/.editorconfig` 覆盖仓库默认的 CRLF/4 空格）；`docs/**` 与 `AGENTS.md` 仍是 CRLF。

## 2. 分层映射

桌面 → 网页端的模块对应（`web/src/` 下的路径省略前缀）：

| 桌面（`WindBoard/`） | 网页端（`web/src/`） | 备注 |
|---|---|---|
| `Board/BoardDocument.cs`、`Board/Items/` | `board/document.ts`、`board/model/{stroke,shape}.ts` | 列表顺序即 z 序 |
| `Board/Editing/BoardSession.cs` | `board/session.ts` | `IBoardCommand` → `Command { do, undo }` |
| `Board/Editing/BoardWorkspace.cs`、`BoardPage.cs` | `board/workspace.ts`、`board/page.ts` | 多页，恒 ≥ 1 页 |
| `Board/Viewport/BoardViewport.cs` | `board/viewport.ts` | 缩放 0.05~32（导出态另有区间） |
| `Board/Commands/*.cs` | `board/commands.ts` | 一条 C# 命令 ↔ 一个命令对象 |
| `Board/Persistence/BoardWorkspaceSnapshot.cs`、`BoardWorkspaceSnapshotConverter/Applier` | `board/snapshot.ts` | 运行时 ↔ 持久化的桥梁 |
| `Board/Elements/*.cs` | `board/model/element.ts` | 文本/链接/媒体/文件四类元素 |
| `Board/Editing/{InkItemHitTest,SegmentMath2D}.cs` | `ink/hit-test.ts` | 擦除命中（点到线段 / 线段到线段距离） |
| `Board/Editing/{InkItemPickTest,InkItemRectSelectTest,InkItemScreenBounds}.cs` | `board/editing/{pick-test,rect-select,ink-bounds}.ts` | 点选 / 框选 / 屏幕边界 |
| `Board/Editing/{ElementPickTest,ElementRectSelectTest,ShapePropertyMath}.cs` | `board/editing/{element-pick,shape-property}.ts` | 形状属性浮层的换算 |
| `Board/Editing/{PixelStrokeEraser,WholeStrokeEraser}.cs` | `ink/pixel-eraser.ts`、`tools/eraser-tool.ts` | 像素擦除与整笔擦除 |
| `Board/Persistence/Wbix/**` | `persistence/wbix/**` | 互通核心，见 §3.1 |
| `Rendering/Board/BoardSceneRenderer.cs` | `render/scene.ts`、`render/element-card.ts`、`ink/outline.ts` | Canvas 2D 重实现 |
| `Rendering/Board/BoardSceneMath.cs` | `ink/geometry.ts` | 笔宽/半径/贝塞尔控制点 |
| `Rendering/DxSwapChainPanelRenderer*.cs`、`DxDirtyRectCalculator.cs` | `render/render-handle.ts`、`render/frame-scheduler.ts`、`render/static-cache.ts` | rAF 合并、脏区、静态层缓存 |
| `Interaction/BoardInputController/*.cs` | `interaction/pointer-router.ts`、`canvas/board-controller.ts` | 指针决策表 |
| `Interaction/Tools/{Pen,Shape,Eraser,Select}Tool.cs` | `tools/{pen,shape,eraser,select}-tool.ts` | 工具状态机 |
| `Interaction/{BoardTool,ToolOptions}.cs` | `tools/tool-settings.ts` | 工具枚举与画笔参数 |
| `Features/Shortcuts/**`、`UI/MainWindow/MainWindow.Shortcuts.cs` | `interaction/shortcuts.ts` | 撤销/重做绑定 + 网页端补充的选择快捷键 |
| `Features/Dock/**`（`DockItemIds.cs`、排序/隐藏/就地编辑） | `ui/shell/dock-layout.ts`、`ui/shell/DockEditOverlay.tsx`、`settings/model.ts`、`settings/dock-order.ts` | Dock 分组、默认顺序、可拖拽排序、隐藏项与主界面就地编辑 |
| `Controls/BoardCanvasControl.xaml{,.cs}`、`BoardCanvasControl.*.cs` | `canvas/{board-controller,dom-input}.ts` + `ui/shell/BoardSurface.tsx` | 命令式画布 + React 外壳 |
| `Controls/BoardCanvasControl.{SelectionHandles,ShapeProperties}.cs`、`BoardCanvasControl.xaml` 的选择框/手柄 | `render/{selection,selection-handles}.ts`、`ui/overlay/**` | 桌面是 XAML 浮层，网页端用 Canvas 绘制 + DOM 浮层 |
| `Controls/PageThumbnailControl.xaml.cs` | `render/thumbnail.ts`、`ui/shell/PageThumbnail.tsx` | 页面缩略图 |
| `MainWindow.xaml`、`UI/MainWindow/MainWindow.*.cs` | `ui/shell/*`（`AppShell`/`DockBar`/`DockButton`/`ReminderStack`） | Dock、提醒栈；**标题栏不渲染**（见 §6） |
| `MainWindow.xaml` 里的 Flyout 段 | `ui/flyouts/{Pen,Eraser,Shape,Pages}Flyout.tsx` | 弹出面板 |
| `App.xaml` 里的 Style 资源 | `styles/tokens.css`、`styles/dock.css`、`styles/overlay.css` | 颜色/圆角/尺寸常量 |
| `Features/Export/**` | `features/export/**` | PNG/PDF/WBIX 导出 |
| `Features/Import/**` | `features/import/wbix-import.ts` | 仅 WBIX 导入 |
| `Settings/**`、`AppSettingsService` | `settings/**`、`settings/service.ts` | 设置模型 / 服务 / 页面 |
| `Strings/<locale>/*.resw`、`Localization/L10n` | `i18n/**`（构建期生成） | 见 §3.7 |
| `Reminders/`、`Errors/` | `ui/reminders/reminder-store.ts`、`ui/shell/ReminderStack.tsx` | 提醒条（InfoBar） |

网页端独有、桌面没有对应物的部分（改这些时不必找桌面依据）：

| 路径 | 作用 |
|---|---|
| `platform/{io,pwa,file-handlers}.ts` | File System Access / Service Worker / `launchQueue` |
| `persistence/autosave*.ts`、`ui/dialogs/RecoveryDialog.tsx` | 自动保存与崩溃恢复（桌面无此功能） |
| `persistence/wbix/` 之外的 `settings/settings-file.ts` | 设置导出为 JSON（对齐桌面 settings 文件形态） |
| `app/**`、`canvas/dom-types.ts` | 应用装配、Provider、DOM 类型边界 |

## 3. 双端契约与保障手段

每节给出：**事实源**（改哪里要同步）→ **网页端实现** → **保障手段**（能自动发现漂移的测试/脚本，或「无，人工比对」）。

### 3.1 WBIX 持久化（保障最强，字节级）

- 事实源：`WindBoard/Board/Persistence/Wbix/**`（读写、JSON 选项、页 payload、资源上限）。
- 网页端：`persistence/wbix/**`。
- 保障：
  - `web/tests/unit/{json-options,f32-format,manifest,page-payload,ink-codec,wbix}.test.ts`：逐字符断言 JSON 文本、f32 文本、键序、转义、CRLF。
  - `web/tests/conformance/` 直读 `WindBoard.Tests/Fixtures/wbix/`（`model/*.json`、`expected/**`、`files/*.wbix`）。
  - `pnpm interop`，双端 A~E 五步（详见 `web/README.md`）：网页端读桌面产物、桌面读网页端产物、重写后再读，dump 逐字段比对。
- 漂移症状：桌面能打开但网页端打不开（或反之）；`pnpm interop` 在某个 `pages[i].xxx` 路径报差异。
- **版本升级义务**：`.wbix` 的 `version` 是真契约。桌面加 `version=4` 时必须同时给网页端加 reader 分派（`persistence/wbix/read.ts` 的 reader 注册表，对应桌面 `WbixWorkspaceSerializer.Readers.cs` 的 `MinVersion/MaxVersion` 区间），并补 golden 语料。绝不能只改桌面。

### 3.2 笔迹与图形几何

- 事实源：`WindBoard/Rendering/Board/{BoardSceneRenderer,BoardSceneMath}.cs`；规范见 `docs/dev/guides/ink-rendering.zh-CN.md`。
- 网页端：`ink/geometry.ts`、`ink/outline.ts`、`render/scene.ts`。
- 保障：`web/tests/unit/{ink-geometry,ink-outline}.test.ts`、`render-scene.test.ts`（断言绘制调用序列）、桌面侧 `WindBoard.Tests/Rendering/Snapshot/` 像素基线。**两端没有像素级交叉比对**，几何改动必须人工按 `ink-rendering` 文档逐条核对。
- **锐利度**：桌面用 `ID2D1Ink::DrawInk` 由 D2D 在设备分辨率上重新细分，网页端展开成多边形再 `fill`。两者要看起来一致，关键在三点，改任何一处都要重新目视核对：
  1. 后备存储必须等于 `DIP × 真实 DPR`（DPR 被夹小 → 浏览器放大画布 → 边缘过渡从 1 设备像素变 2 像素，见 §6 的 DPR 行）；
  2. 折线细分容差必须是 **0.25 设备像素** = `0.25 / (zoom × DPR)` 世界单位（`ink/outline.ts` 的 `outlineScale`），只按 CSS 像素算会让 DPR 3 的屏幕上误差放大到 0.75 设备像素；
  3. 后备存储与画布左上角必须落在**整设备像素**上（`render/render-handle.ts` 的 `computeDeviceGridAlignment`）：`clientWidth/clientHeight` 是四舍五入后的整数，拿它算后备存储会与小数布局盒差 1 行（约 0.1% 整图缩放）；合成层原点落在半设备像素上时 Chromium 还会整层双线性重采样。两条都会让小数缩放（Windows 125%/150%、浏览器缩放 110%）下的边缘从 1 个设备像素过渡变成 2 个。`web/tests/unit/render-device-grid.test.ts` 锁住算法。
- 漂移症状：笔迹粗细/拐角/箭头翼形状在两端看起来不同；同一 `.wbix` 打开后线条明显变形；**同几何下网页端边缘更软、放大后边缘有细碎折角**。

### 3.3 视口与坐标换算

- 事实源：`WindBoard/Board/Viewport/BoardViewport.cs`。
- 网页端：`board/viewport.ts`。
- 保障：`web/tests/unit/board-viewport.test.ts`（含滚轮 `1.1^(Δ/120)`、`MinZoom/MaxZoom`）。
- 漂移症状：缩放锚点偏移、平移速度不同、绘制点与光标位置错开。

### 3.4 输入路径与工具状态机

- 事实源：`WindBoard/Interaction/**`（`BoardInputController` 的 partial 拆分、`Tools/*Tool.cs`）。
- 网页端：`interaction/pointer-router.ts`（纯决策表）+ `canvas/board-controller.ts`（状态机装入）+ `tools/*.ts`。
- 保障：`web/tests/unit/{pointer-router,board-controller,select-tool,tool-pen,tool-shape,tool-eraser}.test.ts`、`dom-input.test.ts`。
- 漂移症状：某一手势（双击、双指、框选阈值、亚阈值拖拽）在两端行为不同。**桌面这里最容易变**（历史多次改过时序），因此 `pointer-router` 的改动必须补单测。
- 双指手势的「结束单指操作」与「开始手势」是**两个先后时刻**，判定必须按「结束之后」的状态算。这条时序在桌面侧改过，改桌面 `BoardInputController.Pointer.cs` 的 MultiFinger 分支时务必同步网页端。

桌面判断与网页决策条目的对照如下，按符号名记录，不记行号：

| 桌面 | 判断 | 网页端 |
| --- | --- | --- |
| `PointerRoutingDecisions.ShouldStartStroke` | 鼠标需左键，笔与触摸恒为 true | `decidePointerAction` 的非触控落笔门 |
| `PointerRoutingDecisions.ShouldStartPan` | `allowViewport && Mouse && 右键` | `decidePointerAction` 的非触控 pan 分支 |
| `PointerRoutingDecisions.NormalizePressure` | 非笔 1.0，笔夹到 0.1~1 | `normalizePressure` |
| `PointerRoutingDecisions.ResolveTouchPressRoute` | 双指、选择手势、有捕获时忽略、笔橡皮 | `decidePointerAction` 的触摸按下四分支 |
| `PointerRoutingDecisions.ResolveTouchGestureEnd` | 五种结束结果 | `decideTouchGestureEnd` |
| `PointerRoutingDecisions.EvaluateWheelZoomTick` | 空闲 150ms 结束 | `decideWheelZoomTick` |
| `PointerRoutingDecisions.ResolveHorizontalWheelPanScreenDelta` | `(-Δ, 0)` | `horizontalWheelPanScreenDelta` |
| `Manipulation.cs` 的 `OnCanvasPointerWheelChanged` | 六步顺序，横向平移先于修饰键变换 | `decideWheelAction` |
| `Manipulation.cs` 的 `HasBlockingInteractionForWheel`、`HasBlockingInteractionForManipulation` | 有活动工具操作或手势时阻塞 | `ctx.hasBlockingInteraction`、`ctx.blockingInteraction` |
| `Pointer.cs` 的 `HandleTouchPointerPressed` | 选择工具下恒起框选，即使点在选区内 | 触摸按下 + 选择工具单指 → `select-marquee` |
| `Pointer.cs` 的 `HandleNonTouchPointerPressed`、`TryBeginSelectGesture`、`TryBeginNonSelectGesture` | 捕获门之后分选择与非选择两路 | `decidePointerAction` 的非触控分支 |
| `ManipulationStarting` | `select && target == Selection && 有选中` 时为选区手势 | `resolveTouchManipulationTarget`、`gesture.target` |
| `Open.cs` 的 `HandleElementClickForMaybeOpen` | 抬起时同元素、500ms、10DIP 内 | 抬起相位的 `open-element`，常量 `ELEMENT_DOUBLE_CLICK_TIMEOUT_MS`、`ELEMENT_DOUBLE_CLICK_MAX_DISTANCE_DIP` |
| `Pointer.MoveEnd.cs` 的 `HandlePointerEnded` | pan → marquee → selection → active | 抬起与取消相位 → `end` |
| `PointerRouteState.ResolveMoveRoute` | 按 `pointerId` 归属路由 | 移动相位按 `activeGesture` 路由 |
| `SelectTool.IsScreenPointInsideSelectedBounds` | 屏幕命中测试 | `ctx.insideSelectionBounds` |
| `PointerRouteState.HasActivePointerCapture` | 活动工具操作或任一手势 | `ctx.hasActivePointerCapture` |

- 网页端独有的分支（桌面没有对应逻辑）：笔按下期间忽略触摸（掌拒护栏），水平滚轮平移结果 `-0` 归一化；画布上的 `contextmenu` 一律 `preventDefault`（`canvas/dom-input.ts`），否则右键拖动平移一松手就会弹出浏览器菜单。

### 3.5 领域模型与撤销粒度

- 事实源：`WindBoard/Board/**`（`BoardSession`、`Commands/**`、`Editing/**`）。
- 网页端：`board/{document,session,commands,workspace}.ts`、`board/editing/**`。
- 保障：`web/tests/unit/{board-session,board-workspace,editing-*}.test.ts`。
- 漂移症状：同样操作在两端撤销步数不同（例如橡皮：桌面是「一次手势一条命令」，清屏是一条 `ClearCommand`）。

### 3.6 UI 常量与视觉

- 事实源：`WindBoard/MainWindow.xaml`、`WindBoard/App.xaml`（按钮/色块/托盘样式）、`WindBoard/Settings/*.xaml`、`WindBoard/Features/**/UI/*.xaml`。
- 网页端：`ui/**`（结构）+ `styles/tokens.css`（颜色、圆角、尺寸）+ `styles/{shell,dock,settings,dialogs,overlay}.css`。
- 保障：`web/tests/conformance/desktop-tokens.test.ts`：**直接读桌面 XAML 源码**，把 Dock 底板/按钮/页码/页面面板/选择手柄等 20 余个数值与 `styles/*.css` 的令牌逐个比对（颜色按 ARGB→RGBA 换算后比通道）。改桌面数值不同步网页端 → 该测试立刻红。
- `web/e2e/`（`visual.spec.ts` 8 个用例 / 10 张界面基线截图 + `touch.spec.ts` 3 个触屏用例）+ `web/tests/unit/dock-layout.test.ts`、`web/tests/unit/ui-shell-smoke.test.tsx`（挂载与关键交互冒烟）。未纳入令牌测试的 XAML 数值仍靠人工核对，新增令牌时请顺手加一条规则。
- 页面管理条目的宽度由面板内容区决定（桌面 ListViewItem 拉伸、网页 `.wb-pages__row` 不设固定宽度），不再维护 240 常量；令牌比对保留面板宽、缩略图高与删除按钮尺寸。
- 漂移症状：面板尺寸/圆角/选中态颜色不对。
- **坑（已踩过两次）**：XAML 的 8 位十六进制颜色是 **ARGB**，CSS 的 8 位是 **RGBA**，直接抄会得到完全不同的颜色（`#E4000000` 在浏览器里是不透明红，`#08FFFFFF` 是青色）。所有带 alpha 的颜色必须写成 `rgb(r g b / a%)`。
- **坑（Fluent UI v9）**：Griffel 用原子类下发 `width`/`height`/`display` 等属性，与单类选择器同权重但注入更晚。覆盖 Fluent 组件自身设置过的属性必须用双类选择器（`.fui-DialogSurface.wb-settings-surface`）。

**WinUI 3 视觉层（P11）**：组件层不再直接使用 Fluent v9 的 `webLightTheme`/`webDarkTheme`，而是：

- `styles/winui-tokens.css` 提供 `--winui-*` 的 Light/Dark 字典（`SolidBackgroundFillColor*`、`CardFill/Stroke`、`AccentFillColor*`、焦点双描边、4/8px 圆角）；
- `app/theme.ts` 用 `winuiLightTheme`/`winuiDarkTheme` 把 Fluent Theme token 重指到上述变量，并把 `fontFamilyBase` 设为系统字体栈；
- `styles/winui-controls.css` 只覆盖主题管不到的 Fluent 几何/表面属性，全部使用双类选择器。

这些令牌与规则移植自 `@pstrikez/fluent-winui-theme@0.2.0`（MIT，来源与许可全文见 `web/src/styles/winui/NOTICE.md`），**不把该包加入依赖**。强调色按用途分层，不要“统一成一个蓝”：

| 用途 | 值 | 出处 |
|---|---|---|
| 设置/对话框的强调按钮、开关、滑块 | `AccentFillColorDefault`：浅色 `#0067C0`、深色 `#4CC2FF` | WinUI 默认蓝调色板 |
| 选区/手柄 | `SystemAccentColor` 默认 `#0078D4` | 桌面 `BoardCanvasControl.xaml` |
| Dock 选中 | `#1976D2` | 桌面 `MainWindow.xaml:69-73` 固定值 |

设置窗口**不伪造 Mica**：浏览器拿不到桌面壁纸与材质，窗口底使用 `SolidBackgroundFillColorBase`（浅色 `#F3F3F3`、深色 `#202020`）。字体只使用系统栈（Segoe UI Variable → Segoe UI），不打包、不请求 CDN，保证离线 PWA 与 Windows 10 回退。

无障碍与浏览器 UI 约定：

- `color-scheme` 在 `:root` 跟随 `data-theme`，原生滚动条、表单控件与主题同步。
- 设置导航未选中项使用 `TextFillColorPrimary`（WinUI `NavigationViewItemForeground`），不要沿用 Fluent subtle Button 默认的 `colorNeutralForeground2`；选中态靠背景与 3×16 指示条表达，不加粗（WinUI `FontWeight="Normal"`）。
- `user-select: none` 与 `touch-action: none` 只作用于画布和 Dock 编辑层；设置/对话框保持文字可选中、内容可触摸滚动。
- 自定义焦点环用 `outline: 2px solid CanvasText` + `outline-offset`；`CanvasText` 跟随 `color-scheme`，强制色彩模式下自动换成系统色。`accent-color` 指向 WinUI 强调色。
- 不启用 `font-size-adjust`：本项目没有 Web Font 换字导致的 CLS，而 `from-font` 会按 Segoe UI Variable 的 x-height 缩放中文回退字体，改变 WinUI 基线里的 CJK 字号。
- 字号暂按 WinUI 有效像素（`px`）固定，页面缩放会整体缩放；不响应浏览器“默认字号”偏好。若要支持系统文本缩放，需要单独把自定义 CSS 与 Fluent `fontSizeBase*`/`lineHeightBase*` 换成 `rem`/无单位并重跑视觉基线。

### 3.7 文案

- 事实源：`WindBoard/Strings/<locale>/*.resw`（桌面唯一事实源）+ `WindBoard/Localization/`。
- 网页端：`i18n/locales/*.generated.ts`（**构建期生成，提交入库**）、`i18n/index.ts`、`i18n/message.ts`。
- **新增 / 修改 resw key 后，网页端必须跑 `pnpm i18n` 重新生成并提交生成物**；生成器会校验两语言 key 集合一致。
- 网页独有文案（浏览器才有的概念：自动保存恢复、Service Worker、把设置导出成 JSON 等）放在 `i18n/web-strings/*.json`，key 一律 `Web_` 前缀。**不要**把这类文案塞进桌面 `.resw`：那会让桌面背死 key，并干扰桌面侧的 key 审计（`WindBoard.Tests/Localization/LocalizationKeyAuditTests.cs` 要求 key 被 C# 引用）。
- 保障：`web/tests/unit/i18n.test.ts`：重新解析 18 个 `.resw` 与 web-strings，与生成物逐 key 比对（过期即失败），并校验前缀、集合一致、无空文案。
- 编码约定：C# 用 `L10n.Get/Format("Key")`；网页端组件里 `const { t } = useI18n()` 后 `t('Key')`。**模块级常量表不要存中文**，存 `MessageKey`（如 `labelKey`），渲染处取词。
- 开发者诊断文本（解析错误、`console.warn`、不变量异常）**不本地化**，与桌面保持一致。

### 3.8 设置格式

- 事实源：桌面设置文件与 `WindBoard/Settings/**`（含 `PenSettingsDefaults`、颜色归一化规则）。
- 网页端：`settings/model.ts`（模型 + 归一化）、`settings/service.ts`（单例 + 事件）、`settings/settings-file.ts`（导出/导入 JSON）。
- 保障：`web/tests/unit/{settings-model,settings-service,settings-file}.test.ts` + `web/tests/conformance/settings-defaults.test.ts`（读 `WindBoard.Tests/Fixtures/settings/defaults.json` 与 `DEFAULT_SETTINGS` 逐字段比对；语料由 `WindBoard.Tests/Settings/SettingsDefaultsGoldenTests.cs` 守护，桌面改默认值 → 桌面语料测试先红 → 重建语料 → 网页端测试再红）。
- 重建默认值语料：`WINBOARD_REGEN_SETTINGS_GOLDEN=1 dotnet test WindBoard.slnx --filter FullyQualifiedName~SettingsDefaultsGoldenTests`，改动理由写进提交说明。
- 漂移症状：导入桌面设置后某些项回落默认值（通常是新增字段或归一化规则变了）。
- 桌面才有意义的项不在网页端暴露 UI，但字段照旧读写（保证设置文件互通）：`general.startupWindowMode`（浏览器无法控制启动窗口形态）、`dock.isShortcutDocksVisible`（v1 不实现快捷 Dock）、`dock.leftOrder` 里的 `minimize`（浏览器没有最小化窗口能力，主 Dock 与 Dock 编辑层都不渲染）。`dock.hiddenItems` 双端共享：网页端 Dock 渲染会过滤隐藏项，主界面 Dock 编辑层（`ui/shell/DockEditOverlay.tsx`）提供拖动排序与隐藏/恢复入口。

### 3.9 页面缩略图

- 事实源：`WindBoard/Controls/PageThumbnailControl.xaml.cs`（`CanvasPadding=8`、`MaxPointsPerStroke=240`、只缩小不放大、线宽 `max(0.8, baseSize*scale*0.55)`、等距降采样保留末点）。
- 网页端：`render/thumbnail.ts`、`ui/shell/PageThumbnail.tsx`。
- 保障：`web/tests/unit/render-thumbnail.test.ts`（13 例，按桌面实际行为断言，包括「1000 点降采样后是 251 点」这种非整数拷贝行为）。
- 已知有意偏差：网页端额外绘制形状（桌面只画笔迹），否则「只有形状的页」缩略图会全空白。

### 3.10 窄屏（手机）布局

- 事实源：**无**。桌面 WinUI 窗口的最小宽度足以横排三段 Dock，没有窄屏概念；全部是网页端新增，不属于移植项。
- 断点：`styles/shell.css` 与 `ui/shell/use-narrow-layout.ts` 里的 **700px**，两处必须同时改（`NARROW_LAYOUT_QUERY` 的注释已声明这条约束）。取值依据是实测的三段 Dock 原始宽度：左 134 + 中 395 + 右 62；700 的阈值是按去掉「最小化」前的左段宽度（198）留的余量，不随左段收窄而下调；中段绝对居中的排法在 <819 会与左段压叠，由 `@media (max-width: 900px) and (min-width: 701px)` 改成不脱流的居中排法单独处理。
- 窄屏形态：单段紧凑栏（`ui/shell/DockBar.tsx` 的 `NarrowDock`）= 4 个工具 + 分隔线 + 撤销/重做 + 「更多」浮层；页面组（上一页/指示器/下一页/新增页）与导入/导出/设置/全屏/页面管理全部收进「更多」浮层（`NarrowMoreButton`）。
- 安全区：`index.html` 的 `viewport-fit=cover` + `styles/shell.css` 里 `.wb-body`、Dock 层、提醒栈的 `env(safe-area-inset-*)`。
- 保障：`web/tests/unit/ui-narrow-shell.test.tsx`（4 例，装 `matchMedia` 桩锁住窄屏分支的按钮集合与浮层入口）；宽屏分支由 `ui-shell-smoke.test.tsx` 覆盖。像素级尺寸（44×44 触摸目标、隐藏文字）只能靠浏览器截图复核。
- 踩坑：移动端 Chrome 遇到横向溢出会**撑宽布局视口且不再收回**（实测 360 → 413，`position: fixed` 的外壳与 Dock 一起被推着变宽）。因此 `html, body` 必须 `overflow: hidden`，且窄屏浮层宽度必须固定：浮层内展开子面板会改变宽度时，Fluent 按旧尺寸算好位置再塞进更大的内容，右边缘瞬间冲出视口就触发了这个行为。

## 4. 同步流程：改了桌面之后做什么

第一步永远是判断「这次改动是否落在契约面上」。按桌面改动位置，必查的网页端位置与必跑命令如下。

| 改动了 | 网页端必查 | 必跑 |
|---|---|---|
| `Board/Persistence/Wbix/**`、`.wbix` 版本号 | `persistence/wbix/**`、`WindBoard.Tests/Fixtures/wbix/` 语料 | `pnpm interop`（+ 补 golden） |
| `Rendering/Board/**`、`Board/Persistence` 里的几何 | `ink/geometry.ts`、`ink/outline.ts`、`render/scene.ts` + `docs/dev/guides/ink-rendering.zh-CN.md` | `pnpm test`，并目视两端同图对比 |
| `Board/Viewport/**` | `board/viewport.ts` | `pnpm test` |
| `Interaction/**`、`Interaction/Tools/**` | `interaction/pointer-router.ts`、`tools/*.ts`、`canvas/board-controller.ts` | `pnpm test` + 手工过一遍手势清单 |
| `Board/Commands/**`、`Board/Editing/**` | `board/commands.ts`、`board/editing/**` | `pnpm test` |
| `MainWindow.xaml`、`App.xaml`、设置页 XAML | `ui/**` 结构 + `styles/tokens.css` 常量 | `pnpm e2e`（必要时 `pnpm e2e:update` 并说明理由） |
| `Strings/**/*.resw` | 生成物 `i18n/locales/*.generated.ts` | `pnpm i18n` → `pnpm test` |
| `Settings/**`（模型/归一化/默认值） | `settings/model.ts`、`settings/settings-file.ts` | `pnpm test` |
| `Features/Shortcuts/**` | `interaction/shortcuts.ts` | `pnpm test` |
| `Features/Dock/**`（默认顺序/显示开关/编辑层） | `ui/shell/dock-layout.ts`、`ui/shell/DockBar.tsx`、`ui/shell/DockEditOverlay.tsx`、`settings/model.ts`、`settings/dock-order.ts` | `pnpm test` → `pnpm e2e` |
| `Controls/PageThumbnailControl.xaml.cs` | `render/thumbnail.ts` | `pnpm test` |
| `Features/Export/**` | `features/export/**` | `pnpm test`（PNG/PDF 结构测试） |
| `Features/Import/**` | `features/import/wbix-import.ts` | `pnpm test` |
| 只改 `docs/`、`installer/`、CI | 无需同步 | — |

**三条固定动作**（最容易漏）：

1. 动 `.resw` → `cd web && pnpm i18n`（否则 `i18n.test.ts` 会失败，但只有跑测试才发现）。
2. 动 WBIX → `pnpm interop`（`pnpm verify` 里**不含**这一步，它需要 .NET SDK）。
3. 动 UI 常量/布局 → `pnpm e2e`（基线会失败，看图确认差异是预期的再 `e2e:update`；基线变更要在提交说明里给出理由）。

## 5. 提交前门禁

网页端（在 `web/` 下执行）：

| 命令 | 覆盖 | 何时跑 |
|---|---|---|
| `pnpm typecheck` | `tsc -b`，含 src / tests / scripts / e2e | 总是 |
| `pnpm lint` | oxlint（`src` / `tests` / `scripts`） | 总是 |
| `pnpm test` | vitest（57 个测试文件 / 1400+ 例） | 总是 |
| `pnpm verify` | typecheck + test + build | 提交前最低要求 |
| `pnpm e2e` | Playwright 视觉 + 触屏回归（需本机 Chrome，不用下载 Chromium） | 动了 `ui/**` / `styles/**` |
| `pnpm interop` | 双端 WBIX 一致性 A~E（需 .NET SDK） | 动了 `persistence/wbix/**` 或桌面格式层 |
| `pnpm i18n` | 由 resw + web-strings 重新生成词表 | 动了文案 |
| `pnpm icons` | 重新生成 PWA 图标 | 改了图标脚本 |
| `pnpm perf` | 帧时间测量（手写脚本，结果人工判读） | 动了渲染/缓存 |

桌面侧（仓库根）：提交前仍按 `AGENTS.md` 跑零告警构建 + 全量单测。**改了两端相关代码时两边都要跑。**

CI（`.github/workflows/web.yml`）在 push/PR 时自动跑 `lint` → `typecheck` → `test`（含三组 conformance）→ `build`，并校验 `src/i18n/locales` 与词表同步。**CI 不跑 `pnpm e2e`**（视觉基线含中文字形，换机器即不可比）也**不跑 `pnpm interop`**（需要 Windows + .NET 10，默认只在 `workflow_dispatch` 勾选时跑），所以这两项仍是本地闸门。

## 6. 有意偏差（不要"顺手修正"）

以下差异是**刻意**的，改回去会破坏体验。动手前先读代码里的注释：

| 偏差 | 位置 | 理由 |
|---|---|---|
| 运行时用双精度，写盘才按 f32 取最短往返文本 | `persistence/wbix/f32-format.ts` + `canvas/board-controller.ts` | 全程 fround 会让每个采样点付舍入成本，且边界值更难解释；误差 ≤ 1e-7 相对值，在 0.05~32 缩放下远小于 1 DIP |
| 缩略图额外画形状 | `render/thumbnail.ts` | 桌面只画笔迹，「只有形状的页」缩略图会全空白 |
| 笔按下期间忽略触摸（掌拒护栏） | `interaction/pointer-router.ts` | 移动端手掌压屏的代价远高于桌面；删掉一个 `if` 即可回到逐字节等价 |
| 选择强调色固定 `#0078D4` | `render/selection.ts` | 浏览器拿不到系统 `SystemAccentColor`，桌面会用用户强调色 |
| 选择手柄带方向光标、「角优先于边」；框选矩形补 12% 填充 | `render/selection-handles.ts`、`render/selection.ts`、`styles/overlay.css` | 桌面是 XAML 浮层、无方向光标、框选无填充；网页端补可见性（常量可还原） |
| 新增 `Ctrl+A` / `Esc` / `Delete` / `Backspace` 键盘路径 | `interaction/shortcuts.ts` + `canvas/board-controller.ts` | 桌面选择操作只在画布浮层按钮上，无键盘入口；按浏览器用户预期补充（用户明确要求加） |
| 不识别 `Meta` / `Cmd` 组合键 | `interaction/shortcuts.ts` | 避免浏览器把 `Cmd+Z` 当成 `Ctrl+Z` 触发两次 |
| 静态层缓存门槛按**采样点数**（30k），桌面用脏矩形 | `render/render-handle.ts`、`render/static-cache.ts` | 贴图成本随画布像素走、重绘成本随点数走；宁可少优化也不让缓存更慢 |
| DPR 不设固定上限，只按后备存储的单边（16 384）与面积（67 108 864；iOS 16 777 216）反算夹取 | `render/render-handle.ts` | 固定上限（曾为 2）会让 DPR 2.5 / 3 的屏幕用低一倍的后备存储铺满窗口，浏览器再放大画布，笔迹边缘从 1 设备像素变成 2 像素，这就是「网页版笔迹比桌面糊」的根因。夹取只在真的越过浏览器上限时发生，且绝不把 <1 的 DPR 抬高 |
| 元素卡文案走词表、徽标用 Unicode/Fluent 图标、正文不自动换行（只按换行符裁切） | `render/element-card.ts` | 无 Segoe Fluent Icons 与 DirectWrite 排版引擎；`RenderContext2D` 也不暴露 `measureText`，尺寸配色仍按桌面真值 |
| 设置为模态 `Dialog` + 侧栏，非独立窗口 | `ui/dialogs/SettingsWindow.tsx` | 浏览器只有一个窗口，无法复刻 `AppWindow` |
| 不渲染「最小化」Dock 项（设置里仍保留该 id）；设置持久化用 `localStorage` | `ui/shell/DockBar.tsx`、`settings/pages/DockPage.tsx`、`settings/service.ts` | 浏览器没有对应能力；导出设置文件另名为 `windboard-settings.json` |
| 桌面与网页端 Dock 均为主界面就地编辑 + 精简设置页（仅「编辑 Dock」与「恢复默认布局」）；网页端无快捷入口 Dock | `Features/Dock/UI/DockEditOverlay.xaml`、`ui/shell/DockEditOverlay.tsx`、`settings/pages/DockPage.tsx` | 网页端编辑层用 Pointer Events 自己实现拖动（鼠标/触摸/笔），不引入拖拽库；窄屏（≤700px）改为可滚动的纵向分组。`dock.hiddenItems` 字段与归一化规则双端一致；v1 不实现快捷入口 Dock |
| Dock 文字用 `--wb-dock-text-primary`（89.4% 黑，不随主题） | `styles/tokens.css` | Dock 区强制浅色底板，深色主题下白字会看不见 |
| 自动保存 / 崩溃恢复 | `persistence/autosave*.ts`、`ui/dialogs/RecoveryDialog.tsx` | 网页端增强，桌面没有；不是移植项 |
| 不渲染标题栏 | `ui/shell/AppShell.tsx`（原 `TitleBar.tsx` 已删除）、`styles/shell.css` | 桌面的 `TitleBar` 是给无边框窗口做拖拽区与标题显示的；浏览器窗口自带标题栏，PWA 由系统绘制，再画一条白占 32 DIP（手机上是整整一行）。窗口标题仍在 `<title>`/PWA `name` 里，安全区改由 `.wb-body` 的 `padding-top` 承担 |
| 窄屏（≤700px）改用单段紧凑栏，页面组与导入/导出/设置/全屏收进「更多」浮层 | `ui/shell/use-narrow-layout.ts`、`ui/shell/DockBar.tsx`、`styles/shell.css`、`styles/dock.css` | 桌面窗口放得下三段 Dock；360px 下三段实测 198+395+62，互相压叠且按钮点不到 |
| 安全区内边距 `env(safe-area-inset-*)` + `viewport-fit=cover` | `index.html`、`styles/shell.css` | 刘海屏/圆角屏会遮挡标题栏与 Dock；桌面无此概念 |
| 窄屏设置卡片改为纵向堆叠 | `styles/settings.css` | 360px 下控件会把标题区挤成 0 宽（实测竖排一字一行） |
| 强调色按用途分层：设置/对话框 `AccentFillColorDefault`、选区 `#0078D4`、Dock `#1976D2` | `styles/winui-tokens.css`、`app/theme.ts`、`styles/tokens.css` | 浏览器读不到系统强调色；三者对应桌面不同用途，不能互相替换 |
| 不伪造 Mica，设置窗口用 `SolidBackgroundFillColorBase` | `styles/settings.css`、`styles/winui-tokens.css` | 浏览器无法获取桌面壁纸/材质；这是有意的实心降级 |
| 字体只用系统栈（Segoe UI Variable → Segoe UI），不打包、不请求 CDN | `app/theme.ts` | 离线 PWA 不能依赖网络字体；Windows 10 自动回退 |
| 移植 `@pstrikez/fluent-winui-theme@0.2.0` 的令牌与组件规则 | `styles/winui-tokens.css`、`styles/winui-controls.css`、`styles/winui/NOTICE.md` | MIT；只移植令牌/规则，不引入依赖、字体、AppShell 或组件包装层 |

## 7. 已知受限项

- **视觉基线只适合本机 / 同镜像 CI**：截图含中文字形，跨环境需把字体打包进页面（属设计变更）。
- **无 iPad / Safari 真机验证**：iOS 的 canvas 尺寸上限、`PointerEvent` 压感行为只做了代码层防护（`MAX_CANVAS_AREA_PIXELS = 16 777 216`）。
- **CI 只跑 `web/` 的 verify 作业**：`.github/workflows/web.yml` 覆盖 lint / typecheck / test / build / i18n 漂移；`pnpm e2e`（需真实 Chrome 与字体一致的基线）与 `pnpm interop`（需 Windows + .NET 构建 WinUI）仍是本地闸门，`interop` 只在手动触发时跑。
- **组件级测试只到冒烟**：`ui-shell-smoke.test.tsx` 覆盖挂载与关键交互，视觉正确性依赖 Playwright 基线。
- **两端没有交叉像素比对**：几何一致性靠 `ink-rendering` 文档 + 双方各自的测试，不靠图像 diff。

### 7.1 完全没有自动保障、只能人工核对的部分

这些地方改了桌面**不会让网页端任何测试变红**，必须人工过一遍：

| 项目 | 现状 |
|---|---|
| `MainWindow.xaml` / `App.xaml` / 设置页 XAML 的数值（圆角、间距、尺寸） | `tests/conformance/desktop-tokens.test.ts` 已覆盖 20 余项；**未加规则的新令牌仍需人工核对**（新增令牌时请顺手加一条规则） |
| 两端渲染像素 | 桌面 D2D 快照与网页 Canvas 各自独立，无图像 diff |
| `UI/MainWindow/MainWindow.*.cs` 的编排时序（导出/导入/分页/快捷键） | 网页端 `ui/shell/**` 按行为重写，无对账测试 |
| 设置默认值与归一化规则 | 默认值已由 `WindBoard.Tests/Fixtures/settings/defaults.json` 强制两端一致；**归一化规则**（非法值回落）仍各自单测 |
| 导出文件命名 / 画幅 / DPI 计算 | `Features/Export/**` 与 `features/export/**` 各自单测 |
| Dock 默认顺序 | 已包含在设置默认值语料里（四组顺序 + 隐藏项 + 快捷入口显示开关） |
| 快捷键表 | 默认绑定（`Ctrl+Z`/`Ctrl+Y`）在设置语料里；**按键解析与冲突检测**仍各自单测，不锁行为 |
| WinUI 模板度量（InfoBar、元素卡） | `generic.xaml` 里的值手工抄进 `element-card.ts` / `chrome.css` |
| 系统强调色 | 桌面取 `SystemAccentColor`，网页固定 `#0078D4`，永远无法自动一致 |
| e2e 视觉基线 | 只覆盖 8 个用例 / 10 张图，且含中文字形，换机器即不可比 |

## 8. 网页端编码约定（与仓库默认不同）

`web/` 是仓库里唯一的 TypeScript 子工程，以下约定与根 `.editorconfig` 不同，改代码前先确认：

- **行尾 LF、缩进 2 空格**（`web/.editorconfig` 覆盖根配置的 CRLF + 4 空格；根 `.gitattributes` 里 `web/** text eol=lf`）。工具批量写回 CRLF 时要先归一化再提交，否则 `git commit` 会报 "CRLF will be replaced by LF"。
- **相对 import 带扩展名**（`from './scene.ts'`），与 `allowImportingTsExtensions` 一致。
- **不使用 default export**，唯一例外是 `src/App.tsx`（`main.tsx` 按入口约定引入）。
- **领域层不碰浏览器**：`board/**`、`ink/**`、`persistence/**`、`features/**` 的纯计算部分不得 import React、不得访问 `window`/`document`，这是让它们能在 node 环境跑单测的前提。DOM 适配集中在 `canvas/dom-input.ts` 与 `canvas/dom-types.ts`。
- **测试入口**：`vite.config.ts` 的 `test.include` 只收 `tests/**/*.test.ts` 与 `tests/**/*.test.tsx`；默认 node 环境，组件测试在文件头写 `/** @vitest-environment jsdom */`。断言对象用 `toEqual`（`toBe` 是 `Object.is`，会失败）。
- **副作用必须可注入**：导出文件名取 `now`、自动保存取 `setTimeoutFn`/`clearTimeoutFn`、离屏渲染取 `surfaceFactory`，否则测试要劫持全局时钟。
- **oxlint 的四个坑**（都踩过）：内部函数名 `useXxx` 会被 `react/rules-of-hooks` 当成 Hook（改叫 `shouldUseXxx`）；`react(refs)` 禁止渲染期读 ref（要展示的值镜像进 state）；`react(purity)` 禁止渲染期调用 `Date.now()`（用 `useState(() => Date.now())`）；`react(set-state-in-effect)` 禁止在 effect 里同步 setState。
- **覆盖 Fluent 组件的样式必须双类选择器**（`.fui-DialogSurface.wb-settings-surface`），单类会被 Griffel 的原子类盖掉，详见 §3.6。

## 9. 相关文档

- `docs/dev/guides/wbix.zh-CN.md`：`.wbix` 格式与版本兼容契约。
- `docs/dev/guides/ink-rendering.zh-CN.md`：笔迹/图形几何规范（绘制顺序、笔宽半径、箭头翼、常量表）。
- `docs/dev/guides/localization.zh-CN.md`：本地化约定（resw、`L10n`、key 审计）。
- `web/README.md`：网页端深入说明（命令、目录、WBIX 字节契约细节、一致性测试台五步）。
- `.trellis/tasks/10-31-web-port/`：立项期的 PRD、设计、阶段记录与调研（**历史资料**，不含后续变更）。
