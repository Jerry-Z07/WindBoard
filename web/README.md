# WindBoard Web

WindBoard 的网页端（纯静态、本地优先的 PWA）。目标是与桌面版（WinUI 3）在 `.wbix` 文件上
**双向无损互通**，并最大化还原桌面端的操作方式与界面。

> 当前进度：**P0（工程骨架 + 格式层 + WBIX 读写）已完成**。画布渲染在 P1，界面外壳在 P3，
> PWA/离线在 P5。本阶段不包含任何画布与 UI 功能。

## 命令

```bash
pnpm install        # 安装依赖
pnpm dev            # 本地开发服务器
pnpm typecheck      # 类型检查（tsc -b，覆盖 src / tests / scripts）
pnpm test           # 单元测试 + 跨端一致性测试（vitest）
pnpm build          # 生产构建（tsc -b && vite build）
pnpm verify         # typecheck + test + build（纯前端，提交前跑这个）
pnpm samples        # 生成互操作样本到 .interop/produced/
pnpm samples:check  # 自检：读回样本再写，确认 JSON 文本逐字节不变
pnpm interop        # 双端一致性测试台 A~E（需要 .NET SDK，会构建并调用桌面端测试工程）
pnpm verify:all     # verify + interop（改了 WBIX 格式层时跑这个）
```

## 目录

| 路径 | 说明 |
| --- | --- |
| `src/board/model/` | 领域模型（纯数据，无 UI 依赖）：笔迹、图形、元素、工作区快照 |
| `src/persistence/wbix/` | WBIX 编解码：`json-options`（JSON 文本形态）、`f32-format`（float32 文本）、`manifest`、`page-payload`、`ink-codec`、`paths`、`read`、`write`、`errors` |
| `tests/unit/` | 单元测试（逐字符断言 JSON 文本与 f32 文本） |
| `tests/conformance/` | 跨端一致性：直接读桌面端仓库里的 golden 语料（`WindBoard.Tests/Fixtures/wbix/`） |
| `scripts/` | 互操作样本生成与自检脚本（Node 24 可直接跑 `.ts`），以及双端一致性测试台 `interop.mjs` |
| `.interop/produced/` | 互操作样本产出（生成物，已 gitignore） |
| `.interop/dump-*/`、`.interop/produced-ts/`、`.interop/rewritten-cs/` | 一致性测试台的中间产物（生成物，已 gitignore） |

## 与桌面端对齐的关键约定

这些是**格式契约**，改动前务必先看 `.trellis/tasks/10-31-web-port/` 下的调研文档：

1. **换行是 CRLF**：桌面端 `JsonSerializer` 的缩进换行取 `Environment.NewLine`，Windows 上即
   `\r\n`；文件末尾**没有**尾随换行。读侧同时容忍 LF 与 CRLF。
2. **f32 文本规则**：所有数值都是 float32。最短往返表示 + 指数阈值（十进制指数 `>= 9` 或 `< -4`
   用科学计数法，大写 `E`、指数至少两位）；整数不带小数点；`-0` 写作 `-0`。见 `f32-format.ts`。
3. **字符串转义**：等价于 .NET 的 `JavaScriptEncoder.Default` —— 非 ASCII 全部 `\uXXXX`（大写），
   且 `< > & ' + "` 也转义（`"` 写作 `\u0022`）。见 `json-options.ts`。
4. **null 成员照常输出**（未设 `DefaultIgnoreCondition`），键序等于 C# 类型的声明顺序。
5. **时间**：`DateTimeOffset` 写 `+00:00`（不是 `Z`），小数位 0~7 位可变（去掉末尾的 0）。
   JS `Date` 只有毫秒，逐字节往返要靠 `createdUtcText` 这类原始文本通道。
6. **容器**：`.wbix` 是 zip，条目顺序 `pages/*` → `assets/*` → `manifest.json`（manifest 最后写）。

## 双端一致性测试台（`pnpm interop`）

`scripts/interop.mjs` 把"两端对同一个 `.wbix` 包的读写语义一致"变成可执行断言。它需要本机的
.NET SDK（会先 `dotnet build WindBoard.slnx -p:Platform=x64`，再用
`dotnet test --filter FullyQualifiedName~WbixInteropDumpTests --no-build` 驱动桌面端的 dump 生成器）。
纯前端环境跑不了这一步，所以 `pnpm verify` 只做前端四件事，`pnpm verify:all` 才带上它。
前置条件是 `.interop/produced/` 里已有样本（`pnpm samples`），脚本不会自己生成样本。

五步（任一步失败即非零退出，并打印首个差异路径与两侧取值）：

| 步骤 | 做什么 | 产物 |
| --- | --- | --- |
| A | 网页端读 `WindBoard.Tests/Fixtures/wbix/files/*.wbix` + `.interop/produced/*.wbix` | `.interop/dump-ts/` |
| B | 桌面端读**同一批字节**，与 A 逐字段比对 | `.interop/dump-cs/` |
| C | 网页端由 `Fixtures/wbix/model/*.json` 写出 `.wbix`，并与 `Fixtures/wbix/expected/**` 逐字节比对 | `.interop/produced-ts/` |
| D | 桌面端读回 C 的产物，与「由 model 独立推出的期望 dump」比对（即 P0.4 验收：桌面能读回网页端写出的包） | `.interop/dump-cs-model/` |
| E | 桌面端重写 C 的产物 → 网页端读回，dump 与 D 一致 | `.interop/rewritten-cs/`、`.interop/dump-ts-rewritten/` |

**dump 口径**（两端必须同步维护：网页端在 `scripts/interop.mjs` 头部注释，桌面端在
`WindBoard.Tests/Board/Persistence/WbixInteropDumpTests.cs` 的 `<remarks>`）：

- 对象键按序数（ordinal）排序、数组保序；**键序本身参与比对**；
- 所有数值写成 **float32 最短往返文本字符串**（`formatF32` / .NET `JsonSerializer.Serialize(float)`），
  整数型字段（`version`/`currentIndex`/`order`）保持 JSON 数值；
- 不比对 `media` 元素的 `sourcePath`（桌面读侧会把它改写成临时文件路径，属平台相关运行时行为）；
- 不比对 `createdUtc` 的具体值（时钟不可注入），只锁存在性（占位符 `"<createdUtc>"`）。

**反向验证**：故意把 `f32Text()` 改成 `toFixed(2)`，或改动某处键序/数组顺序，`pnpm interop` 必须
失败并指出位置（例如 `pages[0].strokes[0].colorRgba.w`、`pages[0] 的键序`）；验证后务必复原。

可选环境变量：`WINDBOARD_INTEROP_SKIP_BUILD=1` 跳过脚本内的 `dotnet build`（已手动构建过时省时间）。

## 依赖

React 19 + TypeScript + Vite + `@fluentui/react-components`（UI 阶段用）+ `fflate`（ZIP，MIT）。
不使用 JSZip。

## 与桌面仓库的关系

`web/` 不属于 `WindBoard.slnx`，独立用 pnpm 管理；但跨端一致性测试会**直接读取**
`WindBoard.Tests/Fixtures/wbix/` 的语料（单一事实源，不复制副本）。
