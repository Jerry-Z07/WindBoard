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
  cover.png        (Optional: cover image, v2 export will attempt to generate)
  ...              (Reserved: can store images/videos/audio, etc. in the future)
```

Explanation:

- `manifest.json`: Manifest and index (version, page list, resource list, current page, etc.).
- `pages/page-XXX.json`: Data for each page (v1/v2 only contains strokes, `elements` is reserved).
- `assets/`: Directory for resource binary files (v2 primarily uses `cover.png`).

## 3. manifest.json

### 3.1 Field Description

`manifest.json` corresponds to `WbixManifest` in code:

- `format`: Fixed as `"wbix"`.
- `version`: Format version number (currently exported as `2`; reading is compatible with `1~2`).
- `createdUtc`: Creation time (UTC, ISO 8601).
- `currentIndex`: Current page index (0-based).
- `pages`: Page list (includes page `id`, `index`, `path`).
- `resources`: Resource list (reserved extension point, can be empty for v1/v2; v2 export will attempt to add a cover image resource entry).

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
- `elements`: Page element list (reserved extension point; exported as an empty array for v1/v2).

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

### 4.2 elements (Page Elements) Extension Point

Each entry in `elements` corresponds to `WbixPageElement`:

- `type`: Element type (e.g., `image`, `video`, `stickyNote`, `shape`, etc.).
- `data`: Semi-structured data (`JsonElement`) for carrying specific fields of that element type.

Suggested extension directions (for future development reference):

- `type=image`: `data` can contain `resourceId` (referencing `manifest.resources[].id`), `transform` (position/scale/rotation), `size`, `opacity`, etc.
- `type=video`: `data` can contain `resourceId`, `posterResourceId` (cover), `startTime`, `duration`, etc.
- `type=shape`: `data` can contain vector parameters, border/fill colors, etc.

Compatibility suggestions:

- The import side should **ignore elements with unknown `type`** (or retain the original JSON for potential re-export later) to prevent old versions from failing to open new files.
- Writers adding new fields should keep them optional whenever possible (to avoid breaking old readers).

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
  "elements": []
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