# Directory Structure

> How backend code is organized in this project.

---

## Overview

This is a WinUI 3 C# desktop application with a layered architecture. The codebase is organized by functional areas and layers, following domain-driven design principles.

---

## Directory Layout

```
WindBoard/
├── Board/                      # Domain model (pure C#, no UI dependencies)
│   ├── Commands/               # Command pattern implementations (IBoardCommand)
│   ├── Editing/                # Workspace, session, viewport logic
│   ├── Elements/               # Board elements (text, media, link, file)
│   ├── Persistence/            # Workspace serialization (WBIX format)
│   └── Viewport/               # Viewport math (zoom, pan)
├── Rendering/                  # DirectX rendering layer (Vortice)
│   ├── Board/                  # Board scene rendering
│   └── SwapChain/              # Swap chain management
├── Interaction/                # Input handling
│   └── BoardInputController/   # Pointer → domain operations bridge
├── Controls/                   # WinUI UserControl components
│   ├── BoardCanvasControl.xaml(.cs)
│   └── PageThumbnailControl.xaml(.cs)
├── Features/                   # Feature modules (see "Feature Module Convention")
│   ├── Camouflage/
│   │   ├── CamouflageFlow.cs              # Coordinator/orchestrator
│   │   ├── Models/                        # Data models, snapshots
│   │   ├── Services/                      # Business logic
│   │   └── UI/                            # XAML pages + code-behind
│   ├── Dock/
│   ├── Export/
│   ├── Import/
│   ├── ScreenAnnotation/
│   └── Shortcuts/
├── Services/                    # Application-level infrastructure
│   ├── Settings/               # AppSettingsService, AppSettingsStore
│   ├── Persistence/            # IBoardPersistenceService
│   ├── Reminders/              # AppReminderService, reminder channels
│   ├── Errors/                 # AppErrorService
│   └── Updates/                # Update checking
├── Logging/                     # AppLog, FileLogSink
├── Localization/               # L10n extension, localization support
└── UI/MainWindow/             # MainWindow partial splits by feature

WindBoard.CrashReporter/        # WinForms crash reporter (standalone)
WindBoard.Launcher/             # Native AOT launcher (standalone)
WindBoard.Tests/                # xUnit tests
```

---

## Module Organization

### Domain Layer (Board/)
**Purpose**: Core business logic, pure C# with no UI dependencies
- `BoardDocument`: Document model (strokes + elements)
- `BoardSession`: Undo/Redo stack management
- `BoardWorkspace`: Multi-page workspace
- `BoardViewport`: Zoom/pan math
- **No UI references allowed in this layer**

### Rendering Layer (Rendering/)
**Purpose**: DirectX rendering using Vortice (D3D11/D2D1)
- `BoardSceneRenderer`: Scene drawing
- `DxSwapChainPanelRenderer`: Swap chain management
- Dirty rectangle optimization

### Interaction Layer (Interaction/)
**Purpose**: Bridge WinUI pointer events to domain operations
- `BoardInputController`: Partial class split by pointer/operation/manipulation
- Converts WinUI events to domain commands

### Controls Layer (Controls/)
**Purpose**: WinUI UserControl components
- `BoardCanvasControl`: Core canvas control (connects Renderer, Session, Viewport, InputController)
- `PageThumbnailControl`: Page thumbnail rendering

### Feature Modules (Features/)
**Purpose**: Organize features by domain, following convention:
```
FeatureName/
├── FeatureNameFlow.cs         # Coordinator/orchestrator
├── Models/                    # Data models, snapshots
├── Services/                  # Business logic
└── UI/                        # XAML pages + code-behind
```

### Application Services (Services/)
**Purpose**: Singleton services for cross-cutting concerns
- `AppSettingsService.Instance`: Settings management
- `AppErrorService.Instance`: Error handling
- `AppReminderService.Instance`: Reminder system
- Event-driven via `Action` and `EventHandler`

---

## Naming Conventions

### Directories and Files
- **Directory names**: `PascalCase` (e.g., `Rendering/`, `Interaction/`)
- **Feature modules**: `PascalCase` (e.g., `Camouflage/`, `ScreenAnnotation/`)
- **Partial class files**: `<ClassName>.<Feature>.cs` (e.g., `MainWindow.Pages.cs`, `BoardCanvasControl.Rendering.cs`)
- **Test files**: `<Target>Tests.cs` (e.g., `AddStrokeCommandTests.cs`)

### Code Elements
- **Types and methods**: `PascalCase`
- **Private fields**: `_camelCase` (with leading underscore)
- **Constants and static fields**: `PascalCase`
- **Interfaces**: `IPascalCase` prefix (e.g., `IBoardCommand`, `IBoardPersistenceService`)
- **Namespaces**: Match directory structure

---

## Examples

### Well-organized Feature Module
- `WindBoard/Features/Camouflage/CamouflageFlow.cs`: Coordinates window title/icon updates
- `WindBoard/Features/Camouflage/Services/CamouflageService.cs`: Business logic
- `WindBoard/Features/Camouflage/UI/CamouflageSettingsPage.xaml.cs`: Settings UI

### Domain Model Example
- `WindBoard/Board/BoardDocument.cs`: Document model (strokes + elements)
- `WindBoard/Board/Commands/AddStrokeCommand.cs`: Command pattern implementation

### Service Example
- `WindBoard/Settings/AppSettingsService.cs`: Singleton settings service
- `WindBoard/Errors/AppErrorService.cs`: Singleton error handling service

---

## Anti-Patterns

### ❌ DON'T
- Put UI dependencies in `Board/` domain layer
- Create features without the `*Flow.cs` coordinator
- Place business logic directly in UI code-behind (use `Services/` instead)
- Reference WinUI namespaces from domain layer
- Mix concerns: keep rendering separate from domain logic

### ✅ DO
- Keep `Board/` layer pure C# (no UI references)
- Use `*Flow.cs` as the feature coordinator
- Put business logic in `Services/` subdirectory
- Use singleton pattern for app-level services
- Split large files into partial classes by feature
