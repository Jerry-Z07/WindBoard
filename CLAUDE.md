# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

WindBoard is a WPF-based intelligent whiteboard application built with Material Design 3, featuring smooth handwriting input and multi-page management. This is a .NET 10.0 Windows application actively developed entirely with AI assistance.

**Target Framework**: `net10.0-windows10.0.26100.0`

## Build and Development Commands

Run from repository root:

```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build WindBoard.sln

# Run application
dotnet run --project WindBoard.csproj

# Run all tests
dotnet test WindBoard.sln

# Optional: Run tests with coverage
dotnet test WindBoard.sln -p:CollectCoverage=true
```

## Architecture: Input Pipeline + Mode System + Services

WindBoard uses a **staged input pipeline** that routes events through interaction modes, coordinated by `MainWindow`.

### Input Flow

1. **Raw WPF Events** → `MainWindow/MainWindow.InputPipeline.cs` captures Mouse/Touch/Stylus events
2. **Abstraction** → Events wrapped into `Core/Input/InputEventArgs` (contains device type, coordinates, modifiers, timestamp)
3. **Routing** → `Core/Input/InputManager` dispatches to current mode via `ModeController`
4. **Mode Handling** → Modes implement `OnPointerDown/Move/Up` for specific interactions

### Key Systems

**Interaction Modes** (`Core/Modes/`):
- Strategy pattern for switching between interaction behaviors
- `InkMode`: Handwriting with simulated pressure and detail-preserving smoothing
- `EraserMode`: Erasing with swipe-to-clear gesture
- `SelectMode`: Selection and manipulation of strokes/attachments
- `NoMode`: Input suppression during exclusive operations
- `ModeController`: Manages mode switching and active mode tracking

**Input Pipeline** (`Core/Input/`):
- Multi-stage event processing with filter support
- `InputManager`: Central event dispatcher
- `InputStage`: Pipeline stage abstraction
- `ExclusiveModeFilter`: Blocks input to exclusive modes
- `RealTimeStylusAdapter`: Integration for high-performance stylus input
- `InputSourceSelector`: Automatically selects between RealTimeStylus and WPF standard input

**Services Layer** (`Services/`):
- `PageService`: Multi-page management, state save/restore
- `StrokeService`: Stroke management and pen thickness control
- `StrokeUndoHistory`: Per-page undo/redo with transaction support
- `ZoomPanService`: Camera-style zoom/pan using RenderTransform (not LayoutTransform to avoid layout cascade)
- `AutoExpandService`: Automatically expands canvas when strokes approach boundaries
- `TouchGestureService`: Two-finger gesture recognition (pinch zoom, pan)
- `SettingsService`: JSON persistence to `%APPDATA%\WindBoard\settings.json`
- `ExportService`, `WbiExporter/WbiImporter`: Export to PNG/JPG/PDF/WBI formats

**Data Models** (`Models/`):
- `BoardPage`: Stores strokes, attachments, canvas size, view state (zoom/pan)
- `BoardAttachment`: Supports Image/Video/Text/Link attachments with position, size, z-index
- `AppSettings`: Application settings with automatic persistence
- `Wbi/`: WBI format models (manifest, page data)

### Critical Performance Constraints

**Do NOT violate these performance rules:**

1. **Camera Transform**: Use `RenderTransform` (not `LayoutTransform`) for zoom/pan to avoid layout cascade. Already implemented in `MainWindow.Architecture.cs:69-78`.

2. **BitmapCache Scope**: NEVER enable `BitmapCache` on `CanvasHost` (default 8000×8000 canvas causes 100+MB allocation). Only cache `Viewport` (see `SetViewportBitmapCache`).

3. **Async Image Loading**: Use asynchronous decoding for images to avoid UI blocking. `StaBitmapLoader` already exists for reuse.

## Code Organization

### MainWindow Partial Classes (`MainWindow/`)

**DO NOT** add logic to `MainWindow.xaml.cs`. Use domain-specific partial classes:

- `MainWindow.Architecture.cs`: Core initialization, mode/service setup
- `MainWindow.InputPipeline.cs`: Event routing to InputManager
- `MainWindow.Attachments.*.cs`: Attachment import, selection, external opening
- `MainWindow.Export.cs`: Export dialog and operations
- `MainWindow.Pages.cs`: Page navigation and management
- `MainWindow.SettingsSync.cs`: Settings synchronization
- `MainWindow.ToolUi.cs`: Toolbar state management
- `MainWindow.SystemDock.cs`: System tray integration
- `MainWindow.VideoPresenter.cs`: External video presenter integration

### Module Responsibilities

- `Core/`: Input abstraction, interaction modes, ink algorithms (simulated pressure, detail-preserving smoothing)
- `Services/`: Business logic (pages, strokes, zoom/pan, settings, import/export)
- `Models/`: Pure data models (no business logic)
- `Views/`: XAML + thin code-behind (move complex logic to Services/Core)
- `Resources/`, `Styles/`: Fonts (MiSans) and XAML resource dictionaries

## Ink System Details

**Writing Mode** (`Core/Modes/InkMode.cs`):
- **Smoothing**: `DetailPreservingSmoother` algorithm preserves sharp corners while smoothing curves
- **Pressure Handling**:
  - Real pressure from hardware stylus (auto-switches after sufficient samples)
  - Fallback to simulated pressure based on velocity/time for pen-like effect
  - Parameters in `SimulatedPressureParameters`, defaults in `SimulatedPressureDefaults`
- **Thickness Consistency**: Optional feature to maintain consistent stroke thickness across different writing speeds (configured via `StrokeService.SetStrokeThicknessConsistencyEnabled`)

## WBI Format (WindBoard Interchange)

WBI files (`.wbi`) are ZIP archives containing:

```
manifest.json          # Version, page count, settings
pages/
  page_001.json        # Page metadata (size, zoom, pan, attachments)
  page_001.isf         # WPF Ink Serialized Format (if strokes exist)
  page_002.json
  ...
assets/                # Optional embedded image assets
  <guid>.<ext>
  ...
```

**Implementation**: `Services/Export/WbiExporter.cs`, `Services/Export/WbiImporter.cs`
**Models**: `Models/Wbi/WbiManifest.cs`, `Models/Wbi/WbiPageData.cs`

See [`docs/dev/wbi-format.md`](docs/dev/wbi-format.md) for detailed specification.

## Testing

**Framework**: xUnit v2.9.3 + Xunit.StaFact v1.2.69

**WPF Testing**: Use `[StaFact]` for tests involving WPF types (`InkCanvas`, `StrokeCollection`, etc.)

**Test Location**: `WindBoard.Tests/` with structure mirroring main project:
- `WindBoard.Tests/Ink/`: Ink algorithms and writing modes
- `WindBoard.Tests/Services/`: Service layer tests

**Naming Pattern**: `ClassName_MethodUnderTest_ExpectedOutcome`

**Run Tests**:
```bash
dotnet test WindBoard.sln
```

## Coding Conventions

**Naming**:
- Types/Methods/Properties: `PascalCase`
- Locals/Parameters: `camelCase`
- Private fields: `_camelCase` (when needed)
- XAML elements: `PascalCase`

**Style**:
- 4-space indentation
- Nullable reference types enabled (fix warnings, don't suppress)
- Prefer explicit types, guard clauses, small single-responsibility methods
- Comments in Chinese or English; complex logic requires comments

**Code Reuse**: Always prefer reusing existing code, components, and packages over reimplementation. Check existing services/helpers before creating new ones.

**UI Thread**: WPF UI updates must occur on UI thread. Use `Task.Run` for expensive operations. Existing `StaBitmapLoader` available for async image loading.

## Commit Conventions

Follow Conventional Commits (English or Chinese):
- `feat:`, `fix:`, `refactor:`, `docs:`, `build:`, `chore:`
- Optional scope: `fix(SettingsWindow): ...`, `refactor(ZoomPanService): ...`

Always run before committing:
```bash
dotnet build WindBoard.sln
dotnet test WindBoard.sln
```

## Key Dependencies

- **MaterialDesignThemes** v5.3.0: Material Design 3 UI components
- **Newtonsoft.Json** v13.0.4: Settings persistence and WBI manifest
- **PdfSharpCore** v1.3.67: PDF export
- **System.Drawing.Common** v10.0.1: Image processing

## Important Context

- **App Settings**: Persist to `%APPDATA%\WindBoard\settings.json`, broadcast via `SettingsService.SettingsChanged`
- **Page State**: Each `BoardPage` stores its own view state (zoom/pan), restored on page switch
- **Undo System**: Per-page undo/redo with transaction support for multi-stroke operations
- **Attachment Layers**: Two `ItemsControl` layers in XAML (pinned on top, non-pinned below strokes)
- **Touch Gestures**: Configurable to require two fingers only (prevents accidental activation)
- **Camouflage Mode**: Special presentation mode for classroom/demonstration scenarios

## Reference Documentation

For deeper details, see:
- Architecture: [`docs/dev/architecture-overview.md`](docs/dev/architecture-overview.md)
- Project Structure: [`docs/dev/project-structure.md`](docs/dev/project-structure.md)
- Coding Guidelines: [`docs/dev/coding-guidelines.md`](docs/dev/coding-guidelines.md)
- Testing Guide: [`docs/dev/testing.md`](docs/dev/testing.md)
- WBI Format: [`docs/dev/wbi-format.md`](docs/dev/wbi-format.md)
- Settings Persistence: [`docs/dev/settings-persistence.md`](docs/dev/settings-persistence.md)
