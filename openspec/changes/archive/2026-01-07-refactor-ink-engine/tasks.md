## 1. Implementation (Phased)

- [x] 1.1 Add an ink-backend abstraction used by `InkMode` (begin/update/end stroke; cancel; clear)
- [x] 1.2 Implement `InkCanvas` legacy backend adapter (wrap current `StrokeCollection` behavior)
- [x] 1.3 Introduce a backend-agnostic stroke model (IDs + points + metadata) and WPF `Stroke` adapters
- [x] 1.4 Wire page switching + undo/redo through the abstraction (keep behavior unchanged under legacy backend)
- [x] 1.5 Add a feature flag / setting to choose backend (default: legacy)

## 2. Custom Backend (Behind Feature Flag)

- [x] 2.1 Add `InkSurface` renderer control with dynamic layer + tile-baked cache layer (no editing yet)
- [x] 2.2 Implement model-based incremental rendering for active strokes
- [x] 2.3 Implement tile baking for finalized strokes and dirty-tile invalidation; verify large pages do not redraw fully

## 3. Editing Parity (Behind Feature Flag)

- [x] 3.1 Implement spatial index for stroke segments in the model
- [x] 3.2 Implement point eraser with partial erase (stroke splitting) + undo/redo
- [x] 3.3 Implement stroke selection with move/resize/copy/delete/reorder; keep user-visible features (Selection Dock) without requiring InkCanvas selection semantics

## 4. Compatibility & Validation

- [x] 4.1 Keep importing WBI v1.0 (ISF) and add versioned WBI ink payload export for the model (older app versions not required to open)
- [x] 4.2 Add/extend unit tests for model conversion, tile caching behavior, hit-testing and erase splitting
- [x] 4.3 Run `dotnet build WindBoard.sln` and `dotnet test WindBoard.sln`

## 5. Remove Legacy Backend

- [x] 5.1 Remove the legacy `InkCanvas` backend implementation and wiring
- [x] 5.2 Remove any backend-selection setting/flag and related code
- [x] 5.3 Ensure legacy pages (ISF/WBI v1.0) migrate to the model backend
- [x] 5.4 Run `dotnet build WindBoard.sln` and `dotnet test WindBoard.sln`
