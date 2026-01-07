# Proposal: Refactor Ink Engine (InkCanvas Replacement)

## Why

WindBoard currently depends on WPF `InkCanvas` + `StrokeCollection` for both **rendering** and **editing** (selection/eraser). This has three long-term issues:

- **Performance ceiling**: rendering + hit-testing cost grows with total stroke/point count; the code already contains mitigations (e.g., splitting long strokes to avoid “single stroke gets slower and slower”).
- **Limited extensibility**: custom brushes (texture/highlighter blend, calligraphy nib, variable-width shaders), layered rendering, and granular caching are difficult because `Stroke`/`DrawingAttributes` is the only primitive.
- **Tight coupling**: core systems (undo/redo, page switching, import/export) are coupled to WPF Ink types, making it hard to evolve ink algorithms independently.

## What Changes

This change proposes a **phased migration** that introduces a new ink engine architecture and (after parity) removes the legacy InkCanvas backend:

1. **Introduce an ink-backend abstraction** used by `InkMode` and services (no user-visible behavior change).
2. **Define a backend-agnostic stroke model** (stable IDs + point/pressure/time + brush attributes) and adapters to/from WPF `Stroke` for compatibility.
3. **Add a custom renderer backend** (`InkSurface`) behind a feature flag:
   - Incremental rendering for the active stroke (low latency)
   - Cached rendering for finalized strokes via tile baking (bound redraw to dirty tiles)
4. **Add hit-testing + editing services** for the custom backend (selection move/resize/copy/reorder + point eraser with partial erase), and gate “default switch” on parity.
5. **Update WBI stroke payload** to store the backend-agnostic model when needed; exported files MAY require a newer WindBoard version to open.
6. **Remove the legacy InkCanvas backend** and any backend-selection setting once the custom backend is the only supported implementation.

## Impact

- **Default behavior**: the custom backend becomes the only backend after parity; the legacy InkCanvas backend and related settings are removed.
- **Performance**: enables incremental rendering, caching, and faster hit-testing that are not possible with `InkCanvas` alone.
- **Compatibility**: importing existing `.wbi` (v1.0 / ISF) remains supported; new exports may move to a versioned format that older WindBoard versions are not required to open.
- **Risk**: editing parity (partial erasing + selection operations) and tile cache correctness are the main functional risks; mitigated by feature flag + parity gate.
