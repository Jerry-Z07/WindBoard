# Repository Guidelines

## Project Structure & Module Organization

WindBoard is a WPF whiteboard app targeting `net10.0-windows10.0.26100.0`.

- `Core/`: input pipeline, interaction modes, and ink algorithms (`Core/Input`, `Core/Modes`, `Core/Ink`).
- `Services/`: app/services layer (pages, strokes, undo history, zoom/pan, settings, import/export).
- `Models/`: data-only models (pages, attachments, export options, WBI manifest/data).
- `Views/`: XAML + code-behind for windows/dialogs/controls; keep UI logic thin.
- `MainWindow/`: `MainWindow` partial classes split by concern (input pipeline, export, attachments, settings sync).
- `Resources/`, `Styles/`: fonts and XAML resource dictionaries.
- `WindBoard.Tests/`: unit tests mirroring the domain folders.

## Build, Test, and Development Commands

Run from the repo root:

- `dotnet restore`: restore NuGet packages.
- `dotnet build WindBoard.sln`: build the app and tests.
- `dotnet run --project WindBoard.csproj`: launch the WPF app.
- `dotnet test WindBoard.sln`: run unit tests.
- `dotnet test WindBoard.sln -p:CollectCoverage=true`: optional coverage collection (Coverlet collector).

## Coding Style & Naming Conventions

- Indentation: 4 spaces; keep `using` directives organized.
- Nullable reference types are enabled; fix warnings rather than suppressing.
- Naming: types/methods/properties `PascalCase`; locals/parameters `camelCase`; private fields use `_camelCase` when needed.
- Prefer explicit types, guard clauses, and small single-responsibility methods.
- UI: keep heavy logic in `Services/` or `Core/` (not in XAML code-behind). Add new `MainWindow` logic to the appropriate partial file instead of growing `MainWindow.xaml.cs`.

## Testing Guidelines

- Framework: xUnit + `Xunit.StaFact`. Use `[StaFact]` for WPF/STA-dependent tests (`InkCanvas`, `StrokeCollection`, etc.).
- Place tests under `WindBoard.Tests/` and follow the existing folder layout.
- Naming pattern: `ClassName_MethodUnderTest_ExpectedOutcome`.

## Commit & Pull Request Guidelines

- Commits generally follow Conventional Commits: `feat:`, `fix:`, `refactor:`, `docs:`, `chore:` with optional scope (scopes may be English or Chinese), e.g. `fix(ZoomPanService): ...`.
- PRs should include: a short description, repro/verification steps, linked issues (if any), and screenshots/GIFs for UI changes.
- Before opening a PR, run `dotnet build WindBoard.sln` and `dotnet test WindBoard.sln`.

## Configuration & Security Tips

- User settings are persisted to `%APPDATA%\\WindBoard\\settings.json`; avoid committing user-specific config or generated output (`bin/`, `obj/`).
