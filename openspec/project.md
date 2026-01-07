# Project Context

## Purpose
WindBoard (轻风白板) is an open-source, lightweight electronic whiteboard app for Windows. It focuses on fast, smooth inking for classroom-style usage and supports pen/touch/mouse input, multiple pages, an “infinite” canvas via zoom/pan, attachments (image/video/text/link), undo/redo, and import/export (PNG/JPG/PDF/WBI).

## Tech Stack
- **Language/Runtime**: C# on .NET 10 (target `net10.0-windows10.0.26100.0`)
- **UI**: WPF + WPF Ink (`StrokeCollection` / ISF)
- **UI Theme**: `MaterialDesignThemes` / `MaterialDesignColors`
- **Serialization**: `Newtonsoft.Json` (settings + WBI metadata)
- **Export**: `PdfSharpCore` (PDF), image export helpers (`System.Drawing.Common`)
- **Markdown Rendering**: `Markdig.Wpf` (WPF markdown viewer)
- **Versioning**: Nerdbank.GitVersioning (`Directory.Build.props`, `version.json`)
- **Testing**: xUnit + `Xunit.StaFact`, optional coverage via `coverlet.collector`
- **Release/Packaging**: GitHub Actions (`.github/workflows/release.yml`) builds ZIP variants + Inno Setup installer (`installer/`)

Common commands (repo root):

```bash
dotnet restore
dotnet build WindBoard.sln
dotnet run --project WindBoard.csproj
dotnet test WindBoard.sln
```

## Project Conventions

### Code Style
- Indentation: 4 spaces; keep `using` directives organized.
- Nullable reference types: enabled; prefer fixing warnings rather than suppressing.
- Naming:
  - Types/methods/properties: `PascalCase`
  - Locals/parameters: `camelCase`
  - Private fields: `_camelCase` when needed
  - XAML element names: `PascalCase`
- File/class layout:
  - Prefer one public type per file (except partial classes).
  - File name matches the type name.
- UI boundaries:
  - Keep XAML code-behind thin; push heavy logic to `Services/` or `Core/`.
  - New `MainWindow` logic should go into the appropriate partial under `MainWindow/` (avoid growing `Views/MainWindow.xaml.cs`).

### Architecture Patterns
- High-level structure: “input pipeline + mode system + service layer”.
  - Input: `MainWindow/MainWindow.InputPipeline.cs` captures WPF input and feeds `Core/Input/InputManager` with `InputEventArgs`.
  - Modes: `Core/Modes/*` implements interaction modes (`InkMode`, `EraserMode`, `SelectMode`) via `ModeController`.
  - Services: `Services/*` contains app/domain services (pages, strokes, undo history, zoom/pan, settings, import/export).
  - Models: `Models/*` is data-only; Views are in `Views/*`.
- UI thread: WPF UI updates must run on the Dispatcher; do expensive work off-thread and marshal UI updates back.
- Performance constraints:
  - Zoom/pan uses “camera-style” `RenderTransform` (avoid `LayoutTransform`).
  - Do not enable `BitmapCache` on the full canvas host; cache only the viewport where needed.

### Testing Strategy
- Test framework: xUnit; use `Xunit.StaFact` for STA/WPF-dependent tests.
- Test location: `WindBoard.Tests/` mirrors domain folders (`Ink/`, `Services/`, `Resources/`, ...).
- Use `[StaFact]` when touching WPF types (`InkCanvas`, `StrokeCollection`, etc.).
- Naming: `ClassName_MethodUnderTest_ExpectedOutcome`.
- Run: `dotnet test WindBoard.sln` (coverage optional: `dotnet test WindBoard.sln -p:CollectCoverage=true`).

### Git Workflow
- Default branch: `main`.
- Commits generally follow Conventional Commits (`feat:`, `fix:`, `refactor:`, `docs:`, `build:`, `chore:`) with optional scope.
- Before PR/tag: run `dotnet build WindBoard.sln` and `dotnet test WindBoard.sln`.
- Releases: push tag `vX.Y.Z` to trigger `.github/workflows/release.yml`.
  - Optional release notes: `docs/release-notes/<tag>.zh-CN.md` and `docs/release-notes/<tag>.en-US.md`.

## Domain Context
- **Pages**: `Models/BoardPage` stores strokes, attachments, canvas size, and view state (zoom/pan).
- **Strokes**: WPF Ink; persisted as ISF via `StrokeCollection.Save/Load`.
- **Attachments**: `Models/BoardAttachment` with types `Image`/`Video`/`Text`/`Link`, positioned in canvas coordinates with z-order and “pinned top” rendering.
- **Modes**: Writing/eraser/selection modes live in `Core/Modes/`.
- **Zoom/Pan**: `Services/ZoomPanService` supports wheel zoom, right-drag pan, and two-finger touch gestures.
- **Localization**: string resources in `Resources/Strings.*.xaml` and `Services/Localization/LocalizationService.cs`.
- **WBI**: `.wbi` is WindBoard’s restoreable format: a ZIP containing `manifest.json`, per-page JSON, optional per-page ISF, and optional embedded image assets (videos are path-only). See `docs/dev/wbi-format.md`.

## Important Constraints
- Windows-only WPF app; many behaviors require an STA thread.
- Avoid UI jank: do expensive IO/decoding off-thread; marshal UI updates via `Dispatcher.Invoke/InvokeAsync`.
- User settings persist to `%APPDATA%\\WindBoard\\settings.json`; do not commit user-specific config.
- WBI compatibility: importer currently supports up to `1.0`; changes should be versioned and kept backward compatible where possible.

## External Dependencies
- Runtime: .NET 10 Windows Desktop Runtime (for framework-dependent builds).
- Optional local integrations:
  - Video presenter integration requires a locally installed presenter app; path/args are user-configured (`AppSettings.VideoPresenter*`).
  - Opening video/link attachments uses the OS shell/default apps (WindBoard itself does not require network services).
