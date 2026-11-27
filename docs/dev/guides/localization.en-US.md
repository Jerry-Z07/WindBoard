# Localization

WindBoard's user-visible text is uniformly extracted into resource dictionaries (`.resw` files) and compiled through WinUI 3 / MRT Core into a PRI resource index: `<TargetName>.pri` (for this project, `WindBoard.pri`) in the unpackaged form (portable / development run), and the package-root `resources.pri` in the packaged (MSIX / Store) form.

## Resource Location

- Resource files (split by language/feature):
  - `WindBoard/Strings/zh-CN/*.resw` (Default language: Simplified Chinese)
  - `WindBoard/Strings/en-US/*.resw` (English)
- C# entry point: `WindBoard/Localization/L10n.cs`
- XAML entry point: `WindBoard/Localization/LocExtension.cs`
- Build-time language/feature metadata generation: `WindBoard/Build/GenerateLocalizationMetadata.ps1`

- Two languages are available: `zh-CN` (Simplified Chinese, default) and `en-US` (English). New languages get a directory with the same structure (for example, `ja-JP`).
- At runtime, the first segment of the key prefix selects the feature resource file (for example, `Settings_*` -> `Settings.resw`). Text comes from the PRI resource index: the unpackaged form reads `WindBoard.pri` explicitly, while the packaged form relies on `ResourceManager` to auto-load the package-root `resources.pri`.
- Fallback strategy for missing language/key:
  - Missing language: Automatically falls back to the default `zh-CN`.
  - Missing key: Falls back to `fallback` (if provided) or the key itself, and logs via `WindBoard.Logging.AppLog` (each key is logged only once).
  - Partial translation: when a language has some resource files but the key is still missing, a "missing translation" message is logged and the lookup falls back to the default language, so the gap is easy to fill later.

## C# Usage

Use `L10n.Get / L10n.Format` for all text:

```csharp
string title = L10n.Get("Common_Settings");
string message = L10n.Format("Export_Completed_File_Fmt", filePath);
```

Constraint:
- The key parameter for `L10n.Get/Format` must be a **string literal**; dynamic key construction is not allowed (unit tests audit this).

## XAML Usage

Declare the namespace in the XAML root node:

```xml
xmlns:l10n="using:WindBoard.Localization"
```

Use it for every user-visible string:

```xml
Text="{l10n:Loc Key=Common_Close}"
Header="{l10n:Loc Key=Settings_Dock_ShowShortcutDocks_Header}"
```

## Language Switching

Switch the application display language in "Settings → General → Language":

- Entry page: `WindBoard/Settings/Pages/GeneralSettingsPage.xaml`
- Persistence: `settings.json` → `general.languagePreference`
- Current available values:
  - `system`: Follow system (default)
  - `<CultureName>`: Any language for which resources are provided (e.g., `zh-CN` / `en-US` / `ja-JP`)

Implementation details:
- `L10n` reads resources from `CultureInfo.CurrentUICulture`. At startup the app loads settings first and applies the language preference before creating any UI, so XAML MarkupExtension does not pick up the old language.
- Changing the language on the settings page at runtime writes the new value to settings and applies it immediately, but **text in already loaded UI typically does not refresh automatically**. By convention, the page prompts the user to restart the app for the change to fully take effect.

## Steps to Add a New Language

1. Copy `WindBoard/Strings/zh-CN/` to `WindBoard/Strings/en-US/` (or another target language directory, e.g., `ja-JP`, named according to the `BCP 47` standard).
2. Translate the resource values item by item (Keys remain unchanged; files are split by feature).
3. Run `dotnet test WindBoard.slnx` to confirm the localization key audit passes.

- The settings page enumerates the languages that have resources, using metadata generated at build time from `Strings/**/*.resw`;
- The dropdown display text uses `CultureInfo`'s `NativeName` (along with the `CultureName`), so each language needs no extra localization keys.

Verification steps:
1. Run `dotnet test WindBoard.slnx` to confirm the localization key audit passes.
2. Launch the application → open "Settings → General → Language" to confirm the new language appears and is selectable.
3. After selection, restart the application as prompted to confirm the UI language takes effect.

## Tests

- Unit test: `WindBoard.Tests/Localization/LocalizationKeyAuditTests.cs`
  - Scans `{l10n:Loc Key=...}` in `WindBoard/**/*.xaml`.
  - Scans `L10n.Get/Format("...")` in `WindBoard/**/*.cs`.
  - Asserts that all referenced keys exist in the default language resources (`Strings/zh-CN/*.resw`).
