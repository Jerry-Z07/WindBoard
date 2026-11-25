# WindBoard Web

WindBoard 的网页端（纯静态、本地优先的 PWA）。目标是与桌面版（WinUI 3）在 `.wbix` 文件上
**双向无损互通**，并最大化还原桌面端的操作方式与界面。

> 同步规范（桌面改了哪里、网页端该改哪里、跑什么命令）见
> [`docs/dev/guides/web-port.zh-CN.md`](../docs/dev/guides/web-port.zh-CN.md)。
> 以下内容只讲**怎么用这套工程**。

当前状态：P0（格式层）~ P5（性能 / 视觉回归 / 触屏 / i18n / PWA）已完成。画布、工具、
选择与元素、导出导入、设置、自动保存、离线安装能力均已实现。

## 命令

```bash
pnpm install        # 安装依赖
pnpm dev            # 本地开发服务器（http://localhost:5173）
pnpm typecheck      # 类型检查（tsc -b，覆盖 src / tests / scripts / e2e）
pnpm lint           # oxlint（src / tests / scripts）
pnpm test           # 单元测试 + 跨端一致性测试（vitest）
pnpm build          # 生产构建（tsc -b && vite build）
pnpm verify         # typecheck + test + build（纯前端，提交前跑这个）

pnpm e2e            # Playwright 视觉 + 触屏基线（用本机 Chrome，不下载 Chromium）
pnpm e2e:update     # 重建视觉基线（基线变更须在提交说明里给理由）
pnpm interop        # 双端一致性测试台 A~E（需要 .NET SDK）
pnpm verify:all     # verify + e2e + interop

pnpm i18n           # 由 resw + web-strings 重新生成词表（改了文案必须跑）
pnpm icons          # 重新生成 PWA 图标（PNG 由脚本自绘，无第三方依赖）
pnpm perf           # 帧时间测量脚本（结果人工判读）
pnpm samples        # 生成互操作样本到 .interop/produced/
pnpm samples:check  # 自检：读回样本再写，确认 JSON 文本逐字节不变
```

## 目录

| 路径 | 说明 |
| --- | --- |
| `src/board/` | 域模型（纯数据 + 无 UI 依赖）：文档、页、工作区、会话（撤销栈）、命令、视口、快照 |
| `src/board/editing/` | 命中/框选/边界/形状属性换算/元素打开与复制（纯函数） |
| `src/ink/` | 几何与轮廓：笔宽半径、贝塞尔控制点、笔迹轮廓缓存、像素擦除 |
| `src/render/` | Canvas 2D 绘制：场景、元素卡、帧调度、静态层缓存、选择覆盖层、缩略图 |
| `src/interaction/` | 指针决策表与快捷键匹配 |
| `src/tools/` | 工具状态机：笔 / 形状 / 橡皮（像素 + 整笔）/ 选择 |
| `src/canvas/` | 命令式画布宿主：`board-controller`（状态机装配）、`dom-input`（DOM 事件桥） |
| `src/persistence/wbix/` | WBIX 编解码：`json-options`、`f32-format`、`manifest`、`page-payload`、`ink-codec`、`paths`、`read`、`write`、`errors` |
| `src/features/{export,import}/` | PNG / PDF / WBIX 导出与 WBIX 导入 |
| `src/platform/` | 浏览器能力：文件存取、Service Worker、`launchQueue` |
| `src/settings/` | 设置模型（含归一化）、服务、页面、设置文件导入导出 |
| `src/i18n/` | 词表运行时 + 生成物 `locales/*.generated.ts` + 网页独有词条 `web-strings/` |
| `src/ui/` | 外壳（标题栏 / Dock / 提醒栈）、弹出面板、对话框、覆盖层 |
| `src/styles/` | CSS 变量与各区域样式（颜色/圆角/尺寸常量的唯一来源） |
| `tests/unit/` | 单元测试（逐字符断言 JSON 文本与 f32 文本） |
| `tests/conformance/` | 跨端一致性：直接读桌面仓库里的 golden 语料（`WindBoard.Tests/Fixtures/wbix/`） |
| `e2e/` | Playwright 视觉基线（`__screenshots__/`）与触屏回归 |
| `scripts/` | 一致性测试台、词表生成、图标生成、样本生成、性能测量 |
| `.interop/` | 一致性测试台的中间产物（已 gitignore） |

## 与桌面端对齐的关键约定

这些是**格式契约**，改动前先看 `docs/dev/guides/web-port.zh-CN.md` 与 `docs/dev/guides/wbix.zh-CN.md`：

1. **换行是 CRLF**：桌面端 `JsonSerializer` 的缩进换行取 `Environment.NewLine`，Windows 上即
   `\r\n`；文件末尾**没有**尾随换行。读侧同时容忍 LF 与 CRLF。
2. **f32 文本规则**：所有数值都是 float32。最短往返表示 + 指数阈值（十进制指数 `>= 9` 或 `< -4`
   用科学计数法，大写 `E`、指数至少两位）；整数不带小数点；`-0` 写作 `-0`。见 `src/persistence/wbix/f32-format.ts`。
3. **字符串转义**：与 .NET 的 `JavaScriptEncoder.Default` 等价，非 ASCII 全部 `\uXXXX`（大写），
   且 `< > & ' + "` 也转义（`"` 写作 `\u0022`）。见 `src/persistence/wbix/json-options.ts`。
4. **null 成员照常输出**（未设 `DefaultIgnoreCondition`），键序等于 C# 类型的声明顺序。
5. **时间**：`DateTimeOffset` 写 `+00:00`（不是 `Z`），小数位 0~7 位可变（去掉末尾的 0）。
   JS `Date` 只有毫秒，逐字节往返要靠 `createdUtcText` 这类原始文本通道。
6. **容器**：`.wbix` 是 zip，条目顺序 `pages/*` → `assets/*` → `manifest.json`（manifest 最后写）。

## 双端一致性测试台（`pnpm interop`）

`scripts/interop.mjs` 把「两端对同一个 `.wbix` 包的读写语义一致」变成可执行断言。它需要本机的
.NET SDK（会先 `dotnet build`，再用 `dotnet test --filter FullyQualifiedName~WbixInteropDumpTests --no-build`
驱动桌面端的 dump 生成器）。纯前端环境跑不了这一步，所以 `pnpm verify` 只做前端三件事，
`pnpm verify:all` 才带上它。前置条件是 `.interop/produced/` 里已有样本（`pnpm samples`）。

五步（任一步失败即非零退出，并打印首个差异路径与两侧取值）：

| 步骤 | 做什么 | 产物 |
| --- | --- | --- |
| A | 网页端读 `WindBoard.Tests/Fixtures/wbix/files/*.wbix` + `.interop/produced/*.wbix` | `.interop/dump-ts/` |
| B | 桌面端读**同一批字节**，与 A 逐字段比对 | `.interop/dump-cs/` |
| C | 网页端由 `Fixtures/wbix/model/*.json` 写出 `.wbix`，并与 `Fixtures/wbix/expected/**` 逐字节比对 | `.interop/produced-ts/` |
| D | 桌面端读回 C 的产物，与「由 model 独立推出的期望 dump」比对 | `.interop/dump-cs-model/` |
| E | 桌面端重写 C 的产物 → 网页端读回，dump 与 D 一致 | `.interop/rewritten-cs/`、`.interop/dump-ts-rewritten/` |

**dump 口径**（两端必须同步维护：网页端在 `scripts/interop.mjs` 头部注释，桌面端在
`WindBoard.Tests/Board/Persistence/WbixInteropDumpTests.cs` 的 `<remarks>`）：

- 对象键按序数（ordinal）排序、数组保序；**键序本身参与比对**；
- 所有数值写成 **float32 最短往返文本字符串**，整数型字段（`version`/`currentIndex`/`order`）保持 JSON 数值；
- 不比对 `media` 元素的 `sourcePath`（桌面读侧会把它改写成临时文件路径，属平台相关运行时行为）；
- 不比对 `createdUtc` 的具体值（时钟不可注入），只锁存在性（占位符 `"<createdUtc>"`）。

**反向验证**：故意把 `formatF32()` 改成 `toFixed(2)`，或改动某处键序/数组顺序，`pnpm interop` 必须
失败并指出位置；验证后务必复原。

可选环境变量：`WINDBOARD_INTEROP_SKIP_BUILD=1` 跳过脚本内的 `dotnet build`（已手动构建过时省时间）。

## 依赖

React 19 + TypeScript + Vite（Rolldown）+ `@fluentui/react-components` v9 + `@fluentui/react-icons`
（UI）+ `fflate`（ZIP，MIT）。不使用 JSZip。

测试侧：`vitest`（node 环境，组件测试在文件头写 `/** @vitest-environment jsdom */`）、
`@testing-library/react` + `jsdom`（仅组件冒烟）、`@playwright/test`（用 `channel: 'chrome'` 走本机浏览器）。

## 与桌面仓库的关系

`web/` 不属于 `WindBoard.slnx`，独立用 pnpm 管理；但跨端一致性测试会**直接读取**
`WindBoard.Tests/Fixtures/wbix/` 的语料（单一事实源，不复制副本）。`web/**` 的行尾是 LF、
缩进 2 空格（由根 `.gitattributes` 与 `web/.editorconfig` 覆盖仓库默认值）。
