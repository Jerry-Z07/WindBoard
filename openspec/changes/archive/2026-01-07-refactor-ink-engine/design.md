# Design: Refactor Ink Engine (InkCanvas Replacement)

## Goals

- Improve inking performance on large pages by enabling:
  - bounded per-frame work during writing
  - cached rendering of historical strokes
  - hit-testing that scales sub-linearly via spatial indexing
- Unlock extensibility (new brushes, rendering strategies, persistence) without binding everything to WPF `Stroke`.
- Remove the legacy `InkCanvas` backend once parity is proven.

## Non-goals (for this change)

- Rewriting the full UI shell (toolbars/pages/attachments).
- Changing zoom/pan architecture (must keep camera-style `RenderTransform` constraints).
- Preserving the ability for older WindBoard versions to open newly exported files.

## Current State (Evidence in Code)

- Writing performance mitigation exists:
  - `InkMode` batches point appends and splits long strokes into segments to avoid incremental-update slowdowns.
- High-sample stylus input is optimized:
  - RTS adapter batches packets and reuses buffers to reduce GC pressure.
- Editing still relies on `InkCanvas` built-ins:
  - `InkCanvasEditingMode.EraseByPoint` and `InkCanvasEditingMode.Select`.
- Persistence is ISF-centric:
  - WBI stores per-page `StrokeCollection.Save(stream)` / `new StrokeCollection(stream)`.

These are good optimizations, but they remain constrained by the `InkCanvas` rendering/editing model.

## Proposed Architecture

### Chosen Constraints (Project Decision)

- Default backend switch gate requires full editing parity: point eraser with partial erase + stroke selection (move/resize/copy/reorder/delete) in addition to writing.
- Selection semantics do not need to match `InkCanvas` implementation details, but user-visible features must not be reduced.
- No “hybrid editing” period: when the custom backend is enabled, selection/erasing MUST be handled by the custom backend (not delegated to a hidden `InkCanvas`).
- Rendering cache strategy targets tile baking for finalized strokes.
- Stroke shape does not need to be pixel-identical to the current WPF ink output, as long as it is visually acceptable.
- WBI export is allowed to introduce a new version that older WindBoard versions are not required to open; importing existing WBI v1.0 remains supported.

### 1) Backend Abstraction

Introduce an “ink backend” abstraction that owns:

- **stroke creation/editing** (begin/update/end; erase; selection)
- **rendering** (active stroke updates; finalized stroke caching)
- **page binding** (swap page ink content without cloning)

Backend:

- **Custom backend**: renders via a new `InkSurface` control and uses custom hit-testing.

The rest of the app should depend on the abstraction, not directly on `InkCanvas.Strokes`.

### 2) Backend-agnostic Stroke Model

Define a model suitable for both rendering and persistence:

- Stable `StrokeId` (GUID or deterministic ID) and metadata:
  - color, logical thickness (in DIP), brush kind
  - input device info (optional), timestamps (optional)
- Point stream:
  - canvas DIP coordinates
  - pressure (0..1) + optional contact size
  - time (ticks) for replay/speed-based effects

Adapters:

- WPF `Stroke` -> model (import, transition, testing)
- model -> WPF `Stroke` (legacy rendering/export compatibility, best-effort)

### 3) Rendering Strategy (Custom Backend)

Minimum viable rendering (first milestone):

- **Two-layer rendering**
  - Dynamic layer: active strokes (updated incrementally)
  - Static layer: finalized strokes, cached as frozen visuals/geometries
- Avoid per-frame traversal of all historical strokes.

Target rendering cache strategy (required before default switch):

- **Tile baking**: bake finalized strokes into fixed-size bitmap tiles; invalidate only dirty tiles during edits and partial erasing.
- **Async build** (optional): generate frozen geometries / baked tiles off-thread for finalized strokes and marshal back to the UI thread.

### 4) Hit Testing & Editing (Custom Backend)

Provide services for:

- Point erase with **partial stroke erasing** (splitting strokes) and undo/redo.
- Selection:
  - rectangle selection (MVP)
  - move / resize
  - copy / delete
  - reorder (bring-to-front within the stroke render order)
  - lasso selection (later)

Implementation approach:

- Maintain a spatial index over stroke segments (grid/R-tree).
- For erasing, use segment-distance tests with a configurable eraser shape and split the stroke model into remaining sub-strokes.

Recommended erase splitting rules (initial):

- Eraser is treated as a shape in canvas DIP (rectangle, matching current UX).
- When an erase sample hits a stroke, remove affected polyline portions and split the stroke into remaining sub-strokes.
- Drop “tiny remnants” to reduce fragmentation (e.g., < 2 mm length or < 3 points).
- Clamp the maximum number of fragments created per erase operation to avoid worst-case explosions; if exceeded, fall back to removing the whole stroke.

### 5) Migration / Parity Gate

- The legacy backend is removed once the custom backend supports:
  - writing (pen/touch), smoothing + simulated pressure behaviors
  - point eraser with partial erase
  - selection and manipulation hooks (move/resize/copy/delete/reorder)
  - undo/redo correctness
  - WBI import of existing v1.0 files
- After the switch, there is no backend-selection setting.

## Persistence (WBI) Strategy

Short term (migration-friendly):

- Keep importing WBI v1.0 stroke storage as ISF (`.isf`), converting to the backend-agnostic model on load.
- Export MAY continue to write ISF as a best-effort fallback early in migration, but this is not required for backward app compatibility.

Long term (full-fidelity model storage):

- Extend WBI with a dedicated ink payload for the backend-agnostic model (e.g., `pages/page_XXX.inkbin`).
- When exporting model ink, the manifest MUST set `min_compatible_version` high enough to prevent older WindBoard versions from importing unsupported payloads.
- ISF output becomes optional and may be omitted once the custom backend becomes the default.

## Testing Strategy

- Unit tests (xUnit):
  - model<->Stroke conversion
  - geometry generation determinism (golden tests on point sets)
  - hit-testing / erase splitting correctness
- STA tests for WPF-specific components (renderer visual tree, interop).
