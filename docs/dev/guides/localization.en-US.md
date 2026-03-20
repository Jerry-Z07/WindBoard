# Localization

This document specifies how WindBoard's "user-visible text" is uniformly extracted into resource dictionaries (`.resx` files) to prepare for future multi-language support.

## Resource Location

- Resource files (split by "language/feature"):
  - `WindBoard/Localization/zh-CN/*.resx` (Default language: Simplified Chinese)
  - `WindBoard/Localization/en-US/*.resx` (English)
- C# entry point: `WindBoard/Localization/L10n.cs`
- XAML entry point: `WindBoard/Localization/LocExtension.cs`

Notes:
- Currently, `zh-CN` (Simplified Chinese, default) and `en-US` (English) are provided. When adding other languages later, create new language directories following the same structure (e.g., `ja-JP`).
- At runtime, the feature resource file is selected based on the first segment of the key prefix (e.g., `Settings_*` -> `Settings.resx`).
- Fallback strategy for missing language/key:
  - Missing language: Automatically falls back to the default `zh-CN`.
  - Missing key: Falls back to `fallback` (if provided) or the key itself, and logs via `WindBoard.Logging.AppLog` (each key is logged only once).
  - Partial translation missing: If a key is still missing in a language for which some resource files have been provided, a "missing translation" message will be logged and it will fall back to the default language, facilitating subsequent completion.

## C# Usage

Use `L10n.Get / L10n.Format` uniformly:

```csharp
string title = L10n.Get("Common_Settings");
string message = L10n.Format("Export_Completed_File_Fmt", filePath);
```

Constraint:
- The key parameter for `L10n.Get/Format` must be a **string literal**; dynamically constructed keys are not allowed (this is audited by unit tests).

## XAML Usage

Introduce the namespace in the XAML root node:

```xml
xmlns:l10n="using:WindBoard.Localization"
```

Then uniformly use the following for user-visible text:

```xml
Text="{l10n:Loc Key=Common_Close}"
Header="{l10n:Loc Key=Settings_Dock_ShowUndoRedo_Header}"
```

## Language Switching

Switch the application display language in "Settings → General → Language":

- Entry page: `WindBoard/Settings/Pages/GeneralSettingsPage.xaml`
- Persistence: `settings.json` → `general.languagePreference`
- Current available values:
  - `system`: Follow system (default)
  - `<CultureName>`: Any language for which resources are provided (e.g., `zh-CN` / `en-US` / `ja-JP`)

Implementation details:
- `L10n` reads resources based on `CultureInfo.CurrentUICulture`; at startup, settings are loaded first and the language preference is applied before creating any UI (to avoid XAML MarkupExtension using the old language).
- Switching the language in the settings page during runtime will immediately write to settings and attempt to apply it, but **text in already loaded UI typically does not refresh automatically**. Therefore, it is recommended to prompt the user to restart the application for changes to take full effect.

## Steps to Add a New Language

1. Copy `WindBoard/Localization/zh-CN/` to `WindBoard/Localization/en-US/` (or another target language directory, e.g., `ja-JP`, named according to the `BCP 47` standard).
2. Translate the resource values item by item (Keys remain unchanged; files are split by feature).
3. Run `dotnet test WindBoard.slnx` to confirm the localization key audit passes.

Notes:
- The settings page dynamically enumerates languages for which "resources are provided" from the localized resources embedded in the package;
- The dropdown display text uses `CultureInfo`'s `NativeName` (along with the `CultureName`), so no additional localization keys need to be added for each language.

Verification steps:
1. Run `dotnet test WindBoard.slnx` to confirm the localization key audit passes.
2. Launch the application → Open "Settings → General → Language" to confirm the new language appears and is selectable.
3. After selection, restart the application as prompted to confirm the UI language takes effect.

## Tests

- Unit Test: `WindBoard.Tests/Localization/LocalizationKeyAuditTests.cs`
  - Scans for keys in `{l10n:Loc Key=...}` within `WindBoard/**/*.xaml` files.
  - Scans for keys in `L10n.Get/Format("...")` within `WindBoard/**/*.cs` files.
  - Asserts that all referenced keys exist in the default language resources (`zh-CN/*.resx`).
