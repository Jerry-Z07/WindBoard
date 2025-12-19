# AGENTS.md

This file provides guidance to agents when working with code in this repository.

## Project Specifics (Non-Obvious)
- **UI Framework**: Uses `MaterialDesignThemes` (v5.3.0) with Material Design 3 theme.
- **Core Component**: `InkCanvas` is the central element, configured with specific defaults (Background: #2E2F33).
- **Window Behavior**: Main window defaults to `Maximized` state.
- **Localization**: UI labels and comments use **Simplified Chinese (zh-CN)**.

## Coding Conventions
- **Parameter Passing**: XAML `Tag` attributes are heavily used to pass data (e.g., color codes, stroke thickness) to event handlers.
- **Ink Configuration**: 
  - Default stroke: White, Thickness 8.
  - Preset thicknesses: 8, 16, 24.
  - Preset colors: 9 specific colors defined in `MainWindow.xaml`.

## Critical Gotchas
- **Color Handling**: `ColorConverter.ConvertFromString` is used; failures silently default to `Colors.White`.
- **Hot Reload**: `dotnet watch run` is the preferred way to run for rapid UI iteration.
