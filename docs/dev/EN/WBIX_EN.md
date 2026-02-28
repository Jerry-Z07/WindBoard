# WBIX (WindBoard Interchange) Format Specification (v2)

This document describes WindBoard's private exchange format `.wbix` to facilitate subsequent development, adaptation, and extension (e.g., export/import of page content like images, videos).

## 1. Overview

WBIX is a whiteboard workspace file carried within a **Zip container**:

- Extension: `.wbix`
- Container: Zip
- Text content: JSON (UTF-8, `camelCase`, indented by default, trailing commas allowed, comments ignored, case-insensitive field names)
- Binary content: Placed in the `assets/` directory (e.g., cover image `assets/cover.png`)

Design goals:

- **Readable and extensible**: Clear structure for easy debugging and migration.
- **Forward extension points**: Future page elements (images/videos/sticky notes/shapes, etc.) are reserved via `resources` and `elements`.
- **Stable page identity**: Each page has a stable `id` for easy resource referencing and cross-version adaptation.

## 2. Package Directory Structure

The internal structure of a WBIX (Zip) package is as follows:

```
manifest.json
pages/
  page-000.json
  page-001.json
  ...
assets/
  cover.png              (Optional: cover image, v2 export will attempt to generate)
  elements/
    <elementId>.png      (Optional: embedded image resources for page elements)
  ...                    (Reserved: may store video/audio resources in the future)
```

Explanation:

- `manifest.json`: Manifest and index (version, page list, resource list, current page, etc.).
- `pages/page-XXX.json`: Data for each page (strokes + elements; unknown element types should be ignored for forward compatibility).
- `assets/`: Directory for resource binary files (v2 uses `cover.png` and embedded element images under `assets/elements/`).

## 3. manifest.json

### 3.1 Field Description

`manifest.json` corresponds to `WbixManifest` in code:

- `format`: Fixed as `"wbix"`.
- `version`: Format version number (currently exported as `2`; reading is compatible with `1~2`).
- `createdUtc`: Creation time (UTC, ISO 8601).
- `currentIndex`: Current page index (0-based).
- `pages`: Page list (includes page `id`, `index`, `path`).
- `resources`: Resource list (reserved extension point, can be empty for v1/v2; v2 export will attempt to add a cover image resource entry).
- `viewportCameraWorld`: Optional. Records viewport camera world position on export (record-only; import does not force-apply).
- `viewportZoom`: Optional. Records viewport zoom on export (record-only).
- `viewportSizeDip`: Optional. Records viewport size in DIP (useful for future view restore / preview).

Each entry in `pages` corresponds to `WbixManifestPage`:

- `id`: Page ID (corresponds to the `id` in `pages/page-XXX.json`).
- `index`: Page order index (0-based).
- `path`: Path within the Zip (e.g., `pages/page-000.json`).

Each entry in `resources` corresponds to `WbixResourceEntry`:

- `id`: Resource identifier (recommended to be stable; can use GUID, hash, etc.).
- `type`: Resource type (suggested: `image` / `video` / `audio` / `file` ...).
- `path`: Path within the Zip (recommended to be under `assets/`).
- `contentType`: MIME (e.g., `image/png`).
- `meta`: Optional metadata (key-value string dictionary, e.g., dimensions, duration, checksum, purpose, etc.).

### 3.2 v2 Cover Image Resource (assets/cover.png)

The current export attempts to generate a cover image of the first page:

- Binary path: `assets/cover.png`
- Manifest resource entry:
  - `id`: `"cover"`
  - `type`: `"image"`
  - `path`: `"assets/cover.png"`
  - `contentType`: `"image/png"`
  - `meta`: Contains `role=cover`, `pageIndex=0`, `pixelWidth`, `pixelHeight` (convenient for subsequent UI list display)

Note: The cover image is an **optional resource**; the import side should allow its absence and handle degradation (e.g., display a default thumbnail).

### 3.3 Example (manifest.json)

The following example is for illustrating the field structure (IDs/times will vary by file):

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

Page files correspond to `WbixPagePayload`:

- `id`: Page ID (consistent with the `pages` entry in the manifest).
- `strokes`: Stroke list (main data for v1/v2).
- `elements`: Page element list (text/link/media/file; import should ignore unknown `type` for forward compatibility).

### 4.1 strokes (Stroke) Structure (v1/v2)

Each entry in `strokes` corresponds to `StrokeSnapshot`:

- `points`: Point list (`StrokePointSnapshot`).
- `colorRgba`: Color (`Vector4`: `x/y/z/w` representing `R/G/B/A` respectively, range typically 0~1).
- `baseSize`: Base size of the stroke (diameter in world coordinates, unit consistent with page coordinates).
- `enablePressure`: Whether pressure sensitivity is enabled (if true, pen width adjusts based on `pressure`).

Each entry in `points` corresponds to `StrokePointSnapshot`:

- `position`: Point position (`Vector2`: `x/y`, world coordinates, unit consistent with canvas logical coordinates).
- `pressure`: Pressure sensitivity (typically 0~1, can be fixed to 1 if pressure is not enabled).

> Coordinate note: WBIX records "world coordinates (DIP approximate)", not screen pixel coordinates. Actual display is determined by viewport and rendering logic after import.

### 4.2 elements (Page elements)

Each item in `elements` corresponds to `WbixPageElement`:

- `type`: Element type (current implementation: `text` / `link` / `media` / `file`).
- `data`: Element data (semi-structured JSON) containing common fields + type-specific fields.

#### 4.2.1 Common fields (data)

- `id`: Element ID (Guid string).
- `layer`: Layer (`belowInk` / `aboveInk`).
- `positionWorld`: Top-left world position (`Vector2`: `x/y`).
- `sizeWorld`: Size (`Vector2`: `x/y`).
- `order`: Order within the same layer (import sorts by `order` and keeps it stable).

#### 4.2.2 text

- `type=text`
- `data.text`: Text content.

#### 4.2.3 link

- `type=link`
- `data.url`: URL.
- `data.title`: Optional title.

#### 4.2.4 media

- `type=media`
- `data.kind`: `image` / `video` / `audio`
- `data.displayName`: Display name (usually a file name)
- `data.sourcePath`: Optional source path (used for external references; embedded images usually set this to null to avoid leaking local absolute paths)
- `data.resourceId`: Optional resource reference (points to `manifest.resources[].id`; used for embedded images, etc.)

> Note: When `resourceId` is present, import should resolve `manifest.resources` and extract the asset from the Zip entry path.

### 4.3 Example (page-000.json)

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

## 5. Constraints

Current reading logic constraints (v2):

- `format` must be `"wbix"` (case-insensitive).
- `version` must be between `1~2` (greater than 2 is considered unsupported).
- Pages are loaded in the order sorted by `manifest.pages[].index` to ensure stable order.

## 6. Security and Robustness Suggestions (Import Side)

WBIX is external input, so the import side is advised to:

- Limit the size of single files, total size, and the number of entries after decompression to prevent zip bombs.
- Validate `resources[].path` and `pages[].path` to disallow path traversal (e.g., `../`).
- Enforce upper limits on JSON array lengths, point counts, etc., to avoid OOM or excessive processing time.
- Perform basic consistency checks between `contentType` and actual content (optional).

## 7. Development Location (Code Entry Points)

- Serialization implementation: `WindBoard/Board/Persistence/Wbix/WbixWorkspaceSerializer.cs`
- Manifest model: `WindBoard/Board/Persistence/Wbix/WbixManifest.cs`
- Page model: `WindBoard/Board/Persistence/Wbix/WbixPagePayload.cs`
- Resource writing model: `WindBoard/Board/Persistence/Wbix/WbixResourceFile.cs`
