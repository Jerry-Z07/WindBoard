# 技术设计：WindBoard Web

> 事实源：`research/01-wbix-format-implementation.md`（文件格式）、`research/02-rendering-interaction-spec.md`（渲染与输入）、`research/03-ui-inventory.md`（界面清单）、`research/04-web-stack-research.md`（技术选型）。

## 1. 设计原则

1. **桌面版是唯一规格来源**。所有几何、常量、交互阈值以 C# 源码为准；`docs/assets/images/` 下的截图是历史版本，不作为依据。
2. **域模型零 DOM 依赖**。`board/`、`ink/geometry.ts`、`persistence/`、`features/export` 的纯计算部分不 import React、不碰 `window`，以便用 vitest 做纯逻辑测试（对应桌面 `WindBoard.Tests` 的定位）。
3. **命令式画布 + 声明式外壳**。canvas 由普通类持有，绝不放进 React 状态；React 只负责标题栏、Dock、弹出面板、设置窗口。
4. **互操作先于功能**。P0 阶段先打通双端 `.wbix` 一致性测试台，再写任何绘画代码——格式错了，后面全部白做。
5. **可降级**。所有浏览器专有能力（FSA、`file_handlers`、OPFS）必须有通用回退路径，回退后功能仍完整（体验略降）。

## 2. 分层映射

| 桌面（C#） | Web（TS） | 备注 |
|---|---|---|
| `Board/BoardDocument` | `board/model/document.ts` | 笔迹/形状/元素列表，列表顺序即 z 序 |
| `Board/Editing/BoardSession` | `board/session.ts` | `IBoardCommand` → `Command { do, undo }` |
| `Board/Editing/BoardWorkspace` | `board/model/workspace.ts` | 多页，恒 ≥1 页 |
| `Board/Viewport/BoardViewport` | `board/viewport.ts` | 纯函数，缩放 0.05~32 |
| `Board/Elements/*` | `board/model/element.ts` | 文本/链接/媒体/文件元素卡 |
| `Rendering/Board/BoardSceneRenderer` | `ink/scene-renderer.ts` | Canvas2D |
| `Rendering/DxSwapChainPanelRenderer` | `ink/frame-scheduler.ts` | rAF + 脏区 + 运动降质 |
| `Interaction/BoardInputController` | `interaction/pointer-router.ts` | 指针路由决策表 |
| `Interaction/Tools/*` | `interaction/tools/*.ts` | 笔/形状/橡皮/选择 |
| `Controls/BoardCanvasControl` | `ui/shell/BoardSurface.tsx` + `ink/canvas-host.ts` | React 壳 + 命令式 canvas |
| `MainWindow.*` | `ui/shell/*` | 标题栏/Dock/提醒栈 |
| `Board/Persistence/Wbix` | `persistence/wbix/*` | 互操作核心 |
| `Features/Export|Import` | `features/export|import` | PNG/PDF/WBIX |
| `Settings/*` | `settings/*` | 模型 + 服务 + 页面 |
| `AppSettingsService` | `settings/service.ts` | 单例 + 事件订阅 |
| `L10n` + `.resw` | `i18n/*`（构建期生成） | 见 §11 |

## 3. 目录结构

```
web/
├─ package.json  pnpm-lock.yaml  vite.config.ts  tsconfig.json  index.html
├─ .editorconfig                 # 覆盖根配置：web 用 LF、2 空格
├─ public/  manifest.webmanifest  icons/
├─ src/
│  ├─ main.tsx  app/App.tsx
│  ├─ board/     model/{document,page,workspace,stroke,shape,element,ids}.ts
│  │             viewport.ts  session.ts  commands/*.ts
│  ├─ ink/       geometry.ts  outline.ts  scene-renderer.ts  rasterizer.ts  frame-scheduler.ts
│  ├─ interaction/ pointer-router.ts  gesture.ts  hit-test.ts  tools/{pen,shape,eraser,select}.ts
│  ├─ persistence/ wbix/{json-options,f32-format,manifest,page-payload,ink-codec,paths,read,write,errors}.ts
│  │               autosave.ts  settings-store.ts
│  ├─ features/  export/{png,pdf,flow,names}.ts  import/flow.ts  dock/{items,order}.ts
│  ├─ platform/  io.ts  fs-access.ts  download.ts  launch-queue.ts
│  ├─ settings/  model.ts  service.ts  pages/*.tsx
│  ├─ i18n/      index.ts  zh-CN.json  en-US.json   # 生成物，提交入库
│  ├─ ui/        shell/{TitleBar,Dock,ReminderStack,BoardSurface}.tsx
│  │             flyouts/{Pen,Eraser,Shape,Pages,More}.tsx
│  │             canvas/{SelectionOverlay,ShapeQuickPanel,SelectionActions}.tsx
│  │             dialogs/{ExportDialog,ImportDialog,SettingsWindow}.tsx
│  └─ styles/    tokens.css  shell.css
├─ tests/
│  ├─ fixtures/  wbix/*.wbix  model/*.json  canonical/*.json
│  ├─ unit/**/*.test.ts        conformance/*.test.ts
└─ scripts/      gen-i18n.mjs  interop.mjs  verify.mjs
```

`web/` 不加入 `WindBoard.slnx`，桌面构建与测试完全不受影响（AC10）。

## 4. 技术选型

| 关注点 | 选择 | 理由 |
|---|---|---|
| UI 框架 | React 19 + TypeScript | Fluent UI v9 的官方载体就是 React；生态最成熟 |
| 设计系统 | `@fluentui/react-components` 9.x（MIT） | Fluent 2 与 WinUI 3 同一设计语言，控件近乎 1:1 映射 |
| 构建 | Vite | 冷启动快，PWA 插件成熟 |
| 画布 | Canvas2D（无 WebGL） | 需要的是精确几何与正确性，不是纹理笔刷；数千笔迹足够 |
| ZIP | `fflate`（MIT，~8kB） | 流式、体积小；`.wbix` 是内存内小包，无需 zip.js |
| PDF | `pdf-lib`（MIT） | 每页一张整页位图，能力足够；API 稳定 |
| PWA | `vite-plugin-pwa`（MIT，Workbox） | 生成 SW 与预缓存清单，符合"SW 静默更新"决策 |
| 测试 | vitest（单元）+ Playwright（视觉回归/端到端） | 与 Vite 同栈；Playwright 可截图对比 |
| 状态 | 无全局状态库 | 域模型自带订阅；设置用单例 + `useSyncExternalStore` |

版本在 P0 一次性冻结进 `package.json`，本文不承诺具体小版本。依赖许可证全部为 MIT/BSD/Apache-2.0，不引入 tldraw（其许可证禁止生产使用）。

## 5. 关键设计决策

### D1 画布不进 React 状态
`CanvasHost` 类持有 `<canvas>`、`BoardSceneRenderer`、`FrameScheduler`，订阅 `BoardSession` 的变更事件后自行标脏重绘。React 只渲染覆盖层（选择框、手柄、操作条、形状快捷面板）。**理由**：pointermove 每帧触发 React 重渲染会拖垮书写延迟；桌面版同样是命令式渲染。

### D2 笔迹几何：世界空间骨架 + 屏幕空间轮廓
桌面用 ID2D1Ink（贝塞尔骨架 + 圆头笔尖逐段插值半径），Canvas2D 没有等价物，因此：
- **骨架**（权威数据）：`points[{x, y, pressure}]` + `baseSize` + `enablePressure`，与 `.wbix` 完全一致，不做任何平滑或重采样。
- **轮廓**：把骨架展开为填充多边形（圆头折线 / 变宽胶囊链）。展开容差按当前缩放取 0.25 屏幕像素，缓存按 `ceil(log2(zoom))` 分桶；缩放跨桶时重建该笔迹的 `Path2D`。
- **理由**：单点笔迹必须是实心圆、变宽段必须线性插值、端帽必须圆头——只有填充轮廓能同时满足；`stroke()` 无法表达变宽。分桶缓存保证 1 万笔迹时平移/缩放仍然流畅（AC9）。

### D3 双端 float32 一致性
`.wbix` 中所有坐标/尺寸/压感都是 `float`（`System.Numerics.Vector2/4`）。TS 的 number 是 double，必须：
- **读**：每个 float 字段过 `Math.fround`。
- **写**：输出 float32 的**最短往返十进制**（`f32Format`），并与 .NET 的 `float.ToString("R")` 逐字符对齐（含指数写法 `1E-07`）。

### D4 JSON 文本形态对齐（尽力）
序列化器未设置 `Encoder`，因此桌面写出的非 ASCII（如中文元素文本、`displayName`）会被转义成 `\uXXXX`。互操作本身不需要这一条（任何 JSON 解析器都能读 raw UTF-8），但**对齐后可以做逐字节 diff**，让格式漂移在测试里立刻暴露。因此：
- 缩进 2 空格、LF、无尾随换行、`null` 成员照常输出、camelCase；
- 字符串按 `JavaScriptEncoder.Default` 的规则转义（非 ASCII + `< > & ' + "`）；
- 字节级一致列为 P0 的**目标**，语义级一致是**硬性要求**。二者冲突时以语义为准。

### D5 帧调度：全量重绘 + 包围盒剔除 + 运动降质
浏览器没有 `Present1` 脏矩形，改用：
1. 每帧 `ctx.setTransform(world→screen)`，遍历可见笔迹按 `Path2D` 填充（包围盒剔除）；
2. 书写中只重绘当前笔迹所在矩形（`clip` + 局部 `clearRect`），静止时再全量校正一次；
3. 平移/缩放进行中按 0.95 降采样渲染（对应桌面 `BoardCanvasControl.xaml.cs:550-588` 的 1.0/0.95/0.25 分档），空闲 150ms 后恢复 1.0。
若 P5 实测不达标，再引入分块位图缓存（设计已预留 `frame-scheduler` 抽象）。

### D6 输入路由：一张决策表
`pointer-router.ts` 把桌面 `PointerRoutingDecisions.cs` 的判断顺序照搬为纯函数 `decide(ctx): Action`，`ctx = {pointerType, button, buttons, modifiers, activePointers, tool, phase}`。**理由**：路由是回归重灾区，做成纯函数后可以用表驱动测试逐条覆盖桌面分支。

### D7 文件能力抽象为 PlatformIO
```ts
interface PlatformIO {
  saveFile(name: string, data: Blob, accept: FilePickerAcceptType[]): Promise<'saved' | 'cancelled'>;
  openFiles(accept: FilePickerAcceptType[], multiple: boolean): Promise<File[]>;
  pickDirectory(): Promise<DirHandle | null>;
  writeInto(dir: DirHandle, relPath: string, data: Blob): Promise<void>;
}
```
- Chromium：File System Access API（`showSaveFilePicker` / `showDirectoryPicker`），用户取消抛 `AbortError` 视为 `'cancelled'`（不是错误）。
- Firefox/Safari：`<a download>` + `<input type=file>`；多文件 PNG 导出降级为**逐个下载 + ZIP 打包**（一次 `.zip` 而非散落文件）。
- 拖拽打开全平台可用，作为主要入口之一。

### D8 自动保存用 OPFS，元数据用 IndexedDB
OPFS 存每页的 `.wbix` 分片快照（worker 内 `createSyncAccessHandle`），IndexedDB 存工作区索引、设置、最近打开列表。自动保存节流 2s，页面 `visibilitychange`/`pagehide` 时强制落盘。**理由**：OPFS 适合大二进制，IndexedDB 适合小结构化数据；两者都是本地优先 PWA 的标准组合。

### D9 不做 Dock 快捷入口与屏幕批注
浏览器无法启动外部程序，快捷 Dock 天然不可实现；屏幕批注需要覆盖其他应用，`getDisplayMedia` 语义不同。二者在 v1 中移除，设置页对应项不出现（不留死 UI）。Dock 的拖拽排序对**左 Dock / 工具 / 页面 Dock** 三组保留。

### D10 设置文件兼容桌面版
桌面设置导出格式在 P0 阶段读取 `Settings/` 源码确认（字段名、容器结构、版本字段）。Web 端设置模型字段名与桌面一致，导入时按字段白名单合并、忽略未知字段、缺失字段取默认值；导出时写出桌面能读的完整结构。**理由**：用户决策要求"保留且兼容桌面版设置文件"。

### D11 版本契约：读侧分派（升级现有实现）
**现状问题**：`version` 只做 `1..3` 门禁（`WbixWorkspaceSerializer.Load.cs:45`），解析完全不看版本，靠形态嗅探兜住三种形态。后果是版本号**不提供任何保护**，将来需要 v4 时没有落点。

**设计**：
- 引入 `IWbixPageReader { int MinVersion; int MaxVersion; ValueTask<BoardPageSnapshot> ReadPageAsync(...); }`（实现取**版本区间**而非单值 `SupportedVersion`：v1/v2 形态相同，用区间可省掉一个几乎相同的 reader，且区间不重叠即可保证唯一命中）
  - `WbixPageReaderV1V2`（1..2）：扁平形态（`points`/`colorRgba`/`baseSize`/`enablePressure` 直接在条目根上）
  - `WbixPageReaderV3`（3..3）：包装形态（`{kind, stroke|shape}`），**允许**扁平条目兜底并 Warn 一次（保持今天的行为）
  - 公共逻辑在 `WbixPageReaderBase`：路径规范化 → 安全校验 → 取 Zip 条目 → 按读模式反序列化 → 元素解析
- `WbixPageReaderRegistry.For(version)` 选择解析器；未知版本沿用既有文案 `Wbix_VersionNotSupported_Fmt` 抛 `InvalidDataException`
- 写侧只写 `CurrentVersion`（`WbixWorkspaceSerializer.cs:22`）
- `InkItemSnapshotJsonConverter` 增加读模式（`LegacyFlat` / `Wrapped`），由 `JsonSerializerOptions` 实例注入，**不改公开 API**

**为什么保留 v3 的扁平兜底**：这是唯一的跨版本宽容，成本为零，且能救活"manifest 写 3 但条目是旧形态"的畸形文件。v4 起不得复用该兜底。

**新增破坏性变更的流程**：新增一个 reader + 提升 `CurrentVersion` + 补 golden 文件。既有 reader 一行不改。

**兼容性**：对外行为与今天等价（同一批文件读出来逐字段相同），因此 AC10 成立。

### D12 字节级固化：golden 语料
**问题**：50 个 `.wbix` 相关测试全是"写→读回来对不对"，没有一个锁定"写出来的字节长什么样"。任何人改模型、加 `[JsonIgnore]`、或 .NET 升级调整浮点格式化，都不会有测试失败。

**设计**：`WindBoard.Tests/Fixtures/wbix/` 作为**单一事实源**：

```
model/*.json           语言中立文档描述（人工编写，覆盖边界）
expected/entries.txt   期望的 zip 条目名与顺序（逐行）
expected/*.golden.json 各条目解压后的期望文本
*.wbix                 桌面版真实导出（v1/v2/v3 各若干）
```

断言三件事：条目名与顺序、每条解压文本**逐字节**相等、`createdUtc` 格式符合预期。

**字节形态的四个必须显式钉死的点**（P0 实测得出，都是"不写下来就会漂移"的地方）：
- **行分隔符固定 CRLF**：`JsonSerializer` 的缩进换行在 Windows 上取 `Environment.NewLine`，即平台偶然；写侧显式设 `JsonSerializerOptions.NewLine = "\r\n"`，把 CRLF 变成格式契约而不是平台产物。文件末尾**无**尾随换行。
- **浮点是 float32 最短往返**，不是 `G9`：`4CEB79A3` 的 G9 是 `123456792`，实际写出 `123456790`；整数不带小数点（`1000000`、`12`）；`-0` 保留负号；科学计数法判定为 `e >= 9` 或 `e < -4`（`e` 为最短表示的十进制指数），指数形如 `1E+09`/`1E-45`（大写 `E`、正号必写、至少两位）。
- **`createdUtc` 小数位 0~7 位可变**（STJ 去掉末尾 0），偏移恒为 `+00:00` 而非 `Z`。
- **非 ASCII 与 HTML 敏感字符一律大写 `\uXXXX`**：`<`→`\u003C`、`&`→`\u0026`、`"`→`\u0022`（不是 `\"`）、emoji 拆代理对。

两个必须绕开的非确定性：
- `CreatedUtc` 直接取 `DateTimeOffset.UtcNow`（`.Save.cs:138`），**无可注入时钟** → 测试内先正则校验格式，再把该值替换为固定串后比对。不为此改动生产代码。
- zip 条目时间戳每次不同 → 只比解压内容，不比 zip 原始字节。

**同一批语料同时供 web 端 conformance 使用**（web 通过相对路径读取），避免两份 fixture 各自漂移。

## 6. 域模型契约

```ts
type F32 = number;                     // 已在边界 fround 过
interface StrokePoint { x: F32; y: F32; pressure: F32 }
interface Stroke {
  kind: 'stroke';
  points: StrokePoint[];
  colorRgba: { x: F32; y: F32; z: F32; w: F32 };
  baseSize: F32;
  enablePressure: boolean;
}
type ShapeKind = 'line' | 'rect' | 'ellipse' | 'arrow';
interface Shape {
  kind: ShapeKind;
  start: { x: F32; y: F32 }; end: { x: F32; y: F32 };
  colorRgba: { x: F32; y: F32; z: F32; w: F32 };
  width: F32;
}
type InkItem = Stroke | Shape;
type Layer = 'belowInk' | 'aboveInk';
interface BoardElement {
  id: string; layer: Layer; order: number;
  positionWorld: { x: F32; y: F32 }; sizeWorld: { x: F32; y: F32 };
  type: 'text' | 'link' | 'media' | 'file';
  text?: string; url?: string; title?: string; kind?: string;
  sourcePath?: string; displayName?: string; resourceId?: string;
}
interface BoardPage { id: string; ink: InkItem[]; elements: BoardElement[]; session: BoardSession }
interface Command { do(doc: BoardDocument): void; undo(doc: BoardDocument): void }
```

命令粒度与桌面严格一致：一笔/一个形状 = 一条 `AddItem`；**一次橡皮手势 = 一条 `ReplaceStrokes`**；一次选择变换 = 一条 `Composite`；清屏 = 一条 `Clear`。撤销栈每页独立、无上限、执行时清空重做栈。

## 7. 渲染契约

| 项 | 规则 | 来源 |
|---|---|---|
| 坐标 | `screen = (world − camera)·zoom + viewportSize/2` | `BoardViewport.cs:59-71` |
| 背景 | 纯色，默认 `#2E2F33`；无网格/纸张/点阵 | `BoardCanvasControl.xaml.cs:38` |
| 绘制顺序 | belowInk 元素 → 笔迹（列表序）→ aboveInk 元素 → 实时预览 | `BoardSceneRenderer.cs:267-292` |
| 单点笔迹 | 实心圆，`r = max(0.5, baseSize·wf/2)` | `:452-457` |
| 笔宽 | `wf = enablePressure ? clamp(pressure, .1, 1) : 1` | `:1048-1052` |
| 折线 | 相邻点直连，半径线性插值，圆头圆角，miter 2.0 | `:1338-1358` |
| 箭头 | 两条 3×width、±30° 的翼 | `:414-415` |
| 手柄 | 8 个 14 DIP 白色方块 | `BoardCanvasControl.xaml:55-72` |
| 光标 | 橡皮 48×72 DIP 椭圆 | 桌面常量 |

## 8. 输入契约

| 场景 | 行为 | 来源 |
|---|---|---|
| 笔 / 鼠标左键 | 绘制（世界距离 > `0.5/zoom` 才追加点） | `PenTool.cs:25,93-119` |
| 鼠标右键拖拽 | 平移画布 | `PointerRoutingDecisions.cs:74-101` |
| 滚轮 | 以光标为锚点缩放，`f = 1.1^(Δ/120)`，夹 0.05~32 | `BoardInputController.Manipulation.cs:129-132` |
| 水平滚轮 | 平移 `−Δ·1.0` DIP | 同上 |
| 单指触摸 | 绘制 / 框选 / 移动选择 | `:119-143` |
| 2 指以上 | 平移 + 缩放（在选区上则变换选区） | `:195-240` |
| 选择工具 + Ctrl/Shift 滚轮 | 缩放 / 旋转选区（5°/格） | `:63-95` |
| 双击元素 | 500ms 内、位移 ≤10 DIP 则打开 | `BoardInputController.Open.cs:19-37` |
| 抬笔 | 点数 ≤1 的笔迹丢弃 | `:154-187` |
| 掌拒 | `pointerType === 'pen'` 活动时忽略 `touch` | 桌面同策略 |
| 快捷键 | 仅 `Ctrl+Z` / `Ctrl+Y`，可改绑，须含 Ctrl 或 Alt，输入框聚焦时忽略 | `ShortcutsFlow.cs:62-79` |

采样：优先 `pointerrawupdate`，回退 `getCoalescedEvents()`；鼠标 `pressure` 固定按 0.5（与 WinUI 一致），触摸按下视为 1。

## 9. WBIX 互操作契约

**写出**（固定 v3）：`manifest.json`（最后写）+ `pages/page-{i:000}.json` + `assets/cover.png` + `assets/elements/{elementId:N}{ext}`；`null` 成员输出；`format="wbix"`、`version=3`、`createdUtc` 用 UTC ISO-8601。

**读入**：按 `manifest.pages[].index` 排序；路径规范化 `\`→`/`、必须 `pages/` 前缀、禁 `..`；容忍并跳过（不中断）单个坏元素、未知元素类型、未知 ink kind、`strokes:null`、不安全资源路径；`kind` 缺省视为 `stroke`；v1/v2 扁平条目按"有 `points` 即 stroke、否则按 shape 嗅探"包装。致命错误（缺 manifest、`format` 不符、version 越界、页 payload 为 null）抛 `InvalidDataException` 等价错误。解析器按 `version` 分派（见 D11）：v1/v2 → 扁平解析器；v3 → 包装解析器，允许扁平条目兜底并 Warn 一次。

**一致性测试台**（P0 交付物）：

语料放在 `WindBoard.Tests/Fixtures/wbix/`（见 D12），web 端通过相对路径读取同一份，**不复制**：

```
model/*.json              语言中立文档描述（人工编写，覆盖边界）
*.wbix                    桌面版真实导出（v1/v2/v3 各若干），作为读入语料
expected/entries.txt      期望 zip 条目名与顺序
expected/*.golden.json    期望的解压文本（字节级）
```

web 侧只在自己的 `.interop/` 下写生成物（`produced/*.wbix`、各类 dump），不污染语料目录。

`web/scripts/interop.mjs` 按序执行五步并逐项 diff：

| 步骤 | 执行方 | 内容 |
|---|---|---|
| A | TS | 读语料 `.wbix` → 规范化 dump |
| B | C# | 读同一批 → 规范化 dump；A 与 B 必须相等 |
| C | TS | 由 `model/*.json` 写 `produced/*.wbix` |
| D | C# | 读 `produced/*.wbix` → dump；与 model 的期望 dump 相等 |
| E | C# | 重新写出 `produced-cs/*.wbix`；TS 读回 → dump 与 D 相等 |

D12 的字节级 golden 断言是同一套语料的另一面：C# 写出的文本必须**逐字节**等于 `expected/*.golden.json`，TS 写出同理。这样"语义相等"和"字节相等"各有独立判据，不会互相掩盖。

**比对的边界（两条，必须在 dump 里实现）**：
- **只比"解压后的条目文本 + 条目名与顺序"，不比 zip 压缩字节、不比整包 SHA**。压缩级别（.NET `CompressionLevel.Optimal` vs fflate level 6）与 zlib 实现版本都不属格式契约，锁整包哈希只会制造假失败。
- **`media` 元素的 `sourcePath` 不参与比对**。桌面读包会把内嵌资源提取到临时文件并把 `sourcePath` 改写成该临时路径——这是平台相关的运行时行为，不是格式语义。dump 对 media 只保留 `resourceId` / `type` / `meta` / `displayName` / `positionWorld` / `sizeWorld` / `layer` / `order`。同理 `createdUtc` 只比存在性与格式，不比具体值（无可注入时钟）。

**dump 规范化**：对象键按序数排序、数组保序、所有数值输出为 float32 最短往返文本；dump 由**域模型**（而非原始 JSON）导出，这样比较的是"两端读出来理解成什么"，而不是"两端解析器碰巧怎么存"。

C# 侧不新增项目：dump 生成器放在 `WindBoard.Tests/Board/Persistence/WbixInteropDumpTests.cs`，通过环境变量 `WINDBOARD_WBIX_INTEROP_DIR` 接收目录，由脚本以 `dotnet test --filter` 触发。**理由**：不改变现有解决方案结构（AGENTS.md 要求），测试项目已引用主工程且可访问 `internal`。

## 10. 测试策略

| 层级 | 工具 | 覆盖 |
|---|---|---|
| 单元 | vitest | 视口数学、轮廓展开、命中测试、命令栈、f32 格式化、JSON 转义、路径校验 |
| 互操作 | vitest + dotnet test | §9 的五步 diff |
| 视觉 | Playwright 截图 | Dock 尺寸/间距、弹出面板、设置页；参考图由桌面版在固定窗口尺寸下截取 |
| 端到端 | Playwright | 打开 `.wbix` → 书写 → 撤销 → 导出 → 重新打开；离线（`context.setOffline`） |
| 手工 | 真机 | Surface Pen / iPad Safari 压感与掌拒 |

渲染快照的容差：结构与布局要求像素级接近（Mica/亚克力区域除外，那部分用不透明近似，只比对结构）。

## 11. 设置与本地化

- **设置模型**：字段名与桌面一致；分组 常规 / 外观 / 书写（笔 + 橡皮 + 形状）/ 快捷键 / Dock / 关于。移除：伪装、自动更新检查、Debug 崩溃测试、屏幕批注、托盘与开机启动。
- **本地化**：`scripts/gen-i18n.mjs` 读取 `WindBoard/Strings/{zh-CN,en-US}/*.resw`，合并为 `src/i18n/{lang}.json`（key 原样保留，含 `Common_` 等前缀）。生成物提交入库；单元测试断言两种语言 key 集合相等且与 `.resw` 同步（对应桌面 `LocalizationKeyAuditTests`）。

## 12. 风险与缓解

| 风险 | 影响 | 缓解 |
|---|---|---|
| float32 文本格式与 .NET 不一致 | 语义相等但字节不等，diff 失效 | P0 先实测 `float.ToString("R")` 的真实输出并建表，覆盖 0.1/1e-7/1e38/1/3 等边界 |
| 变宽轮廓在 zoom 32 时出现折角 | 视觉与桌面不一致 | 轮廓容差按屏幕像素定义 + 分桶重建；P1 用桌面渲染快照做对比 |
| 1 万笔迹重绘掉帧 | AC9 不达标 | 包围盒剔除 + Path2D 缓存；预留分块位图（D5） |
| 浏览器不支持 FSA | 导出体验降级 | PlatformIO 回退；多页 PNG 降级为 ZIP |
| `file_handlers` 仅 Chromium | 双击打开不可用 | 拖拽打开作为主入口，双击作为渐进增强 |
| 桌面设置格式未调研 | D10 无法落地 | P0 读 `Settings/` 源码确认，列入 P4 前置 |
| 版本分派重构引入读取回归 | 既有文件读不出来 | 分派前后对同一批语料做逐字段比对；既有 50 个测试与新增 golden 同时把关；v3 保留扁平兜底 |
| 桌面端被无意改动 | AC10 失败 | 只允许"结构变化 + 行为不变"：读侧分派、新增测试与文档。行为等价性由既有 50 个测试 + 31 张快照 + 新增 golden 三重把关 |

## 13. 阶段划分

详见 `implement.md`：P0 契约先行 → P1 画布内核 → P2 选择与多页 → P3 UI 外壳 → P4 文件能力 → P5 打磨。每个阶段有独立可验收的产物，P0 不通过不进入 P1。
