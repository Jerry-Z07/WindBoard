# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

WindBoard is a WPF-based intelligent whiteboard application built with Material Design 3, supporting smooth handwriting input, multi-page management, and real-time ink smoothing. The project is entirely AI-developed and actively maintained.

**Target Framework**: .NET 10.0 Windows (net10.0-windows10.0.26100.0)

## Essential Commands

### Build and Run
```bash
# Restore dependencies
dotnet restore

# Build project
dotnet build

# Run application
dotnet run

# Run all tests
dotnet test

# Run tests for a specific file (example)
dotnet test --filter "FullyQualifiedName~RealtimeInkSmootherTests"
```

### Test Framework
- Uses **xUnit** for unit testing with **Xunit.StaFact** for WPF STA thread support
- Tests are located in `WindBoard.Tests/` and mirror the main project structure
- Core algorithms (ink smoothing, OneEuroFilter, services) must have corresponding unit tests

## Core Architecture

### Input Processing Pipeline

The application uses a **multi-stage input pipeline** that processes all input events (mouse, stylus, touch) through a unified architecture:

1. **Raw Input** → Device-specific event handlers (MainWindow.InputPipeline.cs)
2. **Input Normalization** → `InputEventArgs` unified format with device metadata
3. **Input Filtering** → `IInputFilter` chain (priority-sorted, e.g., ExclusiveModeFilter)
4. **Mode Dispatch** → `InputManager.Dispatch()` → `ModeController`
5. **Mode Handling** → Active interaction mode processes the input

**Key Flow**:
- `MainWindow.InputPipeline.cs` captures WPF events (Mouse*/Stylus*/Touch*)
- Builds normalized `InputEventArgs` with canvas/viewport coordinates, pressure, device type
- `InputManager.Dispatch(stage, args)` runs filters first, then dispatches to ModeController
- Filters can intercept input (return true) before it reaches modes
- `ModeController` routes to `ActiveMode ?? CurrentMode`

**Special Input Sources**:
- `RealTimeStylusManager`: Alternative low-latency stylus input path (adaptive based on device support)
- `InputSourceSelector`: Automatically chooses between RealTimeStylus vs WPF standard input

### Mode System

The application uses a **strategy pattern** for interaction modes managed by `ModeController`:

- **CurrentMode**: The default mode when no specific action is active (set via toolbar)
- **ActiveMode**: Temporary mode during an interaction (e.g., actively drawing a stroke)
- Mode lifecycle: `SwitchOn()` → handle pointer events → `SwitchOff()`
- On pointer Up, `ActiveMode` is cleared and control returns to `CurrentMode`

**Built-in Modes** (Core/Modes/):
- `InkMode`: Handles pen strokes with real-time smoothing (OneEuroFilter)
- `EraserMode`: Eraser functionality with visual cursor overlay
- `SelectMode`: Selection and manipulation of strokes/attachments
- `NoMode`: Disables all canvas interaction (used during gestures/panning)

**Mode Registration**:
```csharp
_modeController.SetCurrentMode(mode);      // Set default mode
_modeController.ActivateMode(mode);        // Temporarily override
_modeController.ClearActiveMode();         // Return to CurrentMode
```

### Real-time Ink Smoothing

The smoothing system uses the **OneEuroFilter** algorithm with adaptive parameters:

1. **RealtimeInkSmoother** (Core/Ink/RealtimeInkSmoother.cs):
   - Converts canvas DIP to screen millimeters for device-independent smoothing
   - Resamples input based on pen speed (adaptive step size)
   - Applies OneEuroFilter2D with dynamic cutoff frequencies
   - **Corner detection**: Increases filter responsiveness at sharp angles
   - **Sticky mode**: Stabilizes output when pen stops/slows down
   - Returns epsilon-filtered points to avoid micro-jitter

2. **OneEuroFilter2D** (Core/Ink/OneEuroFilter2D.cs):
   - 2D implementation of 1€ filter algorithm
   - Parameters: minCutoff (FcMin), beta, dCutoff (DCutoff)
   - Dynamically adjusts cutoff frequency based on pen velocity
   - Can clamp cutoff to range for corner/sticky modes

3. **InkMode Integration**:
   - `InkMode.ActiveStroke.cs`: Manages active stroke state with smoother instance
   - `InkMode.Flush.cs`: Finalizes strokes, applies to InkCanvas
   - `InkMode.cs`: Coordinates pointer events with smoothing pipeline

### Service Layer

Services are stateful business logic components initialized in `MainWindow.Architecture.cs`:

- **PageService**: Multi-page management, current page tracking, page preview rendering
- **StrokeService**: Pen thickness management with zoom-consistency option
- **ZoomPanService**: Handles mouse wheel zoom, touch gestures, space+drag panning
- **AutoExpandService**: Automatically expands canvas when drawing near edges
- **TouchGestureService**: Touch gesture recognition (deprecated, functionality moved to ZoomPanService)
- **SettingsService**: JSON-based settings persistence to local file

### MainWindow Partial Class Architecture

`MainWindow` is split into multiple partial classes by responsibility:

- `MainWindow.Architecture.cs`: Core initialization, input pipeline setup, mode wiring
- `MainWindow.InputPipeline.cs`: Raw input event handlers, InputEventArgs builders
- `MainWindow.Pages.cs`: Page navigation UI handlers
- `MainWindow.Attachments.*.cs`: Attachment import, selection, external opening, bitmap loading
- `MainWindow.ToolUi.cs`: Toolbar button handlers (mode switching, pen settings)
- `MainWindow.UI.cs`: Window chrome, UI state management
- `MainWindow.Popups.cs`: Popup/dialog management
- `MainWindow.SettingsSync.cs`: Settings window integration
- `MainWindow.SystemDock.cs`: System tray/dock integration
- `MainWindow.VideoPresenter.cs`: External video presenter integration

### Undo/Redo System

Each `BoardPage` has its own `StrokeUndoHistory`:

- Observes `MyCanvas.Strokes.StrokesChanged` events
- Groups changes into transactions via `Begin()`/`End()`
- Supports `Cancel()` for discarding in-progress transactions
- Undo/Redo bound to `ApplicationCommands.Undo`/`Redo`
- Transaction lifecycle tied to mode pointer Down/Up cycle

### Performance Optimizations

1. **RenderTransform over LayoutTransform**: Zoom/pan uses RenderTransform to avoid layout recalculations
2. **Viewport BitmapCache**: Temporarily enabled during zoom/pan/gestures, disabled after interaction
3. **Bitmap Scaling Mode**: Switched to LowQuality during interactions, HighQuality when idle
4. **Gesture Suppression**: Blocks input and cancels strokes during multi-touch gestures to prevent artifacts
5. **Deferred Cache Disable**: Uses DispatcherTimer to delay cache cleanup after interaction ends

## Code Patterns

### Adding New Interaction Modes

1. Implement `IInteractionMode` or extend `InteractionModeBase`
2. Override pointer event handlers: `OnPointerDown/Move/Up/Hover`
3. Implement `SwitchOn()`/`SwitchOff()` for activation/deactivation logic
4. Register mode in `MainWindow.Architecture.InitializeArchitecture()`
5. Wire to toolbar button in `MainWindow.ToolUi.cs`

### Adding Input Filters

1. Implement `IInputFilter` or extend `InputFilterBase`
2. Set `Priority` (higher = runs first)
3. Implement `Handle(InputStage, InputEventArgs, ModeController)` → return true to block propagation
4. Register via `_inputManager.RegisterFilter(filter)` in InitializeArchitecture

### Working with Services

Services are injected as fields in MainWindow and initialized in `InitializeArchitecture()`:
- Services should expose events for state changes
- MainWindow subscribes to service events and updates UI accordingly
- Services should not directly manipulate UI elements (pass callbacks if needed)

## Testing Guidelines

- Use `[WpfFact]` attribute (from Xunit.StaFact) for tests requiring WPF STA thread
- Use `[Theory]` with `[InlineData]` for parameterized tests
- Core algorithms should have comprehensive unit tests with edge cases
- Place test helpers in `WindBoard.Tests/TestHelpers/`
- Test file structure mirrors source: `Core/Ink/RealtimeInkSmoother.cs` → `WindBoard.Tests/Ink/RealtimeInkSmootherTests.cs`

## Important Conventions

### Naming
- Classes and methods: PascalCase
- Variables and fields: camelCase
- Private fields: _camelCase with underscore prefix

### Code Reuse
Prioritize reusing existing code, components, and NuGet packages. Avoid duplicating functionality that already exists in the codebase.

### Partial Classes
When modifying MainWindow, identify the correct partial class file for the change based on the responsibility areas listed above. Add new partial classes if a new responsibility area emerges.

### Input Handling
- Never bypass the input pipeline (always use InputManager.Dispatch)
- Use AddHandler with handledEventsToo=true to receive events even when InkCanvas marks them handled
- Filter for real mouse events with `e.StylusDevice == null` to avoid duplicate processing
- Always call `BeginUndoTransactionForCurrentMode()` on Down and `EndUndoTransactionForCurrentMode()` on Up

### Gesture Suppression
When implementing multi-touch gestures:
- Call `BeginGestureSuppression()` to block input and cancel active strokes
- Call `EndGestureSuppression()` to restore previous mode
- Set `_strokeSuppressionActive` to prevent strokes during gestures
- Hook `SuppressGestureStroke` to remove strokes created during gesture window

## Common Pitfalls

1. **Don't use LayoutTransform for zoom/pan** - causes expensive layout recalculations
2. **Don't cache CanvasHost with BitmapCache** - it's 8000x8000px and will consume excessive memory
3. **Don't modify InkCanvas.EditingMode during active strokes** - can cause stroke corruption
4. **Don't forget to handle RealTimeStylus input** - check `_inputSourceSelector?.ShouldHandleWpfStylus`
5. **Don't skip epsilon filtering in smoothing output** - causes micro-jitter and performance issues
