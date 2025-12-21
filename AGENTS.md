# AGENTS.md

This file provides guidance to agents when working with code in this repository.

## Project Specifics (Non-Obvious)
- **UI Framework**: Uses `MaterialDesignThemes` (v5.3.0) with Material Design 3 theme.
- **Core Component**: `InkCanvas` is the central element, configured with specific defaults.
- **Window Behavior**: Main window defaults to `Maximized` state.
- **Localization**: UI labels and comments use **Simplified Chinese (zh-CN)**.
- **Target Framework**: .NET 10.0 Windows WPF application

## Coding Conventions
- **Parameter Passing**: XAML `Tag` attributes are heavily used to pass data (e.g., color codes, stroke thickness) to event handlers.
- **Ink Configuration**: 
  - Default stroke: White, Thickness 3.
  - Preset thicknesses: 3, 6, 9 (mapped to Tag values "3", "6", "9" in XAML).
  - Preset colors: 9 specific colors defined in `MainWindow.xaml` .
- **Touch Gestures**: Complex pinch-to-zoom implementation with snapshot-based tracking to prevent drift.
- **Zoom System**: InkCanvas stroke thickness inversely scales with zoom to maintain visual consistency.

## Critical Gotchas
- **Color Handling**: `ColorConverter.ConvertFromString` is used; failures silently default to `Colors.White`.
- **Touch Event Handling**: Touch events must be marked as `Handled = true` during multi-touch gestures to prevent promotion to mouse events.
- **Zoom Implementation**: Uses `ScaleTransform` on parent Grid, not on InkCanvas directly.
- **Panning**: Spacebar + mouse drag enables panning mode (temporarily sets EditingMode to None).
- **Dispatcher Usage**: UI updates require `Dispatcher.InvokeAsync` with `DispatcherPriority.Loaded` for proper timing.
