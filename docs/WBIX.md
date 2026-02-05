# WBIX（WindBoard Interchange）格式说明（v2）

本文档用于说明 WindBoard 的私有交换格式 `.wbix`，便于后续开发、适配与扩展（例如导出/导入图片、视频等页面内容）。

## 1. 概述

WBIX 是一个以 **Zip 容器**承载的白板工作区文件：

- 扩展名：`.wbix`
- 容器：Zip（可用 7-Zip 等工具直接查看内容）
- 文本内容：JSON（UTF-8，`camelCase`，默认带缩进，允许尾随逗号，忽略注释，字段名大小写不敏感）
- 二进制内容：放在 `assets/` 目录（例如封面图 `assets/cover.png`）

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
  cover.png        （可选：封面图，v2 导出会尝试生成）
  ...              （预留：后续可存放图片/视频/音频等）
```

说明：

- `manifest.json`：清单与索引（版本、页列表、资源列表、当前页等）。
- `pages/page-XXX.json`：每页数据（v1/v2 仅包含笔迹 strokes，elements 预留）。
- `assets/`：资源二进制文件目录（v2 主要用于 `cover.png`）。

## 3. manifest.json

### 3.1 字段说明

`manifest.json` 对应代码中的 `WbixManifest`：

- `format`：固定为 `"wbix"`。
- `version`：格式版本号（当前导出为 `2`；读取兼容 `1~2`）。
- `createdUtc`：创建时间（UTC，ISO 8601）。
- `currentIndex`：当前页索引（0 基）。
- `pages`：页面列表（包含页 `id`、`index`、`path`）。
- `resources`：资源列表（预留扩展位，v1/v2 可为空；v2 导出会尝试添加封面图资源条目）。

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

### 3.2 v2 的封面图资源（assets/cover.png）

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
  "version": 2,
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
- `strokes`：笔迹列表（v1/v2 主体数据）。
- `elements`：页面元素列表（预留扩展位；v1/v2 导出为空数组）。

### 4.1 strokes（笔迹）结构（v1/v2）

`strokes` 的每个条目对应 `StrokeSnapshot`：

- `points`：点列表（`StrokePointSnapshot`）。
- `colorRgba`：颜色（`Vector4`：`x/y/z/w` 分别表示 `R/G/B/A`，范围一般为 0~1）。
- `baseSize`：笔迹基础尺寸（世界坐标下的直径，单位与页面坐标一致）。
- `enablePressure`：是否启用压感（若为 true，会根据 `pressure` 调整笔宽）。

`points` 的每个条目对应 `StrokePointSnapshot`：

- `position`：点位置（`Vector2`：`x/y`，世界坐标，单位与画布逻辑坐标一致）。
- `pressure`：压感（通常 0~1，未启用压感时可固定为 1）。

> 坐标说明：WBIX 记录的是“世界坐标（DIP 近似）”，不是屏幕像素坐标。导入后由视口与渲染逻辑决定实际显示。

### 4.2 elements（页面元素）扩展位

`elements` 的每个条目对应 `WbixPageElement`：

- `type`：元素类型（例如 `image`、`video`、`stickyNote`、`shape` 等）。
- `data`：半结构化数据（`JsonElement`），用于承载该类型元素的具体字段。

建议的扩展方向（供后续开发参考）：

- `type=image`：`data` 可包含 `resourceId`（引用 `manifest.resources[].id`）、`transform`（位置/缩放/旋转）、`size`、`opacity` 等。
- `type=video`：`data` 可包含 `resourceId`、`posterResourceId`（封面）、`startTime`、`duration` 等。
- `type=shape`：`data` 可包含矢量参数、边框/填充颜色等。

兼容性建议：

- 导入端应 **忽略未知 `type`** 的元素（或保留原始 JSON 以便后续再导出），避免旧版本无法打开新文件。
- 写入端新增字段时应尽量保持可选（不破坏旧读者）。

### 4.3 示例（page-000.json）

```json
{
  "id": "2f6b35f7-9a6f-4c76-9a5d-2e9d0c5c3b7f",
  "strokes": [
    {
      "points": [
        { "position": { "x": 10.5, "y": 20.25 }, "pressure": 0.5 },
        { "position": { "x": 12.0, "y": 24.0 }, "pressure": 0.8 }
      ],
      "colorRgba": { "x": 0.1, "y": 0.2, "z": 0.3, "w": 1.0 },
      "baseSize": 3.25,
      "enablePressure": true
    }
  ],
  "elements": []
}
```

## 5. 版本与兼容性策略

当前读取逻辑约束（v2）：

- `format` 必须为 `"wbix"`（不区分大小写）。
- `version` 必须为 `1~2`（大于 2 视为不支持）。
- 页面按 `manifest.pages[].index` 排序后加载，保证顺序稳定。

推荐的升级策略：

- **新增字段优先**：在不破坏旧字段的前提下扩展（manifest/resources/elements）。
- **保留旧行为**：旧读者忽略未知字段/未知资源/未知元素类型。
- **必要时再升主版本**：只有在无法兼容的结构变更时才提升 `version`。

## 6. 安全与健壮性建议（导入端）

WBIX 属于外部输入，导入端建议：

- 限制解压后单文件大小、总大小与条目数量，防止压缩炸弹。
- 校验 `resources[].path` 与 `pages[].path` 不允许路径穿越（例如 `../`）。
- 对 JSON 数组长度、点数等做上限限制，避免 OOM 或极端耗时。
- 对 `contentType` 与实际内容做基本一致性校验（可选）。

## 7. 开发定位（代码入口）

- 序列化实现：`WindBoard/Board/Persistence/Wbix/WbixWorkspaceSerializer.cs`
- 清单模型：`WindBoard/Board/Persistence/Wbix/WbixManifest.cs`
- 页面模型：`WindBoard/Board/Persistence/Wbix/WbixPagePayload.cs`
- 资源写入模型：`WindBoard/Board/Persistence/Wbix/WbixResourceFile.cs`
