# Directory Structure

> How frontend code is organized in this project.

---

## Overview

WindBoard is a WinUI 3 desktop app and does not use the MVVM pattern. UI code is organized by feature module, code-behind manipulates controls directly, and services are accessed through static singletons.

---

## Directory Layout

```
WindBoard/
├── App.xaml(.cs)                    # App entry point, global resources, global exception registration
├── MainWindow.xaml(.cs)             # Main window shell: tool switching, Dock buttons, Flyouts
├── UI/MainWindow/                   # MainWindow partial split
│   ├── MainWindow.Pages.cs          # Page management
│   ├── MainWindow.Export.cs         # Export entry point
│   ├── MainWindow.Import.cs         # Import entry point
│   ├── MainWindow.Dock.cs           # Dock entry point
│   ├── MainWindow.Shortcuts.cs      # Shortcut entry point
│   ├── MainWindow.Camouflage.cs     # Camouflage entry point
│   ├── MainWindow.ScreenAnnotation.cs  # Screen annotation entry point
│   ├── MainWindow.WindowMode.cs     # Window mode
│   ├── MainWindow.Reminders.cs      # Reminder service
│   ├── MainWindow.Updates.cs        # Auto update
│   ├── MainWindow.ClearCanvasSlide.cs  # Clear-canvas animation
│   └── PageListItem.cs             # Page list item data model
├── Controls/                        # Reusable WinUI UserControls
│   ├── BoardCanvasControl.xaml(.cs) # Core canvas control (main body)
│   ├── BoardCanvasControl.Rendering.cs     # Render loop (partial)
│   ├── BoardCanvasControl.EraserCursor.cs  # Eraser cursor (partial)
│   ├── BoardCanvasControl.SelectionHandles.cs  # Selection handles (partial)
│   ├── PageThumbnailControl.xaml(.cs)      # Page thumbnail
│   └── DialogHelpers.cs            # Dialog helper methods
├── Features/                        # Feature modules (each module follows the same structure)
│   ├── Camouflage/
│   │   ├── CamouflageFlow.cs       # Coordinator
│   │   ├── Models/                 # Data models
│   │   ├── Services/               # Business logic
│   │   └── UI/
│   │       ├── CamouflageSettingsPage.xaml(.cs)  # Page
│   │       └── ...
│   ├── Dock/
│   │   ├── DockFlow.cs
│   │   ├── Models/
│   │   ├── Services/
│   │   └── UI/
│   │       └── DockSettingsPage.xaml(.cs)  # Page
│   ├── Export/
│   │   ├── ExportFlow.cs
│   │   ├── Models/
│   │   ├── Services/
│   │   └── UI/
│   ├── Import/
│   │   ├── ImportFlow.cs
│   │   ├── Models/
│   │   ├── Services/
│   │   └── UI/                         # Optional: create only when an independent XAML page/dialog is needed
│   ├── ScreenAnnotation/
│   │   ├── ScreenAnnotationFlow.cs
│   │   ├── Services/
│   │   └── UI/
│   │       └── ScreenAnnotationWindow.xaml(.cs)  # Window
│   └── Shortcuts/
│       ├── ShortcutsFlow.cs
│       ├── Models/
│       ├── Services/
│       └── UI/
├── Localization/                    # L10n runtime entry + XAML markup extension
├── Strings/                         # Localization source files (`Strings/<culture>/<Feature>.resw`)
├── Settings/                        # AppSettingsService, AppSettingsStore, shared resources for settings pages
│   ├── SettingsPageResources.xaml   # Shared spacing/container styles for settings pages; merged in App.xaml and referenced directly by each page
├── Errors/                          # AppErrorService
├── Reminders/                       # AppReminderService
├── Updates/                         # AppUpdateService
├── Logging/                         # AppLog
└── Persistence/                     # AppDataPaths + AppRuntimeLayout
```

---

## Module Organization

### Localization conventions

- `Localization/L10n.cs`: runtime localization entry; continues to provide `L10n.Get/Format/GetSupportedCultureNames`
- `Localization/LocExtension.cs`: XAML-side entry for `{l10n:Loc Key=...}`
- `Strings/<culture>/<Feature>.resw`: localization source files; `<Feature>` must match the first segment of the key prefix (for example `Settings_*` -> `Settings.resw`)
- `Build/GenerateLocalizationMetadata.ps1`: scans `Strings/**/*.resw` during build and generates `obj/Generated/Localization/L10nResourceMetadata.g.cs`

Validation requirements:

- After changing `.resw`, run `dotnet build WindBoard.slnx -c Release`
- After changing `.resw` or localization keys, run `dotnet test WindBoard.slnx -c Release`

### Feature module convention

Each feature module follows the same structure:

```
FeatureName/
├── FeatureNameFlow.cs         # Coordinator/orchestrator (internal sealed)
├── Models/                    # Data models, snapshots
├── Services/                  # Business logic
└── UI/                        # XAML pages + code-behind
```

**UI type selection**:

| Scenario | Base class | Example |
|----------|------------|---------|
| Settings page | `Page` | `DockSettingsPage`, `CamouflageSettingsPage` |
| Modal dialog | `ContentDialog` | `ImportFlow.ConfirmReplaceCurrentPageRiskAsync` |
| Standalone window | `Window` | `ScreenAnnotationWindow` |

### MainWindow partial split

All partial files share `public sealed partial class MainWindow : Window`. Each partial file is the feature module's **bridge layer** - it only bridges references/state from MainWindow UI to the Feature Flow, while the core logic lives in `Features/*Flow.cs`.

### Control partial split

`BoardCanvasControl` is split by functional area:

- Main file `.xaml.cs`: field declarations, properties, initialization, event subscription/unsubscription, Dispose
- `.Rendering.cs`: render-loop methods
- `.EraserCursor.cs`: eraser cursor logic
- `.SelectionHandles.cs`: selection handle dragging

The main file owns all field and property declarations, and partial files contain methods only.

---

## Naming Conventions

### File naming

| Type | Convention | Example |
|------|------------|---------|
| XAML page | `{Feature}{Purpose}Page.xaml` | `DockSettingsPage.xaml` |
| XAML dialog | `{Feature}Dialog.xaml` | `(create as needed; the current import flow has no standalone XAML dialog)` |
| XAML window | `{Feature}Window.xaml` | `ScreenAnnotationWindow.xaml` |
| Partial class | `{ClassName}.{Feature}.cs` | `MainWindow.Pages.cs` |
| Flow coordinator | `{Feature}Flow.cs` | `ExportFlow.cs` |
| Service | `{Feature}Service.cs` | `CamouflageService.cs` |
| Host bridge | `{Feature}MainWindowHost.cs` | `DockMainWindowHost.cs` |

### Code elements

| Element | Convention | Example |
|---------|------------|---------|
| XAML namespace import | `xmlns:l10n="using:WindBoard.Localization"` | - |
| Event handler | `On{Element}{Event}` | `OnEnabledToggled`, `OnTitleTextChanged` |
| Sync-flag field | `_isSyncingFromSettings` | Prevents settings -> UI -> settings loops |

---

## Examples

### Well-organized Feature Module

```
Features/Camouflage/
├── CamouflageFlow.cs                    # Coordinates window title/icon updates
├── Models/
│   └── CamouflageSettingsSnapshot.cs    # Settings snapshot
├── Services/
│   └── CamouflageService.cs             # Singleton service
└── UI/
    └── CamouflageSettingsPage.xaml(.cs) # Settings page
```

### MainWindow Bridge Pattern

```csharp
// MainWindow.Dock.cs - bridge layer
private DockFlow? _dockFlow;

private void InitializeDock()
{
    var host = new DockMainWindowHost(this, ...);
    _dockFlow = new DockFlow(host, ...);
}
```

---

## Anti-Patterns

### ❌ DON'T
- Put business logic in MainWindow partial files (they should only bridge)
- Create a Feature without a Flow coordinator
- Put business logic in code-behind (use Services/ instead)
- Split the ViewModel into a separate file (the project standard embeds ViewModel logic in code-behind)
- Use MVVM binding patterns (the project does not use INotifyPropertyChanged binding)

### ✅ DO
- Use `*Flow.cs` as the feature orchestration entry point
- Keep MainWindow partials focused on bridging UI references to Flow
- Put business logic in the `Services/` subdirectory
- Split large controls into partial classes by functional area
