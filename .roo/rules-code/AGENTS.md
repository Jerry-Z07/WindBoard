# Project Coding Rules (Non-Obvious Only)

- **XAML Tag Usage**: Always use `Tag` attributes in XAML to pass data to event handlers (color codes, stroke thickness values).
- **InkCanvas Configuration**: Default stroke thickness is 3 (not 8) - see `_baseThickness = 3.0` in `MainWindow.xaml.cs`.
- **Thickness Values**: Use Tag values "3", "6", "9" for stroke thickness presets.
- **Color Handling**: Always wrap `ColorConverter.ConvertFromString` calls in try-catch blocks - failures default to `Colors.White`.
- **Touch Event Handling**: Must set `e.Handled = true` during multi-touch gestures to prevent promotion to mouse events.
- **Zoom Implementation**: Apply `ScaleTransform` to parent Grid (`CanvasHost`), not directly to `InkCanvas`.
- **Dispatcher Timing**: Use `Dispatcher.InvokeAsync` with `DispatcherPriority.Loaded` for UI updates after layout completion.
- **Panning Mode**: Spacebar + mouse drag temporarily sets `EditingMode` to `None` - restore previous mode after panning.
- **Stroke Scaling**: Stroke thickness inversely scales with zoom (`_baseThickness / currentZoom`) for visual consistency.