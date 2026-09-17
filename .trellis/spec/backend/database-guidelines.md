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

**Current version**: 3 (`WbixWorkspaceSerializer.CurrentVersion`). Write side always emits the current version; read side gates `0 < Version <= CurrentVersion`. Older apps refuse to open newer files (by design, no downgrade writing / no per-version migration code).

**manifest.json** structure:
```csharp
record WbixManifest(
    string Format,          // "wbix"
    int Version,            // 3
    DateTimeOffset CreatedUtc,
    int CurrentIndex,
    IReadOnlyList<WbixManifestPage> Pages,
    IReadOnlyList<WbixResourceEntry>? Resources,
    Vector2? ViewportCameraWorld,
    float? ViewportZoom,
    Vector2? ViewportSizeDip);
```

**Semi-structured elements**: `WbixPageElement(Type, JsonElement)` - Type is "text"/"link"/"media"/"file", and Data uses `JsonElement` so the schema is not fixed too early.

### Ink items: Kind-discriminated snapshots (v3)

Page payload `strokes` entries are `InkItemSnapshot { Kind, Stroke?, Shape? }` (JSON key stays `strokes` for v2 compat):

- **v3 write shape**: `{ "kind": "stroke", "stroke": { points, colorRgba, baseSize, enablePressure } }` for polyline strokes and `{ "kind": "line"|"rect"|"ellipse"|"arrow", "shape": { start, end, colorRgba, width } }` for two-point shapes — single form only. Shape kinds are a v3 in-format kind extension (version stays 3; no v3 files were shipped before the extension).
- **v1/v2 read compat**: entries are flat (`points/colorRgba/...` directly on the item, no wrapper). `InkItemSnapshotJsonConverter` handles both forms; a missing or explicit-null `kind` normalizes to `"stroke"`.
- **Unknown kind**: log `Warn("WBIX", ...)` and skip that single item; never fail the whole load. Known shape kinds with a missing `shape` payload are corrupt data and fail fast (same as a known `stroke` kind with a missing payload).
- **Snapshot ↔ domain conversion**: all conversions go through `BoardInkItemCodec` (`ToItemList` / `ToStrokeItem` / `ToItem`), which owns Bounds recalculation and z-order preservation. Shape kind strings (`"line"/"rect"/"ellipse"/"arrow"`) ↔ `BoardShapeKind` mapping lives only in `BoardInkItemCodec` (single point). `WbiWorkspaceImporter` must pass the explicit stroke path (legacy WBI has no kind).

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

Determine the installation type through `AppInstallProbe` (`AppInstallKind`):

Probe priority (first match wins):
1. **Package identity** — Win32 `GetCurrentPackageFullName` (P/Invoke) returns `APPMODEL_ERROR_NO_PACKAGE (15700)` for unpackaged processes → `Msix`, `Evidence = "package-identity"`. Result is cached.
2. Registry `HKLM\SOFTWARE\WindBoard` (`InstallKind`/`InstallVariant`/`InstallDir`, written by the Inno installer).
3. `unins*.exe` in the product root.
4. Fallback → `Portable`.

> **Warning**: use the **Win32** `GetCurrentPackageFullName` for the packaged check, **not** `Windows.ApplicationModel.Package.Current`. `AppDataPaths.GetSnapshot()` calls `AppInstallProbe.ProbeNoLog()` extremely early (before `AppLog` is configured), where bringing up WinRT activation is risky; the Win32 call is cheap and side-effect free.

| Mode | Root directory |
|------|----------------|
| Installer | `%LocalAppData%\WindBoard` |
| Msix | `%LocalAppData%\WindBoard` (the OS redirects **newly created** files to the package-private location; reads fall back to the real directory — see `docs/dev/guides/msix-packaging.zh-CN.md`) |
| Portable | `{AppDir}\data` (falls back to LocalAppData when not writable) |

Available path properties: `RootDirectory`, `SettingsFilePath`, `LogsDirectory`, `CamouflageCacheDirectory`, `DownloadsDirectory`, plus `OwnDataDirectory` (= `RootDirectory`) and `LegacyInstallerDataDirectory` (real `%LocalAppData%\WindBoard`, used **only** as a read source for the legacy-installer migration; computed from the `LOCALAPPDATA` environment variable, falling back to `GetFolderPath`).

> **Important**: MSIX must **not** declare `unvirtualizedResources` / `desktop6:*WriteVirtualization` in this project. Cross-form data sharing is by **exported files** (settings JSON, `.wbix`), not by sharing a runtime data directory — see `prd.md` D2 and `docs/dev/guides/msix-packaging.zh-CN.md`.

### AppRuntimeLayout

Resolves the product root directory, runtime directory, portable data directory, and Launcher/CrashReporter paths. It is compatible with the `shared/` subdirectory layout.

---

## Installation Form Contract (`AppInstallKind`)

`AppInstallKind` is a **cross-layer contract**: `Unknown | Installer | Portable | Msix`. Runtime behavior branches on it, so adding or changing a form has a fixed checklist of consumption points.

### Checklist when adding/changing an installation form

- [ ] `Updates/AppInstallProbe.cs` — probe + `ComputeProbeResult` (keep it injectable for tests)
- [ ] `Persistence/AppDataPaths.cs` — data root selection and the fallback chain
- [ ] `Updates/AppUpdateService.cs` / `UpdateAssetSelector` — whether an update channel exists for this form
- [ ] `Updates/DownloadSourceSpeedTestPolicy.cs` — speed-test policy input
- [ ] `Fonts/SegoeFluentIconsFontLoader.cs` — whether the system font is expected to be pre-installed
- [ ] `Errors/AppCrashReportStore.cs` — the `InstallKind` field written into crash reports
- [ ] Any `switch` over the enum (`AboutSettingsPage.Updates.cs` state/title mapping) — keep exhaustive, no silent default

### Migration marker contract (legacy installer → MSIX)

- The marker file lives in the app's **own** data directory (`migrated-from-installer.flag`); never in the registry.
- Marker is written when the user **confirms and the import succeeds**, and when the user **declines**. It is **not** written when the legacy JSON is corrupt or unreadable — otherwise a user who later fixes the file could never migrate.
- The legacy `settings.json` is only **read**; the app never writes back into the legacy directory.
- When the own data directory and the legacy directory resolve to the **same path** (possible under MSIX virtualization), do **not** treat "own settings.json exists" as "already an old user" — that would silently skip exactly the users the migration targets. Prompt instead.

---

## Common Mistakes

### ❌ DON'T
- Use hard-coded paths directly (for example `Path.Combine(localAppData, "WindBoard")`); use `AppDataPaths` instead
- Modify `AppSettings.Current` without calling `Update` (the change will not trigger events or saving)
- Let a single element failure stop the whole WBIX load flow
- Use `File.WriteAllText` directly when saving files (atomic write should be used instead)
- Rebuild domain ink items from snapshots with private `new Stroke { ... }` code — always go through `BoardInkItemCodec` (three duplicates existed before and were converged; keep it single-point)
- Add new ink item types by scattering `is XxxItem`-style checks across render/pick/serialize/UI/tool dispatch — extend the single registration points only (`BoardSceneRenderer.DrawInkItem` switch, `InkItemPickTest.IsInkItemHitByPoint` switch, `BoardInkItemCodec` kind mapping, `BoardInputController` tool registration)
- Rely on C# property initializers for snapshot compat: a JSON file may carry explicit `null` for optional fields (e.g. `kind`) — normalize on the read side

### ✅ DO
- Modify settings through `AppSettingsService.Update(Action<AppSettings>)`
- Use try-catch + `AppLog.Warn` for non-critical elements during WBIX loading
- Use the `IBoardWorkspaceSerializer` interface instead of a concrete implementation
- Retrieve path-related values through `AppDataPaths`
