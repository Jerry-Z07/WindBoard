# Web 端移植：WindBoard Web（PWA）

## Goal

以**纯静态、本地优先的 PWA** 形式复刻 WindBoard 桌面版，使 Windows 桌面用户与浏览器用户在**同一份 `.wbix` 文件上双向无损协作**，且操作方式与界面结构与桌面版一一对应。

成功的最小证据：桌面版导出的 `.wbix` 拖进浏览器打开后，页数、笔迹、形状、元素卡、当前页与桌面版完全一致；浏览器另存的 `.wbix` 再拖回桌面版同样完全一致。

## 已确认的产品决策

| 决策项 | 结论 | 日期 |
|---|---|---|
| 部署形态 | 纯静态本地优先 PWA，无后端、无账号、无云同步 | 2026-10-31 |
| v1 范围 | 核心书写闭环 + 完整 WBIX 互通 + PNG/PDF 导出 + 画笔/外观设置 | 2026-10-31 |
| 笔迹渲染 | 精确复刻桌面 D2D 几何（折线段 + 每段线性插值笔宽 + 圆头圆角） | 2026-10-31 |
| 伪装（进程名/图标） | 放弃 | 2026-10-31 |
| 自动更新 | 交给 Service Worker 静默更新，无版本检查对话框 | 2026-10-31 |
| 系统托盘 / 开机启动 / 最小化到批注 | 放弃 | 2026-10-31 |
| 设置导入导出 | 保留，且兼容桌面版设置文件 | 2026-10-31 |
| `version` 字段定位 | 升级为**真正的兼容契约**：读侧按版本分派，v4 有明确落点 | 2026-10-31 |
| 格式固化的三项动作 | 并入 P0，不单独开任务 | 2026-10-31 |

## 范围（v1 In Scope）

1. **书写闭环**：笔（压感）、形状（直线/矩形/椭圆/箭头）、橡皮（像素擦除 + 整笔擦除 + 滑动清屏）、选择（框选/移动/缩放/旋转/复制/置顶/删除）、撤销/重做、多页（增删切换）。
2. **视口**：无界画布，右键拖拽平移、滚轮以光标为锚点缩放、双指平移缩放，缩放范围 0.05~32。
3. **文件互通**：`.wbix` 导入（兼容 v1/v2/v3）与导出（固定写 v3）、PNG 导出（自动内容边界 / 固定画幅 / 批量）、PDF 导出（每页一图）。
4. **界面还原**：自定义标题栏、底部三段式 Dock、笔刷/橡皮/形状/页面弹出面板、InfoBar 提醒、设置窗口（常规/外观/书写/快捷键/Dock/关于）。
5. **本地优先**：自动保存到 OPFS、离线可用、拖拽打开 `.wbix`、Chromium 上注册 `file_handlers` 支持双击打开。
6. **双语**：zh-CN / en-US，复用桌面 `.resw` 作为唯一事实源。
7. **格式固化（桌面端，P0 内完成）**：`.wbix` 文本形态加入字节级回归测试；`version` 从"装饰性字段"升级为真正的兼容契约（读侧按版本分派）；笔迹几何补文字规范。这三项独立于网页端，桌面版自身受益。

## 非目标（v1 Out of Scope）

- 屏幕批注（浏览器无法覆盖其他应用；`getDisplayMedia` 方案语义不同，留待 v2 评估）
- `.wbi` 旧格式导入（内部用 ISF 序列化笔迹，浏览器解析成本过高；由桌面版另存为 `.wbix` 后互通）
- 伪装、系统托盘、开机启动、MSIX/安装器、更新对话框、安装迁移
- 账号、云同步、分享链接、协作编辑
- Dock 快捷入口（用户自定义外部程序/文件/链接按钮）——浏览器无法启动外部程序
- 高亮笔、激光笔、文本工具、形状填充（桌面版同样没有，保持一致）

## 约束

- **WBIX 是唯一互操作契约**，其权威描述见 `docs/dev/guides/wbix.zh-CN.md`；实现细节见 `research/01-wbix-format-implementation.md`。
- 桌面版 `version` 字段只做 1~3 门禁、不分支解析逻辑（`WindBoard/Board/Persistence/Wbix/Load.cs:45`），因此网页端一律写 v3。
- 序列化器无 `DefaultIgnoreCondition`（`WbixWorkspaceSerializer.cs:32-41`），`null` 成员必须输出。
- 数值类型是 `float`（`System.Numerics.Vector2/4`），必须按 float32 处理，不能当 double。
- 浏览器存储与文件能力存在实现差异，必须提供降级路径（详见 `design.md` 的 PlatformIO）。
- 不引入 tldraw（自定义许可禁止生产环境使用）；可参考 Excalidraw（MIT）。

## 验收标准

- [ ] AC1 桌面版导出的 `.wbix`（含 v1/v2/v3 三种语料）导入网页端后，页数、当前页、笔迹点数与压感、形状、元素卡位置尺寸、封面图全部一致，控制台零报错。
- [ ] AC2 网页端导出的 `.wbix` 能被桌面版打开且内容一致；桌面版再次导出后与网页端导出的**规范化语义 dump 相等**（允许 zip 时间戳与 JSON 转义差异）。
- [ ] AC3 双端一致性测试台在 CI 中双向通过：`C# 写 → TS 读`、`TS 写 → C# 读`、`TS 写 → C# 写` 三种方向的规范化 dump 均相等。
- [ ] AC4 PNG 导出：自动边界模式下内容范围、背景色、DPI 与桌面版一致；批量导出命名规则为 `WindBoard-{yyyy-MM-dd}` 文件夹 + `{yyyy-MM-dd}-{index+1:000}.png`。
- [ ] AC5 PDF 导出：页面尺寸按 `px*72/dpi` 换算，每页嵌入整页位图，能在系统阅读器中正常打开。
- [ ] AC6 界面结构：标题栏、三段式 Dock、四个弹出面板、InfoBar、七个设置页均存在且布局关系与桌面版一致；Dock 按钮尺寸 60×52、20px 图标 + 11px 文字、14px 圆角、底板 0.85 不透明度、强制浅色主题。
- [ ] AC7 操作一致性：笔工具**二次点击**才弹出颜色/粗细面板；橡皮弹出面板含两模式单选 + 滑动清屏；形状面板含四项；`Ctrl+Z` / `Ctrl+Y` 默认撤销重做且可在设置中改绑。
- [ ] AC8 离线：断网后刷新仍可书写、导入、导出。
- [ ] AC9 性能：1 万笔迹文档，单帧绘制与输入响应满足 60fps 目标（视口画布 + 脏矩形/离屏缓存）。
- [ ] AC10 无回归：桌面版 `dotnet build` 与 `dotnet test` 保持全绿；既有 50 个 `.wbix` 相关测试与 31 张渲染快照基线不得修改。允许新增读侧分派结构与测试，**不允许改变对外可观察行为**（同一批文件读出来的结果必须逐字段相同）。
- [ ] AC11 版本契约：`version` 成为真正的兼容契约——读侧按 `version` 选择解析器，`CurrentVersion` 只影响写侧；未知版本仍以既有文案拒绝；新增破坏性变更时有明确的落点（新增一个解析器 + 提升 `CurrentVersion`），不需要改动既有解析器。
- [ ] AC12 字节级固化：固定语料经写侧产生的 zip 条目名、条目顺序、各条目解压后的文本内容与检查入库的 golden 文件逐字节相等；任何格式漂移（缩进、键序、`null` 输出、数字格式、行分隔符）都会导致测试失败。**锁的是解压后的文本，不是 zip 压缩字节或整包哈希**——压缩级别与 zlib 实现版本不属格式契约。
- [ ] AC13 几何规范：笔迹渲染几何（折线段、每段线性插值半径、圆头圆角、单点圆、箭头翼）有可查阅的文字规范，并与 31 张渲染快照基线一致。

## 开放问题

| 问题 | 现状 | 待决时机 |
|---|---|---|
| 屏幕批注是否进 v2 | v1 不做 | v1 交付后 |
| 是否需要"桌面端批量把 `.wbi` 转 `.wbix`"的小工具 | 建议做，成本低 | P4 开始前 |
| 首版是否必须支持 iPad 触控书写 | 默认桌面优先，触屏做兼容性适配 | P5 开始前 |
| 视觉回归的参考截图如何取得 | 用 `WindBoard.UITests`（FlaUI）自动截图，需交互桌面 | P0 定方案，P5 落地 |

## 视觉体系统一（P11，2026-11-27）

### Goal

网页端当前同时存在两套视觉语言：设置窗口/对话框使用 Fluent UI v9 的 Web 默认主题（Segoe UI、白色平面、`#0F6CBD` 品牌蓝），Dock/浮层使用手写 CSS 模拟桌面 WinUI 3。目标是让网页端收敛为**一套与桌面 WinUI 3 一致的视觉体系**，只改视觉，不改交互、布局与数据契约。

### 已确认事实

- Fluent UI v9 的 `fontFamilyBase` 默认是 `Segoe UI`，`webLightTheme` 的品牌色是 `#0F6CBD`，中性底色是 `#FFFFFF`/`#FAFAFA`；这些是 Fluent 2 Web 的值，不是 WinUI 3。
- 桌面 WinUI 3 使用 Segoe UI Variable；主题字典（Light）为 `SolidBackgroundFillColorBase #F3F3F3`、`CardBackgroundFillColorDefault #B3FFFFFF`、`CardStrokeColorDefault #0F000000`、`TextFillColorPrimary #E4000000`、`ControlCornerRadius 4`、`OverlayCornerRadius 8`。
- 桌面 `SettingsCard` 模板：`Padding 16`、`MinHeight 68`、`CornerRadius = ControlCornerRadius`、`HeaderIcon 20`、`Description 12`。
- 桌面 `ContentDialog`：背景 `SolidBackgroundFillColorBase`、`Padding 24`、标题下边距 12、按钮间距 8、`MaxWidth 548`。
- 网页端现有三套蓝：`#0F6CBD`（Fluent 主题）、`#1976D2`（Dock 选中，桌面 `MainWindow.xaml` 固定值）、`#0078D4`（选择框，桌面 `SystemAccentColor` 默认值）。后两者是桌面有意设计，不是漂移；只有 `#0F6CBD` 是 Fluent Web 带入的。
- 社区包 `@pstrikez/fluent-winui-theme` 0.2.0 是唯一直接做 Fluent v9 → WinUI 3 的方案；令牌值与桌面主题字典一致，但 0.2.0、单维护者、peer 锁 Fluent `~9.74.4`（本项目 9.75.0）、2.46 MB、字体走 CDN，不适合直接依赖。

### 需求

1. **主题层**：新增网页端 WinUI 3 主题模块，App、Dock、浮层、覆盖层统一使用；不再有零散的 `webLightTheme`/`webDarkTheme`。
2. **令牌层**：建立 WinUI 语义的窗口/卡片/描边/控件填充令牌；文字色收敛到一套；强调色**按用途分层**：设置/对话框的强调按钮使用 WinUI `AccentFillColorDefault`（默认蓝调色板的 dark1 `#0067C0` 浅色 / light2 `#4CC2FF` 深色），选区保持桌面 `SystemAccentColor` 基线 `#0078D4`，Dock 选中保持桌面固定 `#1976D2`；三者不互相替换。
3. **字体**：正文使用 Segoe UI Variable，Windows 10 回退 Segoe UI；不打包字体、不依赖 CDN；中文回退 Microsoft YaHei UI。
4. **设置窗口**：设置对话框、左侧导航、设置卡片、图标按 WinUI 3 规格调整；卡片 16 内边距、68 最小高度、4px 圆角、20px 图标；导航项 36px 高、16px 图标；页面标题按 WinUI Title 字号 28/36。
5. **对话框**：导出/导入/确认/取色器/恢复对话框按 WinUI `ContentDialog` 规格：背景 `SolidBackgroundFillColorBase`、8px 圆角、24px 内边距、标题 20/28、按钮间距 8。
6. **Dock 与浮层**：保留桌面自定义的 14px 底板、10px 按钮、0.85 透明度、强制浅色；只统一强调色与内部 Fluent 控件（Slider/Radio/SpinButton/Menu/Popover）的视觉。
7. **参考实现移植**：从 `@pstrikez/fluent-winui-theme`（MIT）移植 WinUI 令牌字典和必要的组件规则，保留署名；不引入该包依赖、不引入 Meslo 字体、不加载微软 CDN。
8. **文档**：更新 `docs/dev/guides/web-port.zh-CN.md`，登记主题层、强调色分层与“不伪造 Mica”的有意偏差。

### 非目标

- 不改桌面端 XAML、`generic.xaml` 或桌面视觉。
- 不实现真正的 Mica/亚克力，不伪造壁纸材质；设置窗口使用 `SolidBackgroundFillColorBase` 实心底色。
- 不读取系统强调色（浏览器无此能力）。
- 不重写整套 WinUI 控件，不替换 Fluent 的交互/无障碍实现。
- 不改 Dock/浮层的布局与交互行为；不改画布渲染、WBIX、设置模型。
- 不做高对比度主题与 RTL（保持现状）。
- 不新增 `package.json` 依赖。

### 验收标准

- AC14：`web/src` 中除主题模块外零 `webLightTheme`/`webDarkTheme` 导入；Fluent Web 的 `#0F6CBD` 字面量清零；`#1976D2`（Dock 选中）与 `#0078D4`（选区）仍按桌面职责保留，但只能出现在主题/令牌模块。
- AC15：设置卡片、导航、对话框的度量与颜色逐条对上 WinUI 主题字典与 `SettingsCard`/`ContentDialog` 模板；浅色/深色两套都成立。
- AC16：计算样式字体族以 `Segoe UI Variable` 开头，Windows 10 回退 `Segoe UI`；页面不发起字体网络请求。
- AC17：Dock 的 60×52、14px 底板、10px 按钮、0.85 透明度、强制浅色不变；窄屏（≤700px）布局不变。
- AC18：`pnpm lint`、`pnpm typecheck`、`pnpm test`、`pnpm build`、`pnpm e2e` 全绿；视觉基线按预期重建并在提交说明给出理由。
- AC19：移植文件保留 MIT NOTICE 与来源注释；`package.json` 与 `pnpm-lock.yaml` 不新增依赖。

### 开放问题

无。已确认采用“移植参考实现”而不是直接依赖。
