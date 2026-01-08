# WBI 文件格式

WBI（`.wbi`）是 WindBoard 的专用可还原格式：本质是一个 ZIP 包，包含清单、每页元数据、笔迹数据以及可选的资源文件。

实现位置：

- 导出：`Services/Export/WbiExporter.cs`
- 导入：`Services/Export/WbiImporter.cs`
- 模型：`Models/Wbi/WbiManifest.cs`、`Models/Wbi/WbiPageData.cs`

## 容器结构（ZIP）

WBI 文件的典型结构：

```
manifest.json
pages/
  page_001.json
  page_001.isf        (可选：该页存在笔迹时)
  page_002.json
  ...
assets/               (可选：当 include_image_assets=true 且存在图片附件时)
  <guid>.<ext>
  ...
```

## manifest.json

字段（见 `Models/Wbi/WbiManifest.cs`）：

- `version`：格式版本（当前导出固定为 `"1.0"`）
- `min_compatible_version`：最低兼容版本（导入时会做兼容检查）
- `app_version`：导出时 WindBoard 版本号（可空）
- `created_at`：UTC 时间
- `page_count`：页面数量
- `include_image_assets`：是否打包图片资源
- `pages`：页面引用列表（按导出顺序）
  - `id`：页面 id（导出时形如 `page_001`）
  - `number`：页面编号（导出时使用 `BoardPage.Number`）

## pages/page_XXX.json

字段（见 `Models/Wbi/WbiPageData.cs`）：

- `number`：页码
- `canvas_width` / `canvas_height`：画布尺寸
- `zoom` / `pan_x` / `pan_y`：视图状态（相机式缩放/平移）
- `strokes_file`：笔迹文件名（例如 `page_001.isf`；没有笔迹则为空）
- `attachments`：附件列表

## pages/page_XXX.isf

- 存储 WPF Ink 的 ISF（Ink Serialized Format），由 `StrokeCollection.Save(stream)` 写入。
- 导入时使用 `new StrokeCollection(stream)` 读取。

## attachments（附件数据）

附件结构（见 `Models/Wbi/WbiPageData.cs` 的 `WbiAttachmentData`）：

通用字段：

- `id`：Guid
- `type`：`Image` / `Video` / `Text` / `Link`（由 `BoardAttachmentType.ToString()` 导出）
- `x` / `y` / `width` / `height`：位置与尺寸（画布坐标）
- `z_index`：层级序号（同一层内越大越靠上）
- `is_pinned_top`：是否置顶（置顶附件显示在笔迹上方）

按类型扩展字段：

- Image：
  - 当 `include_image_assets=true` 且导出时图片路径存在：写入 `asset_file`，并把图片资源写入 `assets/`。
  - 否则仅写入 `file_path`（导入时如果本地找不到文件会记录缺失）。
- Video：写入 `file_path`（只保存路径，不打包）
- Text：写入 `text`
- Link：写入 `url`

> [!NOTE]
> 当前实现会对嵌入的图片资源进行压缩以减小体积；WBI 不承诺“逐字节等同于原文件”。

## 版本兼容

- 导入端目前支持的最高版本为 `1.0`（`WbiImporter.MaxSupportedVersion`）。
- 若 `min_compatible_version` 高于当前支持版本，导入会失败并提示需要更新 WindBoard。

## 导入时的资源处理

- 当 `include_image_assets=true` 且存在 `asset_file`：
  - 导入会把 `assets/` 内资源解压到临时目录（可通过 `assetExtractFolder` 指定；未指定则使用系统临时目录）。
  - 导入后的 `BoardAttachment.FilePath` 会指向解压后的路径。
- 当仅有 `file_path`：导入会直接使用该路径；若本地不存在，会把缺失项加入 `MissingResources`，UI 侧通常用占位卡片显示。

## WBI 当前不包含的内容（重要）

WBI 主要面向“画布内容还原”，目前不包含：

- 应用级设置（例如背景色、伪装、视频展台配置等）
- 画笔面板当前选中的颜色/粗细（这些属于运行时状态）
