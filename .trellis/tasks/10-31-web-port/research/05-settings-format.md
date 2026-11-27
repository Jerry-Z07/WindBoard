# 05 · settings.json 格式调研（P0.6）

> 目的：为网页端（PWA）实现"设置文件与桌面版兼容"（design D10）提供**可直接照抄的事实**。
>
> 事实来源（全部为读源码 + 读桌面端真实产物，无推断成分；推断处已显式标注）：
> - `WindBoard/Settings/AppSettings.cs`（数据结构）
> - `WindBoard/Settings/AppSettingsStore.cs`（序列化选项、读容错、归一化）
> - `WindBoard/Settings/ColorHex.cs`、`PenSettingsDefaults.cs`、`AppLanguagePreference.cs`、
>   `StartupWindowMode.cs`、`UpdateCheckInterval.cs`、`ElementCardTheme.cs`、
>   `DownloadSourceId.cs`、`DownloadSourcePolicy.cs`
> - `WindBoard/Features/Dock/Models/DockSettingsModels.cs`、`DockItemIds.cs`
> - `WindBoard/Features/Shortcuts/Models/KeyboardShortcutsSettings.cs`、`KeyboardShortcutsDefaults.cs`
> - `WindBoard/Logging/AppLogLevel.cs`、`AppLogLevelParser.cs`
> - `WindBoard/Persistence/AppDataPaths.cs`（文件位置）
> - **真实样例**：`WindBoard/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/data/settings.json`
>   （应用运行期写出的文件，1855 字节）

## 1. 序列化选项（与 WBIX 的对照）

`AppSettingsStore.JsonOptions`（`AppSettingsStore.cs:27-33`）：

```csharp
new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
}
```

| 维度 | settings.json | WBIX（`WbixWorkspaceSerializer.cs:32-41`） |
| --- | --- | --- |
| camelCase | 是 | 是 |
| 缩进 | 2 空格（`WriteIndented`） | 2 空格 |
| 换行 | **CRLF**（`JsonWriterOptions.NewLine` 默认 `Environment.NewLine`，Windows 即 `\r\n`） | **CRLF**（同源） |
| 尾随换行 | **无** | 无 |
| null 成员 | **照常输出**（未设 `DefaultIgnoreCondition`） | 照常输出 |
| Encoder | **未设** → `JavaScriptEncoder.Default`（非 ASCII + `< > & ' + "` 全部转义） | 同 |
| 读：注释 | `Skip`（允许 `//`、`/* */`） | 同 |
| 读：尾随逗号 | 允许 | 同 |
| 读：属性名大小写 | **默认（大小写不敏感）** | 显式 `PropertyNameCaseInsensitive = true` |
| `IncludeFields` | 未设（`AppSettings` 全是属性，无影响） | 显式 `true` |

**实测佐证**：真实 `settings.json` 里 `"undo": "Ctrl\u002BZ"` —— `+` 被转义成 `\u002B`，
证明设置文件同样走 `JavaScriptEncoder.Default`；`"thicknessPresets": [2, 3, 5]` ——
float 整数不带小数点（与 WBIX 的 f32 文本规则一致）。

## 2. 文件位置

由 `AppDataPaths.SettingsFilePath` 决定（`WindBoard/Persistence/AppDataPaths.cs:23,133,146`）：

| 安装形态 | 位置 |
| --- | --- |
| MSIX（Store）/ 安装版 | `%LocalAppData%\WindBoard\settings.json`（MSIX 的包私有重定向由系统处理，表达式不变） |
| 便携版 | `{AppContext.BaseDirectory}\data\settings.json` |
| 便携版但 data 不可写 | 回退 `%LocalAppData%\WindBoard\settings.json` |

网页端不共享该路径；此表只用于说明"桌面端读的是哪个文件"，便于跨端导入导出时对齐。

## 3. 完整字段表

类型记号：`f32` = C# `float`（JSON 文本规则同 WBIX，整数不带小数点）；`dto?` = 可空 `DateTimeOffset`。

### 3.1 顶层

| 键 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `general` | object | 见下 | 通用 |
| `appearance` | object | 见下 | 外观 |
| `dock` | object | 见下 | Dock 布局 |
| `writing` | object | 见下 | 书写 |
| `keyboardShortcuts` | object | 见下 | 快捷键 |
| `diagnostics` | object | 见下 | 诊断 |
| `announcements` | object | 见下 | 公告 |

### 3.2 `general`

| 键 | 类型 | 默认 | 归一化 / 容错 |
| --- | --- | --- | --- |
| `languagePreference` | string | `"system"` | `AppLanguagePreferenceParser.NormalizeOrDefault`：`system`/`auto`（大小写不敏感）→ `system`；`_`→`-`；`zh`→`zh-CN`；`en`→`en-US`；其它按 `CultureInfo.Name` 校验，中性文化（如 `ja`）若只有一个同语言可用 culture 则映射（`ja`→`ja-JP`）；非法/空 → `system` |
| `startupWindowMode` | string | `"windowed"` | `windowed`/`fullscreen`（大小写不敏感）；非法 → `windowed` |
| `enterScreenAnnotationWhenMinimized` | `bool?` | `true` | **可空**：`null` → `true`（历史版本无此字段时的兼容写法） |
| `camouflage` | object | 见 3.2.1 | 伪装 |
| `updates` | object | 见 3.2.2 | 更新检查 |

#### 3.2.1 `general.camouflage`

| 键 | 类型 | 默认 | 归一化 |
| --- | --- | --- | --- |
| `enabled` | bool | `false` | — |
| `title` | string | `""` | Trim，null → `""` |
| `sourcePath` | string | `""` | Trim，null → `""` |
| `iconCachePath` | string | `""` | Trim，null → `""` |
| `shortcutLastGeneratedSignature` | string | `""` | Trim，null → `""` |
| `shortcutLastGeneratedPath` | string | `""` | Trim，null → `""` |

#### 3.2.2 `general.updates`

| 键 | 类型 | 默认 | 归一化 |
| --- | --- | --- | --- |
| `autoCheckInterval` | string | `"weekly"` | `weekly`/`biweekly`/`monthly`/`never`（大小写不敏感）；非法 → `weekly` |
| `lastCheckUtc` | dto? | `null` | 若 > 当前时间则钳到当前时间 |
| `lastNotifiedVersion` | string | `""` | — |
| `downloadSourcePolicy` | string | `"auto"` | `auto`/`fixed`；非法 → `auto` |
| `downloadSourceId` | string | `"github"` | `github`/`gh-proxy`/`07`；非法 → `github` |
| `downloadSourceLastTestUtc` | dto? | `null` | 若 > 当前时间则钳到当前时间 |

### 3.3 `appearance`

| 键 | 类型 | 默认 | 归一化 |
| --- | --- | --- | --- |
| `canvasBackgroundHex` | string | `"#2E2F33"` | `ColorHex.NormalizeToHexRgbOrDefault`：接受 `#RRGGBB`/`RRGGBB`/`#AARRGGBB`/`AARRGGBB`（长度 6 或 8，否则非法），输出统一 `#RRGGBB`（**丢弃 alpha**）；非法 → 默认色，默认色也非法 → `#000000` |
| `elementCardTheme` | string | `"dark"` | `dark`/`light`；非法 → `dark` |

### 3.4 `dock`

| 键 | 类型 | 默认 | 归一化 |
| --- | --- | --- | --- |
| `leftOrder` | string[] | `["more","minimize","import"]` | `NormalizeOrder`：过滤未知项 → 去重（保留首次）→ 按默认顺序补齐缺失项 |
| `toolsOrder` | string[] | `["toolSelect","toolPen","toolEraser","toolShape"]` | 同上 |
| `undoRedoOrder` | string[] | `["undo","redo"]` | 同上 |
| `pagesOrder` | string[] | `["pagePrev","pageIndicator","pageNext","pageAdd"]` | 同上 |
| `isUndoRedoVisible` | bool | `true` | — |
| `isShortcutDocksVisible` | bool | `false` | — |
| `shortcutItems` | object[] | `[]` | 见 3.4.1 |

合法的 Dock 项 Id（`DockItemIds.cs`）：
`more`/`minimize`/`import`、`toolSelect`/`toolPen`/`toolEraser`/`toolShape`、`undo`/`redo`、
`pagePrev`/`pageIndicator`/`pageNext`/`pageAdd`。

#### 3.4.1 `dock.shortcutItems[]`

最多 **5** 个（超出截断）；数组里的 `null` 元素直接跳过。

| 键 | 类型 | 默认 | 归一化 |
| --- | --- | --- | --- |
| `id` | string | `""` | 空白 → 新生成 `Guid.NewGuid().ToString("N")`（**32 位小写十六进制、无连字符**）；非空白 → Trim |
| `side` | string | `"left"` | `left`/`right`；非法 → `left` |
| `type` | string | `"file"` | `file`/`link`/`program`；非法 → `file` |
| `displayName` | string? | `null` | 空白 → `null`；否则 Trim |
| `path` | string | `""` | Trim（**允许为空**：设置页先"添加"再补内容） |
| `arguments` | string? | `null` | 空白 → `null`；**非空白时不做 Trim**（保留原始空白） |
| `iconSource` | string | `"default"` | `default`/`icon`/`font`；非法 → `default` |
| `iconPath` | string? | `null` | 空白 → `null`；否则 Trim |
| `iconSymbol` | string? | `null` | 空白 → `null`；否则 Trim |

### 3.5 `writing`

| 键 | 类型 | 默认 | 归一化 |
| --- | --- | --- | --- |
| `pen.paletteHexes` | (string\|null)[] | 9 色（见下） | `PenSettingsDefaults.NormalizePalette`：`null` → 默认 9 色；数量 `Clamp(count, 3, 24)`；每项空白或非法颜色 → `null`（空色块）；合法 → `#RRGGBB`（丢 alpha）；不足部分补 `null` |
| `pen.thicknessPresets` | f32[] | `[2, 3, 5]` | 必须**恰好 3 个**；任一非有限、`< 0.5` 或 `> 64` → 全部回退默认；否则**升序排序**后落盘 |
| `pen.useThicknessSlider` | bool | `false` | — |

默认色板（`PenSettingsDefaults.DefaultPaletteHexes`，顺序即落盘顺序）：
`#FFFFFF`、`#FF3B30`、`#FFCC00`、`#34C759`、`#32ADE6`、`#0A84FF`、`#AF52DE`、`#FF2D55`、`#000000`。

### 3.6 `keyboardShortcuts`

| 键 | 类型 | 默认 | 归一化 |
| --- | --- | --- | --- |
| `conflictReminderEnabled` | bool | `true` | — |
| `undo` | string | `"Ctrl+Z"` | 空白 → `""`（表示**禁用**，不会自动回填）；非法组合键 → 回退默认值并写规范格式；与 `redo` 冲突时按 Undo→Redo 顺序，后者清空并记 `ConflictDisabled` 问题 |
| `redo` | string | `"Ctrl+Y"` | 同上 |

### 3.7 `diagnostics.logging`

| 键 | 类型 | 默认 | 归一化 |
| --- | --- | --- | --- |
| `fileEnabled` | bool | `true` | — |
| `minimumLevel` | string | `"Information"` | `AppLogLevelParser` 接受别名：`trace`/`trc`、`debug`/`dbg`、`info`/`information`/`inf`、`warn`/`warning`/`wrn`、`error`/`err`、`critical`/`fatal`/`crt`（大小写不敏感）；**落盘统一为枚举名** `Trace`/`Debug`/`Information`/`Warning`/`Error`/`Critical` |
| `retentionDays` | int | `14` | `> 365` → 钳到 `365`；`<= 0` 表示不清理 |

### 3.8 `announcements`

| 键 | 类型 | 默认 | 归一化 |
| --- | --- | --- | --- |
| `dismissedIds` | string[] | `[]` | 去空白、Trim、去重（Ordinal）、最多 **32** 条；**不做**已知 Id 白名单过滤（旧版本写入的 Id 必须保留） |

## 4. 读容错与写策略

读（`AppSettingsStore.LoadOrDefault`）：

- 文件不存在 → 默认值 + 归一化（不报错、不落盘）；
- 读取失败 / JSON 解析失败 / 类型不匹配 → **整体回退默认值**（`catch` 里 `AppLog.Warn` 后返回 `new AppSettings()` 归一化结果）；
- 允许 `//`、`/* */` 注释与尾随逗号；属性名大小写不敏感。

写（`AppSettingsStore.Save`）：

1. `AppSettingsCloner.Clone` 深拷贝（避免归一化污染内存态）；
2. 归一化；
3. 序列化为字符串；
4. 写 `FilePath + ".tmp"`，再 `File.Move(tmp, FilePath, overwrite: true)` —— 用临时文件替换降低写中断损坏概率。

> 注意：**没有**"只写非默认值"的裁剪逻辑，全字段一律写出（含 `null`）。

## 5. 真实样例（应用运行期产物，1855 字节）

行尾 **CRLF**、2 空格缩进、末尾**无**换行。下面按 LF 展示（`\u002B` 为真实转义，不是笔误）：

```json
{
  "general": {
    "languagePreference": "system",
    "startupWindowMode": "windowed",
    "enterScreenAnnotationWhenMinimized": true,
    "camouflage": {
      "enabled": false,
      "title": "",
      "sourcePath": "",
      "iconCachePath": "",
      "shortcutLastGeneratedSignature": "",
      "shortcutLastGeneratedPath": ""
    },
    "updates": {
      "autoCheckInterval": "weekly",
      "lastCheckUtc": null,
      "lastNotifiedVersion": "",
      "downloadSourcePolicy": "auto",
      "downloadSourceId": "github",
      "downloadSourceLastTestUtc": null
    }
  },
  "appearance": {
    "canvasBackgroundHex": "#2E2F33",
    "elementCardTheme": "dark"
  },
  "dock": {
    "leftOrder": [
      "more",
      "minimize",
      "import"
    ],
    "toolsOrder": [
      "toolSelect",
      "toolPen",
      "toolEraser",
      "toolShape"
    ],
    "undoRedoOrder": [
      "undo",
      "redo"
    ],
    "pagesOrder": [
      "pagePrev",
      "pageIndicator",
      "pageNext",
      "pageAdd"
    ],
    "isUndoRedoVisible": true,
    "isShortcutDocksVisible": false,
    "shortcutItems": []
  },
  "writing": {
    "pen": {
      "paletteHexes": [
        "#FFFFFF",
        "#FF3B30",
        "#FFCC00",
        "#34C759",
        "#32ADE6",
        "#0A84FF",
        "#AF52DE",
        "#FF2D55",
        "#000000"
      ],
      "thicknessPresets": [
        2,
        3,
        5
      ],
      "useThicknessSlider": false
    }
  },
  "keyboardShortcuts": {
    "conflictReminderEnabled": true,
    "undo": "Ctrl\u002BZ",
    "redo": "Ctrl\u002BY"
  },
  "diagnostics": {
    "logging": {
      "fileEnabled": true,
      "minimumLevel": "Information",
      "retentionDays": 14
    }
  },
  "announcements": {
    "dismissedIds": []
  }
}
```

## 6. 对网页端（D10）的实现建议

1. **字段名/结构照抄**：网页端设置对象用同一套 camelCase 键与层级，导出即为桌面可读的
   `settings.json`，导入即接受桌面产物。
2. **文本形态照抄**：CRLF + 2 空格 + 无尾随换行 + null 照写 + `JavaScriptEncoder.Default` 转义
   （注意 `+` → `\u002B`，快捷键 `Ctrl+Z` 落盘就是 `"Ctrl\u002BZ"`）。
   web 侧可直接复用 `web/src/persistence/wbix/json-options.ts` 的 `JsonTextWriter` 与
   `escapeJsonString`（同一套规则），只需把"顶层对象"换成 settings 结构。
3. **数值文本**：`thicknessPresets` 是 f32，必须复用 `formatF32`（`2` 而不是 `2.0`）。
4. **时间字段**：`lastCheckUtc` / `downloadSourceLastTestUtc` 落盘形态是 `DateTimeOffset`，
   小数位 **0~7 位可变**（`System.Text.Json` 的 `TrimDateTimeOffset` 会去掉末尾的 0，秒为整时
   连小数点一起去掉），偏移恒为 `+00:00`（不是 `Z`）。JS `Date` 只有毫秒精度，
   若要逐字节往返，需像 WBIX 的 `createdUtcText` 那样保留原始文本（见
   `web/src/persistence/wbix/manifest.ts` 的 `createdUtcText` 设计）；只有 `Date` 时，
   可用 `formatUtcDateTimeOffset()` 拼出 `...THH:mm:ss[.fff]+00:00`（毫秒末尾的 0 会被裁掉）。
5. **归一化照抄**：非法值回退规则（枚举、颜色、色板、粗细、日志级别、公告上限）必须一致，
   否则同一份文件在两端显示不同状态。
6. **未知字段必须保留**（风险点）：桌面后续版本会新增字段，网页端若"只写自己认识的键"，
   一次保存就会把桌面新增设置抹掉。建议网页端解析时保留原始 JSON 的未知成员，
   写回时原样带上（`web` 侧目前尚无 settings 实现，落地时务必按此约束设计）。
7. **浏览器持久化**：本地存储用 IndexedDB/localStorage 即可，无需与桌面共享路径；
   `settings.json` 仅作为**导入/导出**格式。

## 7. 未验证项（需 Lead 用 .NET 实测确认，网页端不擅自假设）

| 项 | 现状 | 建议验证方式 |
| --- | --- | --- |
| 桌面能否读取网页端写出的 **LF** 版 settings.json | 推断可以（`JsonSerializer` 对空白不敏感），未实测 | 用网页端产物替换 `data/settings.json` 后启动桌面，确认设置生效 |
| `shortcutItems[].arguments` 非空白时**不 Trim** | 读源码结论（`IsNullOrWhiteSpace` 判定后才赋值，未 Trim） | 写入 `"  --flag  "` 后读回，确认空白保留 |
| `AppLanguagePreference` 中性文化映射表（`ja`→`ja-JP` 等） | 只读到 `TryResolveNeutralCultureToSupported` 的调用点，未读完该函数 | 需要时再读 `AppLanguagePreference.cs` 剩余部分 |
| 归一化后写出的字段**顺序**是否严格等于声明顺序 | 真实样例与声明顺序一致，但样例是默认值文件 | 写一份全字段非默认的 settings.json，逐字符比对 |
