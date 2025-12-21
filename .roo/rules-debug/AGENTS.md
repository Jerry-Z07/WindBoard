# Project Debug Rules (Non-Obvious Only)

- **Touch Event Debugging**: Multi-touch gestures require `e.Handled = true` to prevent promotion to mouse events - check if touch events are being properly handled.
- **Zoom Drift Issues**: Pinch-to-zoom uses snapshot-based tracking to prevent drift - verify `_lastGestureP1` and `_lastGestureP2` are being updated correctly.
- **Dispatcher Timing**: UI updates after layout require `Dispatcher.InvokeAsync` with `DispatcherPriority.Loaded` - incorrect timing causes visual glitches.
- **Stroke Scaling Debug**: Stroke thickness should inversely scale with zoom (`_baseThickness / currentZoom`) - check `UpdatePenThickness` function.
- **Panning Mode Issues**: Spacebar + mouse drag sets `EditingMode` to `None` - ensure previous mode is restored after panning.
- **Color Conversion Failures**: `ColorConverter.ConvertFromString` failures silently default to `Colors.White` - check color codes in Tag attributes.