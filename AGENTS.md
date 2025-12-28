# Repository Guidelines

## Project Structure & Module Organization
- `Core/`: input pipeline, ink smoothing (`Ink/`), stylus adapters (`Input/`), and mode logic (`Modes/`).
- `Services/`: page, stroke, zoom/pan, settings persistence, and auxiliary behaviors (camouflage, auto-expand).
- `MainWindow/`: partial classes split by concern (architecture, attachments, input pipeline, pages, UI glue).
- `Models/`: board page, attachments, and import request types.
- `Views/`: XAML views and controls (dialogs, page navigator, windows) plus backing code-behind.
- `Styles/` and `Resources/`: shared styles and fonts.
- `WindBoard.Tests/`: xUnit test suites organized by domain (`Ink/`, `Services/`); keep new tests colocated.
- `docs/`: user-facing docs; prefer adding architecture notes here when relevant.

## Build, Test, and Development Commands
- `dotnet restore` — restore NuGet packages.
- `dotnet build WindBoard.sln` — compile app and tests (target `net10.0-windows10.0.26100.0` with WPF).
- `dotnet run --project WindBoard.csproj` — launch the WPF app.
- `dotnet test WindBoard.sln` — run all xUnit tests; add `-p:CollectCoverage=true` to emit coverage via coverlet.
- `dotnet format` — keep C# formatting consistent before sending a PR.

## Coding Style & Naming Conventions
- C# with nullable enabled; prefer explicit types and guard clauses for null-sensitive code paths.
- Indent with 4 spaces; keep using statements sorted and scoped minimally.
- PascalCase for types/methods/properties; camelCase for locals/parameters; `_camelCase` for private fields when needed.
- Favor small, single-responsibility methods; continue splitting large code-behind into partial classes under `MainWindow/`.
- When adding styles or resources, place shared entries in `Styles/` or `Resources/Fonts/` and reference via XAML resource keys.

## Testing Guidelines
- Framework: xUnit with `StaFact` for WPF-affecting tests; reuse the existing `ClassName_MethodUnderTest_ExpectedOutcome` naming pattern.
- Keep test fixtures under the matching domain folder (e.g., `WindBoard.Tests/Ink` for ink algorithms).
- For input/ink behaviors, construct `InkCanvas`, `StrokeCollection`, and `StylusPointCollection` as shown in current tests; assert both state and side effects (e.g., page content versions, stroke collections).
- Aim to cover new services or modes with positive, boundary, and undo/redo scenarios; prefer deterministic data over randomness.

## Commit & Pull Request Guidelines
- Follow the existing Conventional-Commit style (`feat:`, `refactor:`, etc.); keep messages concise and scoped to one change.
- Ensure `dotnet build` and `dotnet test` pass before opening a PR; include relevant test additions.
- PR description should summarize behavior change, affected modules, and user-visible impact; link issues when available.
- Include screenshots or short clips for UI-affecting changes (dialogs, controls, or new gestures).
