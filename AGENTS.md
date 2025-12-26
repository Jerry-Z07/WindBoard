# Repository Guidelines

## Project Structure & Module Organization
- `WindBoard.sln` anchors the solution; `WindBoard.csproj` defines the WPF app that hosts the shared InkCanvas logic.
- `MainWindow/` holds the partial classes around UI, touch, zoom/pan, eraser, and page behaviors; `Views/` and `Models/` keep supporting screens and data.
- `Resources/`, `Styles/`, and `Services/` carry themed assets, MaterialDesign themes, and helper services such as configuration or file handling.
- `App.xaml(.cs)` wires up the `MaterialDesignThemes` M3 palette and sets the `InkCanvas` defaults (white strokes, thickness 3, custom cursor handling).
- `bin/` and `obj/` can be ignored when making changes; rely on `dotnet clean` or rebuilds when binaries need refreshing.

## Build, Test, and Development Commands
- `dotnet build WindBoard.sln` compiles every project, applies XAML compilation, and ensures the shared libraries are available for runtime.
- `dotnet run --project WindBoard.csproj` launches the main window (maximized by default) so you can verify ink, zoom, and gesture behaviors quickly.
- `dotnet clean WindBoard.sln` wipes generated artifacts before troubleshooting build issues or altering the canvas expansion logic.
- `dotnet test` has no targets yet, but run it once test projects exist; it will exercise any MSTest/NUnit/xUnit suites you add later.

## Coding Style & Naming Conventions
- C# uses 4-space indentation, PascalCase for types/events, camelCase for locals/parameters, and descriptive suffixes (`OnPointerMoved`, `HandleTOUCH`). Keep partial class files focused on one concern (touch vs. zoom vs. eraser).
- XAML tags rely on `Tag` to pass colors (`#FFFFFFFF`) or thickness strings (`"3"`, `"6"`, `"9"`) to shared handlers; keep those strings consistent and documented next to the controls.
- Keep InkCanvas configuration centralized (stroke color/width scaling with zoom, touch settings marked `Handled = true`) to avoid drift when refactoring gestures or auto-expansion logic.
- Prefer MaterialDesignThemes controls/styling and avoid inline brushes unless they match the nine preset colors defined in `MainWindow.xaml`.

## Testing Guidelines
- There are no dedicated test projects yet; add a test project under the solution root if you need coverage.
- Name future tests to follow `Feature_Scenario_ExpectedBehavior` or similar for clarity, and keep them colocated with the code under test.
- Once tests exist, use `dotnet test WindBoard.sln` or target the specific test project, and verify UI behavior manually in the running app.

## Commit & Pull Request Guidelines
- Commit messages follow `type(scope): 描述` (e.g., `feat(窗口): 优化缩放` or `refactor(字体管理): 重构字体加载`), matching the existing Chinese descriptions in the history.
- Pull requests should explain what changed, list how you verified the change (build/test steps), and include screenshots when adjusting UI or InkCanvas behavior. Link Jira/issue IDs if available.
- Mention any configuration updates (themes, resources, services) so reviewers know if rebinding or resource dictionaries need refreshing.

## Security & Configuration Tips
- Keep secrets/configuration out of source; load them through the `Services` layer. Hardcode only colors or cursor sizes that match the UI theme.
- MaterialDesignThemes settings live in `App.xaml`; adjust the palette centrally to avoid inconsistent brush references across views.
