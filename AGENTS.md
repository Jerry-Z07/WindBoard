# Repository Guidelines

## Project Overview
WindBoard is a Windows-only WPF whiteboard app built on `.NET 10` with MaterialDesignThemes (Material Design 3). Most changes are UI/interaction-heavy; please include repro steps and screenshots when you touch XAML or styling.

## Project Structure & Module Organization
- `Core/`: input pipeline, interaction modes, ink smoothing/filters.
- `Views/`: WPF views and controls (`*.xaml` + `*.xaml.cs`), including `MainWindow.xaml`.
- `MainWindow/`: main window logic split by concern (UI, pages, input pipeline).
- `Services/`: app services (pages, settings, preview rendering, strokes, zoom/pan).
- `Models/`: small data models.
- `Styles/` and `Resources/`: XAML styles and assets (e.g., `Resources/Fonts/*.ttf`).

## Build, Test, and Development Commands
Run from the repo root:
- `dotnet restore`: restore NuGet packages.
- `dotnet build -c Debug`: build the solution.
- `dotnet run --project WindBoard.csproj`: run the WPF app.
- `dotnet build -c Release`: produce release binaries (also works via Visual Studio 2022+).

## Coding Style & Naming Conventions
- Indentation: 4 spaces in C#; keep XAML attributes readable and wrap long lines.
- Naming: `PascalCase` for types/methods, `camelCase` for locals/fields; keep folders aligned with domain (`Core/*`, `Services/*`, `Views/*`).
- Nullability is enabled (`<Nullable>enable</Nullable>`); prefer fixing warnings over suppressing them.
- `.editorconfig` currently only tunes `CS8622` severity—use your IDE/VS formatting defaults for consistency.

## Testing Guidelines
There is no dedicated automated test project today. For changes:
- Add clear manual verification steps (device type if relevant: mouse/pen/touch).
- For UI changes, attach screenshots or short clips and note display scaling (100%/125%/150%).

## Commit & Pull Request Guidelines
- Commit messages follow “conventional commit” style: `feat:`, `fix:`, `refactor:`, optionally scoped like `feat(UI): ...`.
- PRs should include: summary, motivation/linked issue, manual test steps, and screenshots for visual changes. Keep PRs focused; avoid unrelated refactors.

## Security & Configuration Tips
- User settings are persisted to `%APPDATA%\\WindBoard\\settings.json`; avoid committing local settings or machine-specific paths.
