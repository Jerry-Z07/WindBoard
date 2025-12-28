# Repository Guidelines

## Project Structure & Module Organization
WindBoard is a Windows-only WPF whiteboard app targeting .NET 10 with MaterialDesignThemes (Material Design 3). Key folders: `Core/` (input pipeline, interaction modes, ink smoothing and filters), `Services/` (pages, strokes, zoom/pan, settings, preview rendering), `MainWindow/` (UI, pages, input pipeline split by concern), `Views/` (XAML views/controls, including `MainWindow.xaml`), `Styles/` and `Resources/` (XAML styles, fonts), and `Models/` (lightweight data models). Build artifacts live in `bin/` and `obj/`; settings are written to `%APPDATA%\WindBoard\settings.json` (do not commit).

## Build, Test, and Development Commands
- `dotnet restore` – restore NuGet packages.
- `dotnet build -c Debug` – build the solution for local development.
- `dotnet run --project WindBoard.csproj` – launch the WPF app.
- `dotnet build -c Release` – create release binaries.  
Run commands from the repo root. Visual Studio 2022+ or VS Code works; the project targets `net10.0-windows10.0.26100.0`.

## Coding Style & Naming Conventions
Use 4-space indentation in C#; keep XAML attributes readable and wrap long lines. Naming: `PascalCase` for types/methods/properties/events, `camelCase` for locals/fields/parameters. Nullability is enabled; prefer fixing warnings over suppressing them. Stick to the existing folder topology (Core/Services/MainWindow/Views). Follow Material Design 3 patterns already present in XAML. `.editorconfig` only downgrades CS8622 to suggestion—otherwise use default .NET formatting.

## Testing Guidelines
There is no automated test project yet. Manually verify the whiteboard with the relevant input device (mouse/pen/touch) and note display scaling (e.g., 100%/125%/150%). For UI or styling changes, include repro steps and screenshots or short clips. Try to cover drawing, erase, select, page navigation, zoom/pan, and attachments when they are affected.

## Commit & Pull Request Guidelines
Use conventional commits (`feat:`, `fix:`, `refactor:`, optionally scoped like `feat(UI): …`). Keep PRs focused and describe the motivation/linked issue. Include a brief summary of changes, manual test steps (per device), and screenshots for visual changes. Avoid unrelated refactors in the same PR.

## Security & Configuration Tips
User settings persist to `%APPDATA%\WindBoard\settings.json`; avoid committing local config or machine-specific paths. Respect bundled assets under `Resources/` and `Styles/`. If you add fonts or binaries, ensure licensing is compatible with Apache 2.0.
