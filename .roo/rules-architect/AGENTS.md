# Project Architecture Rules (Non-Obvious Only)

- **Zoom System Architecture**: Uses `ScaleTransform` on parent Grid (`CanvasHost`), not directly on `InkCanvas` - this prevents stroke distortion.
- **Touch Gesture Architecture**: Complex pinch-to-zoom uses snapshot-based tracking (`_lastGestureP1`, `_lastGestureP2`) to prevent drift.
- **Event Handling Architecture**: XAML `Tag` attributes are the primary parameter passing mechanism between UI and code-behind.
- **Stroke Scaling Architecture**: Stroke thickness inversely scales with zoom (`_baseThickness / currentZoom`) for visual consistency.
- **UI Layer Architecture**: Three-layer structure: ScrollViewer (viewport) > Grid (scaling container) > InkCanvas (drawing surface).
- **Tool Architecture**: Three main tools (Pen, Eraser, Select) with Pen having sub-options (thickness, color) in a popup.
- **Dispatcher Architecture**: UI updates require `Dispatcher.InvokeAsync` with `DispatcherPriority.Loaded` for proper timing after layout.
- **Touch/Mouse Architecture**: Touch events must be marked as `Handled = true` during multi-touch to prevent promotion to mouse events.
- **Panning Architecture**: Spacebar + mouse drag temporarily sets `EditingMode` to `None` and captures mouse for panning.
- **Auto-Expansion Architecture**: Canvas automatically expands when drawing near edges (1000px threshold, 2000px step) with delayed content shifting for left/top expansion.
- **Eraser Architecture**: Custom eraser cursor overlay that scales inversely with zoom; uses `UseCustomCursor = true` to disable default InkCanvas cursor.
- **Multi-Page Architecture**: Page management system with preview rendering via `Services/PagePreviewRenderer.cs` and `Models/BoardPage.cs`.
- **Partial Class Architecture**: Code organized into partial classes by functional area (UI, Touch, Mouse, ZoomPan, Eraser, AutoExpand, Pages).
- **Event Handling Architecture**: Uses `AddHandler` with `handledEventsToo=true` for mouse events in eraser mode to bypass InkCanvas event handling.