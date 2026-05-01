# Data Persistence Guidelines

> Data persistence patterns and conventions for this project.

> **Note**: This project does not use a database. Data persistence is handled through file-based formats (WBIX, JSON) and application data directories.

---

## Overview

WindBoard's data persistence is divided into three layers:

1. **Workspace persistence**: WBIX format (a Zip package containing JSON plus assets), abstracted behind the `IBoardWorkspaceSerializer` interface
2. **Application settings persistence**: JSON files managed by `AppSettingsStore`
3. **Runtime data persistence**: crash reports, log files, and cache directories, with paths managed centrally by `AppDataPaths`

---

## File Format: WBIX

WBIX (`.wbix`) is the persistence format for a workspace. It is essentially a Zip package:

```
.wbix (Zip)
├── manifest.json          - format/version/page index/viewport info/resource list
├── pages/
│   ├── page-000.json      - strokes + elements
│   └── ...
└── assets/                - binary assets (cover images, embedded images, and so on)
```

**Current version**: 2 (`WbixWorkspaceSerializer.CurrentVersion`)

**manifest.json** structure:
```csharp
record WbixManifest(
    string Format,          // "wbix"
    int Version,            // 2
    DateTimeOffset CreatedUtc,
    int CurrentIndex,
    IReadOnlyList<WbixManifestPage> Pages,
    IReadOnlyList<WbixResourceEntry>? Resources,
    Vector2? ViewportCameraWorld,
    float? ViewportZoom,
    Vector2? ViewportSizeDip);
```

**Semi-structured elements**: `WbixPageElement(Type, JsonElement)` - Type is "text"/"link"/"media"/"file", and Data uses `JsonElement` so the schema is not fixed too early.

### Safety checks

- **Path validation**: `IsSafeZipPath` blocks `..` path traversal
- **Asset size limits**: 32 MB per item, 256 MB total
- **Fault-tolerant parsing**: a single element parse failure does not stop the whole flow (warn after catch and continue)

---

## Serialization Patterns

### Snapshot-runtime conversion

- **Runtime state -> snapshot**: `BoardWorkspaceSnapshotConverter.CreateSnapshot(workspace, viewportCamera, zoom, size)`
- **Snapshot -> runtime state**: `BoardWorkspaceSnapshotApplier.CreatePages(snapshot)` - import fills the Document directly and does not pollute the Undo/Redo stack

### Serialization interface

```csharp
internal interface IBoardWorkspaceSerializer
{
    Task SaveAsync(BoardWorkspaceSnapshot snapshot, Stream output, CancellationToken cancellationToken = default);
    Task<BoardWorkspaceSnapshot> LoadAsync(Stream input, CancellationToken cancellationToken = default);
}
```

UI and file format are decoupled: the UI only cares about `BoardWorkspaceSnapshot` and does not care about the concrete serialization implementation.

### JSON options

- `System.Text.Json`
- `CamelCase` naming policy
- Comments and trailing commas are allowed (`JsonCommentHandling.Skip`, `JsonTrailingCommasHandling.Allow`)

---

## Application Settings Storage

### Storage format

- JSON file (`settings.json`), with path determined by `AppDataPaths.SettingsFilePath`
- Installed version: `%LocalAppData%\WindBoard\settings.json`
- Portable version: `{AppDir}\data\settings.json`

### Save strategy

- **Debounced saving**: 350 ms timer to avoid frequent disk writes on high-frequency updates
- **Atomic write**: replace through a temporary file (`.tmp` -> `Move overwrite`)
- **Normalization**: run `NormalizeInPlace` after load/update/save to fill nulls and correct invalid values
- **Read tolerance**: fall back to defaults when reading fails or JSON is corrupted so startup is not affected
- **Snapshot cloning**: `AppSettingsCloner.Clone` performs a deep copy to prevent external mutation of internal state

---

## Path Infrastructure

### AppDataPaths

Determine the installation type through `AppInstallProbe`:

| Mode | Root directory |
|------|----------------|
| Installer | `%LocalAppData%\WindBoard` |
| Portable | `{AppDir}\data` (falls back to LocalAppData when not writable) |

Available path properties: `RootDirectory`, `SettingsFilePath`, `LogsDirectory`, `CamouflageCacheDirectory`, `DownloadsDirectory`

### AppRuntimeLayout

Resolves the product root directory, runtime directory, portable data directory, and Launcher/CrashReporter paths. It is compatible with the `shared/` subdirectory layout.

---

## Common Mistakes

### ❌ DON'T
- Use hard-coded paths directly (for example `Path.Combine(localAppData, "WindBoard")`); use `AppDataPaths` instead
- Modify `AppSettings.Current` without calling `Update` (the change will not trigger events or saving)
- Let a single element failure stop the whole WBIX load flow
- Use `File.WriteAllText` directly when saving files (atomic write should be used instead)

### ✅ DO
- Modify settings through `AppSettingsService.Update(Action<AppSettings>)`
- Use try-catch + `AppLog.Warn` for non-critical elements during WBIX loading
- Use the `IBoardWorkspaceSerializer` interface instead of a concrete implementation
- Retrieve path-related values through `AppDataPaths`
