# WBIX（WindBoard Interchange）格式说明（v2）

本文档用于说明 WindBoard 的私有交换格式 `.wbix`，便于后续开发、适配与扩展（例如导出/导入图片、视频等页面内容）。

## 1. 概述

WBIX 是一个以 **Zip 容器**承载的白板工作区文件：

- 扩展名：`.wbix`
- 容器：Zip
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
  cover.png              （可选：封面图，v2 导出会尝试生成）
  elements/
    <elementId>.png      （可选：页面图片元素内嵌资源）
  ...                    （预留：后续可存放视频/音频等资源）
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
- `elements`：页面元素列表（文本/链接/媒体/文件等；导入端应忽略未知 `type` 保持前向兼容）。

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

## 5. 约束

当前读取逻辑约束（v2）：

- `format` 必须为 `"wbix"`（不区分大小写）。
- `version` 必须为 `1~2`（大于 2 视为不支持）。
- 页面按 `manifest.pages[].index` 排序后加载，保证顺序稳定。


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
