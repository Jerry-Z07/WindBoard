# WBIX（WindBoard Interchange）格式说明（v3）

本文档用于说明 WindBoard 的私有交换格式 `.wbix`，便于后续开发、适配与扩展（例如导出/导入图片、视频等页面内容）。

## 1. 概述

WBIX 是一个以 **Zip 容器**承载的白板工作区文件：

- 扩展名：`.wbix`
- 容器：Zip
- 文本内容：JSON（UTF-8，`camelCase`，默认带缩进，允许尾随逗号，忽略注释，字段名大小写不敏感）
- 二进制内容：放在 `assets/` 目录（例如封面图 `assets/cover.png`）

> 阅读顺序建议：包结构见 §2，字段语义见 §3~§4，**版本兼容契约**（读侧如何按 `version` 分派、破坏性变更怎么做）见 §5，笔迹/图形的几何绘制口径见 `docs/dev/guides/ink-rendering.zh-CN.md`。
>
> 网页端实现：`web/src/persistence/wbix/**`（逐字符对齐本文的 JSON 文本形态），双端字节级一致性由 `cd web && pnpm interop` 守住；**改了本文任何一条形态约定，网页端必须同步**，对应关系见 `docs/dev/guides/web-port.zh-CN.md` §3.1。

设计目标：

- **可读、可扩展**：结构清晰，便于调试与迁移。
- **前向扩展位**：通过 `resources` 与 `elements` 预留未来页面元素（图片/视频/便签/图形等）。
- **页面身份稳定**：每页有稳定的 `id`，便于资源引用与跨版本适配。

## 2. 包内目录结构

WBIX（Zip）包内结构如下：

```
manifest.json
pages/
  page-000.json
  page-001.json
  ...
assets/
  cover.png              （可选：封面图，导出会尝试生成）
  elements/
    <elementId>.png      （可选：页面图片元素内嵌资源）
  ...                    （预留：后续可存放视频/音频等资源）
```

说明：

- `manifest.json`：清单与索引（版本、页列表、资源列表、当前页等）。
- `pages/page-XXX.json`：每页数据（笔迹 strokes + 页面元素 elements；v1/v2 的 strokes 为扁平形态，v3 起为带 Kind 的包装形态）。
- `assets/`：资源二进制文件目录（主要用于 `cover.png` 与页面图片内嵌资源）。

## 3. manifest.json

### 3.1 字段说明

`manifest.json` 对应代码中的 `WbixManifest`：

- `format`：固定为 `"wbix"`。
- `version`：格式版本号（当前导出为 `3`；读取兼容 `1~3`）。
- `createdUtc`：创建时间（UTC，ISO 8601，形如 `2026-02-05T12:34:56.789+00:00`）。注意小数秒位数是**可变的 0~7 位**：`System.Text.Json` 会去掉小数部分末尾的 `0`，所以 `12:34:56.7890000` 会写成 `12:34:56.789`，整秒时则完全没有小数部分；偏移量恒为 `+00:00`。解析方不得假设固定位数。
- `currentIndex`：当前页索引（0 基）。
- `pages`：页面列表（包含页 `id`、`index`、`path`）。
- `resources`：资源列表（预留扩展位，v1/v2 可为空；v2 导出会尝试添加封面图资源条目）。
- `viewportCameraWorld`：可选。导出时记录视口相机世界坐标（仅记录，不强制导入后应用）。
- `viewportZoom`：可选。导出时记录视口缩放（仅记录）。
- `viewportSizeDip`：可选。导出时记录视口尺寸（DIP，便于后续恢复视图或预览计算）。

`pages` 的每个条目对应 `WbixManifestPage`：

- `id`：页面 ID（与 `pages/page-XXX.json` 里的 `id` 对应）。
- `index`：页面顺序索引（0 基）。
- `path`：Zip 内路径（例如 `pages/page-000.json`）。

`resources` 的每个条目对应 `WbixResourceEntry`：

- `id`：资源标识（建议稳定；可用 GUID、哈希等）。
- `type`：资源类型（建议：`image` / `video` / `audio` / `file` ...）。
- `path`：Zip 内路径（建议放在 `assets/` 下）。
- `contentType`：MIME（例如 `image/png`）。
- `meta`：可选元数据（键值对字符串字典，例如尺寸、时长、校验和、用途等）。

### 3.2 封面图资源（assets/cover.png）

当前导出会尝试生成首页封面图：

- 二进制路径：`assets/cover.png`
- manifest 资源条目：
  - `id`：`"cover"`
  - `type`：`"image"`
  - `path`：`"assets/cover.png"`
  - `contentType`：`"image/png"`
  - `meta`：包含 `role=cover`、`pageIndex=0`、`pixelWidth`、`pixelHeight`（便于后续 UI 列表展示）

注意：封面图属于 **可选资源**；导入端应允许缺失并做降级处理（例如显示默认缩略图）。

### 3.3 示例（manifest.json）

以下示例仅用于说明字段结构（ID/时间会因文件而异）：

```json
{
  "format": "wbix",
  "version": 3,
  "createdUtc": "2026-02-05T12:34:56.789+00:00",
  "currentIndex": 0,
  "pages": [
    { "id": "2f6b35f7-9a6f-4c76-9a5d-2e9d0c5c3b7f", "index": 0, "path": "pages/page-000.json" }
  ],
  "resources": [
    {
      "id": "cover",
      "type": "image",
      "path": "assets/cover.png",
      "contentType": "image/png",
      "meta": { "role": "cover", "pageIndex": "0", "pixelWidth": "512", "pixelHeight": "512" }
    }
  ]
}
```

## 4. pages/page-XXX.json

页面文件对应 `WbixPagePayload`：

- `id`：页面 ID（与 manifest 中 pages 条目一致）。
- `strokes`：笔迹层条目列表（主体数据；v3 起每个条目带 Kind 标识，读取兼容 v1/v2 的扁平形态）。
- `elements`：页面元素列表（文本/链接/媒体/文件等；导入端应忽略未知 `type` 保持前向兼容）。

### 4.1 strokes（笔迹层条目）结构（v3）

`strokes` 的每个条目对应 `InkItemSnapshot`（Kind 判别，与 elements 的 `{type, data}` 模式一致）：

- `kind`：条目类型标识。取值：
  - `stroke`：折线笔迹（缺省 / 为 null 时按 `stroke` 处理）。
  - `line` / `rect` / `ellipse` / `arrow`：两点图形。
  - 未知 `kind`：读取端跳过该条目并记 Warn 日志，不影响整页加载。
- `stroke`：折线笔迹数据（`StrokeSnapshot`，`kind=stroke` 时的载荷）。
- `shape`：两点图形数据（`ShapeSnapshot`，`kind` 为图形类型时的载荷）。

> 图形 kind 是在 `version` 保持 `3` 的前提下**追加**的格式扩展（追加式扩展，见 §5），因此 v3 文件中既可能出现只有 `stroke` 条目的文件，也可能出现 `stroke` + 图形混排的文件。

`stroke`（`StrokeSnapshot`）字段：

- `points`：点列表（`StrokePointSnapshot`）。
- `colorRgba`：颜色（`Vector4`：`x/y/z/w` 分别表示 `R/G/B/A`，范围一般为 0~1）。
- `baseSize`：笔迹基础尺寸（世界坐标下的直径，单位与页面坐标一致）。
- `enablePressure`：是否启用压感（若为 true，会根据 `pressure` 调整笔宽）。

> v1/v2 兼容说明：旧文件的 `strokes` 条目为"扁平"形态（`points`/`colorRgba`/`baseSize`/`enablePressure` 直接位于条目对象上，无 `kind`/`stroke` 包装）。读取端会将扁平条目视为 `kind=stroke` 处理；v3 写侧固定输出包装形态。

`points` 的每个条目对应 `StrokePointSnapshot`：

- `position`：点位置（`Vector2`：`x/y`，世界坐标，单位与画布逻辑坐标一致）。
- `pressure`：压感（通常 0~1，未启用压感时可固定为 1）。

> 坐标说明：WBIX 记录的是“世界坐标（DIP 近似）”，不是屏幕像素坐标。导入后由视口与渲染逻辑决定实际显示。

`shape`（`ShapeSnapshot`）字段：

- `start`：起点（`Vector2`：`x/y`，世界坐标）。
- `end`：终点（`Vector2`：`x/y`，世界坐标）。矩形/椭圆取这两点为对角。
- `colorRgba`：描边颜色（`Vector4`，含义同上）。
- `width`：描边宽度（世界坐标下的 DIP，渲染时下限 0.5）。

> 几何口径（笔宽、半径、箭头翼等）见 `docs/dev/guides/ink-rendering.zh-CN.md`，格式文档不复述几何公式。

### 4.2 elements（页面元素）

`elements` 的每个条目对应 `WbixPageElement`：

- `type`：元素类型（当前实现：`text` / `link` / `media` / `file`）。
- `data`：元素数据（半结构化 JSON），包含通用字段 + 类型字段。

#### 4.2.1 通用字段（data）

- `id`：元素 ID（Guid 字符串）。
- `layer`：层级（`belowInk` / `aboveInk`）。
- `positionWorld`：左上角世界坐标（`Vector2`：`x/y`）。
- `sizeWorld`：尺寸（`Vector2`：`x/y`）。
- `order`：同层内顺序（数值越小越靠后；导入端会按 `order` 排序并保持稳定）。

#### 4.2.2 text

- `type=text`
- `data.text`：文本内容。

#### 4.2.3 link

- `type=link`
- `data.url`：链接 URL。
- `data.title`：可选标题（可为空）。

#### 4.2.4 media

- `type=media`
- `data.kind`：媒体类型（`image` / `video` / `audio`）。
- `data.displayName`：展示名称（通常为文件名）。
- `data.sourcePath`：可选源路径（外链资源使用；内嵌图片通常为 null，避免泄漏本地绝对路径）。
- `data.resourceId`：可选资源引用（指向 `manifest.resources[].id`；用于内嵌图片等）。

> 说明：当 `resourceId` 存在时，导入端应优先使用 `manifest.resources` 找到 `path` 并从 Zip 内提取资源。

#### 4.2.5 file

- `type=file`
- `data.sourcePath`：源路径（外链/本地文件使用）。
- `data.displayName`：展示名称（通常为文件名）。

### 4.3 示例（page-000.json）

```json
{
  "id": "2f6b35f7-9a6f-4c76-9a5d-2e9d0c5c3b7f",
  "strokes": [
    {
      "kind": "stroke",
      "stroke": {
        "points": [
          { "position": { "x": 10.5, "y": 20.25 }, "pressure": 0.5 },
          { "position": { "x": 12.0, "y": 24.0 }, "pressure": 0.8 }
        ],
        "colorRgba": { "x": 0.1, "y": 0.2, "z": 0.3, "w": 1.0 },
        "baseSize": 3.25,
        "enablePressure": true
      }
    }
  ],
  "elements": [
    {
      "type": "text",
      "data": {
        "id": "a0e59f75-7d1a-4f9e-9c60-6f3e15b0c7c1",
        "layer": "belowInk",
        "positionWorld": { "x": 10.0, "y": 20.0 },
        "sizeWorld": { "x": 300.0, "y": 120.0 },
        "order": 0,
        "text": "Hello"
      }
    },
    {
      "type": "media",
      "data": {
        "id": "c2c1a1ce-50d8-4f67-9d4a-2e3a3c5f0b6c",
        "layer": "aboveInk",
        "positionWorld": { "x": 100.0, "y": 200.0 },
        "sizeWorld": { "x": 320.0, "y": 180.0 },
        "order": 0,
        "kind": "image",
        "displayName": "image.png",
        "sourcePath": null,
        "resourceId": "img-c2c1a1ce-50d8-4f67-9d4a-2e3a3c5f0b6c"
      }
    }
  ]
}
```

## 5. 版本契约（读侧分派）

`version` 不是装饰字段，而是**双端互操作契约**：它决定读侧用哪一套解析规则，也决定老应用能否打开新文件。

### 5.1 写侧

- 永远只写当前版本 `WbixWorkspaceSerializer.CurrentVersion`（当前为 `3`）。
- 不做「按目标版本降级写出」：没有 v2/v1 导出选项，也不维护降级写出代码。

### 5.2 读侧分派

读取端在解析 manifest 之后，按 `manifest.version` 选择 reader（`WbixPageReaderRegistry.For(version)`）：

| `version` | reader | `strokes` 条目形态 |
|---|---|---|
| `1`、`2` | `WbixPageReaderV1V2` | 扁平形态（`points`/`colorRgba`/`baseSize`/`enablePressure` 直接在条目上） |
| `3` | `WbixPageReaderV3` | 包装形态（`{ "kind": ..., "stroke" \| "shape": ... }`），并容忍混入的扁平条目 |
| 其它（`<= 0` 或 `> 3`） | 无 | 抛 `InvalidDataException`，文案 key `Wbix_VersionNotSupported_Fmt`（参数为文件中的 `version`） |

约定：

- **未知版本拒绝的时机**：在 manifest 解析与 `format` 校验之后、**任何页面被读取之前**。因此「`version` 非法但 `pages` 为空」的文件同样会被拒绝，不会静默返回空工作区。
- **v3 的扁平兜底**：v3 文件里出现扁平条目时，按 `kind=stroke` 解析，并为**每个文件**记录一次 Warn 日志（不是每个条目一次，避免超大文件刷日志）。这是为了兼容「v3 早期实现」与手工构造的文件。
- **v1/v2 的包装容忍**：v1/v2 reader 遇到包装形态条目时也能解析（不告警），以保证「今日能读的文件，改版后仍能读」。

### 5.3 追加式扩展 vs 破坏性变更

| 变更类型 | 例子 | 是否提升 `version` |
|---|---|---|
| 追加式（前向兼容） | 新增可选字段、新增 `kind` 取值、新增 `elements[].type` | **否**，读侧必须容忍未知字段/未知取值 |
| 破坏性 | 改变既有字段语义、改变结构形态、改变类型（如数字→字符串） | **是**，并按下节流程执行 |

「shape kinds（`line`/`rect`/`ellipse`/`arrow`）」就是追加式扩展的先例：`version` 保持 `3`，读侧遇到未知 `kind` 只跳过该条目。

### 5.4 破坏性变更的执行流程

1. 新增 reader（如 `WbixPageReaderV4`），实现新形态；**既有 reader 一行不改**。
2. 在 `WbixPageReaderRegistry.For` 中登记新版本分派。
3. 提升 `WbixWorkspaceSerializer.CurrentVersion`。
4. 在 `WindBoard.Tests/Fixtures/wbix/` 补充新版本语料（输入文件 + `expected/` 期望文本）。
5. 保留既有 golden 语料与其断言；若写侧字节确实变化，必须在提交说明中给出理由并显式更新期望文本。

### 5.5 字节形态与 golden 语料

`WindBoard.Tests/Fixtures/wbix/` 是写出形态的**单一事实源**：条目顺序、缩进、键序、`null` 成员是否输出、数字格式、非 ASCII 转义方式都被逐字节锁死。任何写侧改动都会让该组测试失败——这是设计意图，不是脆弱测试。

字节层面的硬性约定（任何第二实现都必须与之一致）：

- **行分隔符固定为 CRLF**，由写侧显式指定（`JsonSerializerOptions.NewLine = "\r\n"`），**不使用平台默认值**——换行属于格式契约，不能依赖「在 Windows 上碰巧是 CRLF」；文件末尾没有尾随换行。
- 缩进为 2 个空格；对象键序由写侧声明的字段顺序决定（与本文档各节的字段顺序一致）。
- 数字按 `float` 的**最短往返**写法输出（`12.0` 写成 `12`、`0.33333334`、`999999.94`）。
- 非 ASCII 字符与 JSON 特殊字符转义为大写 `\uXXXX`（字符串内的 `"` 写作 `\u0022`，`&` 写作 `\u0026`，emoji 用代理对如 `\uD83D\uDE00`）。

## 6. 约束

当前读取逻辑约束（v3）：

- `format` 必须为 `"wbix"`（不区分大小写）。
- `version` 必须满足 `0 < version <= 3`；`version <= 0` 或 `version > 3` 均视为不支持。
- 页面按 `manifest.pages[].index` 排序后加载，保证顺序稳定。
- `strokes` 条目同时接受 v3 包装形态与 v1/v2 扁平形态（缺省按 `stroke` 处理）；未知 `kind` 的条目跳过并记录 Warn 日志。
- 页面路径必须位于 `pages/` 前缀下且不含 `..`；资源路径同样做安全校验，不安全的资源被忽略（记 Warn）而不是让整次导入失败。

## 7. 安全与健壮性建议（导入端）

WBIX 属于外部输入，导入端建议：

- 限制解压后单文件大小、总大小与条目数量，防止压缩炸弹。
- 校验 `resources[].path` 与 `pages[].path` 不允许路径穿越（例如 `../`）。
- 对 JSON 数组长度、点数等做上限限制，避免 OOM 或极端耗时。
- 对 `contentType` 与实际内容做基本一致性校验（可选）。

## 8. 开发定位（代码入口）

- 序列化实现：`WindBoard/Board/Persistence/Wbix/WbixWorkspaceSerializer.cs`
- 读侧版本分派（`IWbixPageReader` / `WbixPageReaderRegistry`）：`WindBoard/Board/Persistence/Wbix/WbixWorkspaceSerializer.Readers.cs`
- 清单模型：`WindBoard/Board/Persistence/Wbix/WbixManifest.cs`
- 页面模型：`WindBoard/Board/Persistence/Wbix/WbixPagePayload.cs`
- 资源写入模型：`WindBoard/Board/Persistence/Wbix/WbixResourceFile.cs`
- 绘制条目快照与 JSON 兼容（v1/v2/v3 形态）：`WindBoard/Board/Persistence/BoardWorkspaceSnapshot.cs`、`WindBoard/Board/Persistence/InkItemSnapshotJsonConverter.cs`
- 快照 ↔ 域转换单点（Kind 分发 + Bounds 重算）：`WindBoard/Board/Persistence/BoardInkItemCodec.cs`
- 字节级 golden 语料：`WindBoard.Tests/Fixtures/wbix/`（说明见其 `README.md`）
- 笔迹/图形几何口径：`docs/dev/guides/ink-rendering.zh-CN.md`
