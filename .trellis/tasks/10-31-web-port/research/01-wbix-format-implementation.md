# 调研 01：WBIX 格式实现细节

> 用途：网页端读写 `.wbix` 的权威依据。全部结论来自当前源码（分支 develop，提交 bec9761），非文档推断。
> 面向用户的格式说明见 `docs/dev/guides/wbix.zh-CN.md`；本文记录**实现级**细节。

## 1. 代码位置

- 序列化：`WindBoard/Board/Persistence/`（含 `Wbix/` 子目录）
- 导出：`WindBoard/Features/Export/`（`BoardExportService.cs`、`BoardRasterExporter.cs`、`BoardPdfExporter.cs`）
- 导入：`WindBoard/Features/Import/`（`Wbix/`、`Wbi/`）
- 注意：不存在 `WindBoard/Services/Persistence/`，早期笔记中的该路径有误。

## 2. JSON 序列化选项

`WindBoard/Board/Persistence/Wbix/WbixWorkspaceSerializer.cs:32-41` 是唯一来源：

| 选项 | 值 | 影响 |
|---|---|---|
| `PropertyNamingPolicy` | `CamelCase` | 键名全小驼峰 |
| `WriteIndented` | `true` | 2 空格缩进 |
| `AllowTrailingCommas` | `true` | 读侧宽容 |
| `ReadCommentHandling` | `Skip` | 读侧允许注释 |
| `PropertyNameCaseInsensitive` | `true` | 读侧大小写不敏感 |
| `IncludeFields` | `true` | 字段也参与序列化 |
| `DefaultIgnoreCondition` | **未设置** | **`null` 成员必须输出** |
| `Encoder` | **未设置** | 默认 `JavaScriptEncoder.Default` → 非 ASCII 转义为 `\uXXXX` |

## 3. 包结构

```
manifest.json
pages/page-000.json
pages/page-001.json
assets/cover.png
assets/elements/{elementId:N}{ext}
```

- 写入顺序：全部 `pages/*` → `resources` → `manifest.json` **最后**（`Save.cs:33-35`）——manifest 最后写保证读侧看到完整包。
- 页文件名三位数位置编号（`Save.cs:51`），不是 Guid。
- 压缩：`CompressionLevel.Optimal`（Deflate）；无显式时间戳。
- 读侧用 `ZipArchive.GetEntry` 精确匹配，**大小写敏感**；按 `manifest.pages[].index` 排序（`Load.cs:54-56`）。

## 4. manifest.json

`WbixManifest.cs:14-23`：

```jsonc
{
  "format": "wbix",
  "version": 3,
  "createdUtc": "2026-10-31T10:00:00.0000000+00:00",
  "currentIndex": 0,
  "pages": [ { "id": "<guid>", "index": 0, "path": "pages/page-000.json" } ],
  "resources": [ { "id": "<guid>", "type": "...", "path": "assets/...",
                   "contentType": "image/png", "meta": { "k": "v" } } ],
  "viewportCameraWorld": { "x": 0, "y": 0 },
  "viewportZoom": 1,
  "viewportSizeDip": { "x": 1280, "y": 720 }
}
```

- `currentIndex` 读入时 clamp 到合法范围。
- 三个 `viewport*` 字段可空（`null` 时按默认视口）。

## 5. pages/page-XXX.json

`WbixPagePayload.cs:16-19`：`{ id, strokes[], elements[] }`。

### 5.1 笔迹条目（`InkItemSnapshotJsonConverter.cs`）

v3 包装形态（`:87-95`）：

```jsonc
{ "kind": "stroke", "stroke": { "points": [ { "position": {"x":0,"y":0}, "pressure":0.5 } ],
                                "colorRgba": {"x":0,"y":0,"z":0,"w":1},
                                "baseSize": 3, "enablePressure": true } }
{ "kind": "line|rect|ellipse|arrow", "shape": { "start": {...}, "end": {...},
                                                "colorRgba": {...}, "width": 3 } }
```

- `kind` 会 `Trim()` 后比较（`:130,:144-146`）。
- `kind` 缺省或 `null` → 视为 `"stroke"`（`:174-177`）。
- **v1/v2 扁平条目**（无 `kind` 包装）被 shape-sniff 接受：有 `points` 即笔迹，否则按形状字段嗅探，并包装为 `kind="stroke"`（`:109-116`）。

### 5.2 元素条目（`Save.cs:267-342`）

```jsonc
{ "type": "text|link|media|file",
  "data": { "id": "<guid>", "layer": "belowInk|aboveInk",
            "positionWorld": {"x":0,"y":0}, "sizeWorld": {"x":320,"y":180}, "order": 0,
            "text": "...", "url": "...", "title": "...", "kind": "...",
            "sourcePath": "...", "displayName": "...", "resourceId": "<guid>" } }
```

`data` 是 `JsonElement`（不透明透传），各字段按元素类型可选。

## 6. 版本矩阵

| 版本 | 形态 | 读侧处理 |
|---|---|---|
| v1 / v2 | 扁平条目（无 `kind` 包装） | shape-sniff 后包装为 `kind="stroke"` |
| v3 | `{kind, stroke|shape}` | 直接解析 |

- **现状**：`version` 只做 `1..3` 门禁（`Load.cs:45`），**从不按版本分支解析逻辑**；v1/v2 与 v3 靠形态嗅探共存。
- 旧文件另存时自动升级为 v3。
- **已决策（2026-10-31）**：`version` 升级为真正的兼容契约，读侧按版本分派（`WbixPageReaderV1V2` / `WbixPageReaderV3` + `WbixPageReaderRegistry.For(version)`），v4 起有明确落点；v3 保留扁平条目兜底并 Warn 一次。详见 `design.md` D11 与 `implement.md` P0.7。
- 网页端相应实现**按版本分派的两套读路径**，写出固定 v3。

## 7. 错误与容忍策略

**致命（抛 `InvalidDataException`）**：缺 `manifest.json`、`format != "wbix"`、`version` 越界、页路径不安全、页 payload 为 `null`。

**容忍（Warn 后跳过，不中断）**：单个元素解析失败、未知元素 `type`、不安全资源路径、单资源 > 32 MiB、资源累计 > 256 MiB、资源提取失败、未知 ink `kind`、`strokes: null`。

**元素缺字段默认值**（`Load.cs:183-190`）：`id` → 新 Guid；`positionWorld` → (0,0)；`sizeWorld` → (320,180)；`order` → 数组下标；`layer` → `belowInk`。

## 8. 导出器

### 8.1 PNG（`BoardRasterExporter.cs`）

- D2D + WIC `32bppPBGRA`，`SetResolution(dpi)` 写入 DPI。
- **自动模式**：尺寸 = 内容边界 + 2×`PaddingDip`（默认 24），`dpi = min(请求, 16384*96/w, 16384*96/h)`，下限 12。
- **固定画幅**：像素夹 `[1,16384]`，`dpi = 96`，`zoom = min(avail/content)` 居中。
- 背景始终不透明，**无透明模式**。
- 批量命名：`{yyyy-MM-dd}-{index+1:000}.png`，输出到 `WindBoard-{yyyy-MM-dd}` 子目录（`ExportFlow.cs:185-231`）。

### 8.2 PDF（`BoardPdfExporter.cs`）

- 手写 PDF-1.4。对象 1 = catalog，2 = pages，每页一个 image XObject（`/DeviceRGB` 8bpc `/FlateDecode`）。
- 页面尺寸 pt = `px * 72 / dpi`。
- 内容流固定为 `q\n w 0 0 h 0 0 cm\n/Im0 Do\nQ\n`。
- 数字格式 `"0.###"` invariant culture。
- **无 `/Info` 元数据**；trailer 只有 `/Size` 与 `/Root`；xref 用字节偏移；文件头 `%PDF-1.4\n%âãÏÓ\n`。

## 9. `.wbi`（旧格式，v1 不做）

- `WbiWorkspaceImporter.cs`，仅支持 v1.0（`MaxSupportedVersion = new(1,0)`）。
- manifest 为 snake_case（`WbiFormatModels.cs:15-112`），页在 `pages/{id}.json`。
- 笔迹用 `ISF InkStrokeContainer.LoadAsync`（`:443-444`）——**ISF 是 Windows 专有序列化格式，浏览器解析成本过高**。
- 附件转 elements（`:296-342`），按 `z_index` 排序。

## 10. 现有测试与缺口

已覆盖：`WindBoard.Tests/Board/Persistence/WbixWorkspaceSerializerTests.cs`、`WbixWorkspaceSerializerFailureTests.cs`、`BoardInkItemCodecTests.cs`、`BoardWorkspaceSnapshotApplierTests.cs`、`Features/Import/Wbix/WbixPreviewReaderTests.cs`、`Features/Import/Wbi/WbiWorkspaceImporterTests.cs`、`Features/Export/Services/BoardRasterExporterTests.cs`；E2E `WindBoard.UITests/ImportWbixTests.cs` + `WbixTestFileFactory.cs`。

**缺口（网页端必须自己锁定）**：没有任何测试锁定 JSON 文本形态（缩进/键序/`null` 输出/数字格式）、zip 条目顺序、`createdUtc` 格式、PDF 字节结构、WBI↔WBIX 媒体 `resourceId` 往返、`assets/elements` 命名。

**已决策（2026-10-31）**：文本形态与 zip 条目顺序的缺口由桌面端 P0.8 的字节级 golden 补齐（语料 `WindBoard.Tests/Fixtures/wbix/`），网页端复用同一批语料做 conformance；PDF 字节结构与 `resourceId` 往返仍由网页端自测。

## 11. 对网页端的直接结论

1. 读：按 `version` 分派（v1/v2 扁平、v3 包装 + 扁平兜底）；写：固定 v3。
2. 所有数值按 **float32** 处理；写出用 float32 最短往返格式。
3. `null` 成员必须写；非 ASCII 建议按 `JavaScriptEncoder.Default` 规则转义（便于字节级 diff）。
4. 容忍策略必须照抄，否则"能打开桌面打不开的文件"会变成 bug。
5. 死代码 `WindBoard/Persistence/IBoardPersistenceService.cs:7` 无实现，与网页端无关。
