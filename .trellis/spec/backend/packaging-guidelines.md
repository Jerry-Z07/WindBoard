# Packaging & Distribution Guidelines (MSIX)

> Executable contracts for producing a Microsoft Store MSIX package **without** disturbing the default unpackaged build.
> Walkthrough / operator-facing version: `docs/dev/guides/msix-packaging.zh-CN.md`.

---

## Scenario: single-project MSIX packaging

### 1. Scope / Trigger

This spec applies whenever you touch:
- `WindBoard/Package.appxmanifest`
- `WindBoard/WindBoard.csproj` packaging properties or targets
- `.github/workflows/release.yml` packaging steps
- anything that depends on the installation form (`AppInstallKind`)

**Hard constraints from the approved plan** (do not silently relax):
- MSIX is distributed **only** via Microsoft Store. No sideloading, no self-signed/OV certificates in this repo.
- **No** `unvirtualizedResources` / `desktop6:FileSystemWriteVirtualization` / `desktop6:RegistryWriteVirtualization`.
- `latest.json` `assets` carries **portable zips only** — never `.msix`.
- `latest.json` `downloadUrl` must never point at the Store: the shipped client classifies assets by `fileName` suffix and its download flow is "HTTP download → run the local file", with no "open URL" fallback.
- MSIX artifacts are delivered through `actions/upload-artifact`, **not** as GitHub Release assets.
- `-p:WindBoardUnsignedTest=true` must **never** appear in CI or in any release build: it tags the manifest publisher with the unsigned-test OID, and a package carrying that OID is rejected by the Store. It exists only so a developer can install the package locally with `Add-AppxPackage -AllowUnsigned`.
- Store MSIX builds must pass `-p:AppxBundle=Never`: single-project MSIX cannot emit a multi-architecture bundle, and per-architecture bundles carry duplicate Neutral resource packages whose full names collide in one Store submission (batch rejection). See §4.

### 2. Signatures (MSBuild / CI surface)

| Item | Default (unpackaged) | MSIX |
|---|---|---|
| Switch | *(not passed)* | `-p:WindBoardPackage=Msix` |
| `WindowsPackageType` | `None` | **`MSIX`** (must be explicit) |
| `EnableMsixTooling` | `false` | `true` |
| `AppxPackage` | `false` | `true` |
| `EnableDefaultPriItems` | *(unset)* | `false` |
| `AppxManifest` item | *empty* | `Package.appxmanifest` |
| Version injection | n/a | `$(VersionPrefix)` → `Identity/@Version` (`2.9.0` → `2.9.0.0`) |
| Unsigned local test | *(never)* | `-p:WindBoardUnsignedTest=true` appends the `-AllowUnsigned` OID to `Identity/@Publisher` — **local verification only, must never ship** |

MSIX producing properties:
`-p:GenerateAppxPackageOnBuild=true`, `-p:AppxPackageDir=<abs>`, `-p:UapAppxPackageBuildMode=StoreUpload`, `-p:AppxBundle=Never`,
`-p:AppxPackageSigningEnabled=false`, `-p:WindowsAppSDKSelfContained=true`, `-p:SelfContained=true`, `-p:PublishDir=<abs>`.

`-p:AppxBundle=Never` is **required**: single-project MSIX (Windows App SDK) cannot emit a multi-architecture bundle, so each architecture must be a standalone package. Without it, each architecture becomes its own bundle that carries a duplicate Neutral (`..._Neutral_split.scale-*`) resource package, and Partner Center rejects the whole submission for duplicate package full names (see §4).

### 3. Contracts (pipeline ordering)

- The Appx packaging targets run at `AfterTargets="PrepareMsixPackage"`, i.e. **before** the project's own `AfterTargets="Build"` / `AfterTargets="Publish"` targets.
- The MSIX payload is derived **only** from `@(PackagingOutputs)` (`GetPackagingOutputs` → `_ComputeAppxPackagePayload`). Files merely present on disk are **not** packed.
- Therefore every extra payload file (e.g. `WindBoard.CrashReporter.*`) must be added to `@(PackagingOutputs)` with `<TargetPath>%(Filename)%(Extension)</TargetPath>`, and its publish must be forced to finish before payload computation.
- `Identity/@Version` cannot be overridden by an MSBuild property. `AppxManifestIdentityVersion` is an **output** of `WinAppSdkValidateAppxManifestItems` (`Microsoft.Windows.SDK.BuildTools.MSIX.Packaging.targets`), not an input. Version must be injected by rewriting the manifest into `$(IntermediateOutputPath)` and substituting the `AppxManifest` item.
- Anything that changes **only the manifest content** (version, unsigned-test OID) keeps the same artifact file names, but the upload staging under `$(OutDir)Upload\` is incrementally judged by file existence/timestamps → a stale staged msix can be re-bundled silently into `.msixbundle` / `.msixupload`. `WindBoard_InjectMsixManifestIdentity` therefore deletes `$(OutDir)Upload\` before the packaging pipeline runs (no-op on a clean tree). Do **not** use `AppxPackageUploadDir` for that cleanup: at evaluation time it resolves with the default `AppxPackageDirName` (`AppPackages`), not the real runtime directory.

### 4. Validation & Error Matrix

| Symptom | Cause | Fix |
|---|---|---|
| `error MSB4018` / `WinAppSdkGenerateAppxManifest` + `FileNotFoundException: System.Security.Permissions` | `dotnet msbuild` host runs on `Microsoft.NETCore.App`; the task needs `Microsoft.WindowsDesktop.App` | Build with VS `MSBuild.exe` (CI: `microsoft/setup-msbuild`) — **never** `dotnet msbuild` |
| `Improper project configuration` | `WindowsPackageType=None` together with `GenerateAppxPackageOnBuild=true` | Set `WindowsPackageType=MSIX` explicitly |
| `NETSDK1022` duplicate `PRIResource` | packaging targets implicitly glob `**/*.resw`, project declares them explicitly | `EnableDefaultPriItems=false` |
| `NullReferenceException` in `WinAppSdkGenerateAppxManifest.UpdateLanguages()` (`APPX0002`) | manifest lacks a language resource list | Add `<Resources><Resource Language="x-generate" /></Resources>` |
| `0x800B0100` on `Add-AppxPackage` | package is unsigned | Expected locally: signed packages are required by default. For local verification either self-sign + import into `Cert:\LocalMachine\TrustedPeople` (needs admin), or rebuild with `-p:WindBoardPackage=Msix -p:WindBoardUnsignedTest=true` and install with `Add-AppxPackage -AllowUnsigned` (Win11, needs admin, **never shippable**) |
| `PRI263` warning | duplicate satellite `*.resources.dll` in payload | Exclude `**/*.resources.dll` from the injected payload |
| Every user-visible string renders as its own localization key in the MSIX/Store build while portable/unpackaged builds are fine | Packaged builds emit a **different PRI file name**: `resources.pri` at the package root when `AppxPackage=true`, versus `<TargetName>.pri` when unpackaged (`MrtCore.PriGen.targets`). Code that hard-codes `new ResourceManager("<TargetName>.pri")` cannot resolve resources under package identity, and `L10n`'s fallback silently returns each key | Resolve the PRI by install shape: packaged → default `ResourceManager()` (loads the package-root `resources.pri`; packaged apps must not rename PRI files), unpackaged → explicit `<TargetName>.pri`. In this repo: `Localization/L10n.ResolvePriFileName` |
| Partner Center rejects the whole batch citing `makepri.exe` version | Store validates the tool version recorded in `build:Metadata` | Check `Microsoft.Windows.SDK.BuildTools` version compatibility before suspecting app code |
| Partner Center rejects a multi-arch submission: `..._Neutral_split.scale-100` / `..._split.scale-400` "used by two packages with different content" | Each single-arch **bundle** carries its own copy of the Neutral (architecture-independent) scale resource packages; single-project MSIX cannot merge architectures into one bundle | Build with `-p:AppxBundle=Never` so each architecture yields a single `.msixupload` containing one `.msix` (resources embedded, no split packages); the three packages then have unique, architecture-specific full names (`..._x64_~` / `..._x86_~` / `..._arm64_~`) |
| Explorer shows "path not found" (or the default program never opens) after a debug-page open action **while the in-app InfoBar reports success**, and the log has no failure line | Under package identity the friendly path is not the real one for external processes: `Launcher.Launch*Async` hands the pre-redirection `%LOCALAPPDATA%\WindBoard\...` to the broker, the external process cannot resolve it, yet `Launcher` still returns `true` | Map the friendly path to the externally visible one first (`WindBoard/Persistence/AppDataVisiblePathResolver`, see §4.2), then open through the shell (folder → `explorer.exe "<dir>"`; file → `Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })`). Log **both** the success and the failure branch |
| The registered package **crashes on startup** with `Microsoft.UI.Xaml.Markup.XamlParseException: Cannot locate resource from 'ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml'`, while the build itself was clean (0 warnings/errors) | The Appx PRI was never **merged**: the package-root `resources.pri` is the app-only PRI (~190 KB) instead of the merged one (~2.3 MB), because the packing inputs (`obj\<Platform>\Release\<tfm>\<rid>\pri.resfiles`) were produced from **stale intermediate outputs** in a long-lived working tree (reproduced 2026-09-23 after local multi-architecture MSIX builds) | Rebuild from a clean intermediate state (`obj`/`bin`) — the identical command in a clean worktree produced `resources.pri` = 2,376,232 B and the app started normally. Cheap guard: before registering, assert the unpacked `resources.pri` is in the merged size range (§4.1 step 2.5); a ~190 KB file means the payload PRI was not merged |

### 4.1 Verification without admin rights (loose-layout registration)

`Add-AppxPackage -AllowUnsigned` and self-signing both require an elevated shell, so on a non-admin machine the packaged form can still be exercised through loose-layout registration (developer mode unlock must be on: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock\AllowDevelopmentWithoutDevLicense = 1`):

````powershell
# 0) BACK UP app data first — uninstalling the Store build DELETES %LOCALAPPDATA%\Packages\<PFN>\ (measured 2026-09-23)
Copy-Item "$env:LOCALAPPDATA\Packages\<PFN>\LocalCache\Local\WindBoard" "$env:TEMP\wb-data-backup" -Recurse -Force
# 1) produce the package (see §2 / docs/dev/guides/msix-packaging.zh-CN.md)
# 2) unpack: .msixupload is a zip container, Expand-Archive rejects the extension
Copy-Item WindBoard_x_x64.msixupload WindBoard_x_x64.zip; Expand-Archive WindBoard_x_x64.zip -DestinationPath upload
makeappx unpack /p upload\WindBoard_x_x64.msix /d unpacked /o
# 2.5) ASSERT the Appx PRI is merged BEFORE registering, otherwise the app dies on startup
#      merged payload PRI ≈ 2.3 MB; app-only (~190 KB) means the packing inputs were stale → clean obj/bin and rebuild
(Get-Item unpacked\resources.pri).Length
# 3) uninstall the Store build first: one Identity can only have one registration
Remove-AppxPackage <PackageFullName>
# 4) register the extracted layout (AppxManifest.xml must sit in its root)
Add-AppxPackage -Register <unpacked>\AppxManifest.xml
# 5) launch by AUMID — it is <PackageFamilyName>!<Application Id>, NOT the PackageFullName
Start-Process explorer.exe -ArgumentList "shell:appsFolder\<PackageFamilyName>!App"
````

Notes:

- The registered package reports `IsDevelopmentMode=True` and `SignatureKind=None`; app data stays under `%LOCALAPPDATA%\Packages\<PFN>\...`.
- **Uninstalling is NOT data-preserving** (corrected 2026-09-23): `Remove-AppxPackage <PackageFullName>` removed `%LOCALAPPDATA%\Packages\<PFN>\` together with `LocalCache\Local\WindBoard\{settings.json,Logs,camouflage}`. Always run step 0 first, and restore the backup after the user reinstalls from the Store. The exported settings JSON (`设置管理 → 导出设置`) is an equivalent recovery path.
- Limitations that come from loose layout, not from the app: `AppNotificationManager.Register()` throws a WinRT exception under this shape.
- Assert the outcome programmatically instead of by eye: check the main-window title / UIA element names against the expected strings (localization keys would show up verbatim), and grep the packaged log for `L10n` missing-key warnings.
- Guard the launch: `Start-Process shell:appsFolder\...` can return before the app reaches the UI thread. Verify the process is alive and a window title exists (e.g. `Get-Process WindBoard | Select MainWindowTitle`) before handing the machine over for manual steps, and check the packaged log for a startup crash report.

### 4.2 App-data path visibility (contract for handing paths to external processes)

Measured on packaged builds (2026-09-22 loose-layout registration, 2026-09-23 Store-installed 2.10.0.0):

- Inside a packaged process **both** `Environment.GetFolderPath(LocalApplicationData)` and the `LOCALAPPDATA` environment variable return the **real** `C:\Users\<u>\AppData\Local` — the redirected private location is *not* observable from either one.
- Newly created content under `%LOCALAPPDATA%\WindBoard\...` physically lives in
  `%LOCALAPPDATA%\Packages\<PFN>\LocalCache\Local\WindBoard\...`, i.e. `%LOCALAPPDATA%` → `<LocalCache>\Local`.
  The "merged at runtime to appear in the real AppData location" behaviour exists **only inside the app process**; Explorer / Notepad / any external process sees the real `AppData` and therefore cannot resolve the friendly path.
- Contract: **any path handed to an external process must be mapped first** —
  `visiblePath = <localCacheRoot>\Local\<friendlyPath relative to the friendly LocalAppData root>`.
  - Single implementation point: `WindBoard/Persistence/AppDataVisiblePathResolver`
    (`TryMap` = pure, injectable mapping; `TryResolve` = composition entry using `AppInstallProbe.IsPackagedProcess()` and
    `Microsoft.Windows.Storage.ApplicationData.GetDefault().LocalCachePath`).
  - The `Local` level is supported by the official troubleshooting example
    (`%LocalAppData%\Packages\<PFN>\LocalCache\Local\VFS\`) **plus local measurement** — it is *not* an API contract.
    Re-verify after an OS or Windows App SDK upgrade, and keep the failure path below intact.
- Failure fallback (required): when mapping fails (API unavailable, path outside `%LOCALAPPDATA%`, unexpected layout) the caller must report a failure that includes the path and write an `AppLog.Warn`. Do **not** silently open/copy the friendly path (external processes cannot resolve it) and do not add an "export a copy" workaround.
- Non-packaged shapes are unaffected: the friendly path *is* the real path, so no mapping is applied.

### 5. Good / Base / Bad

- **Good**: `WindBoardPackage` unset → `WindowsPackageType=None`, `AppxManifest` empty, portable output contains no MSIX assets, and only one extra cached P/Invoke (`GetCurrentPackageFullName`) runs at startup.
- **Base**: `WindBoardPackage=Msix` + `AppxBundle=Never` → one `UapAppxPackageBuildMode=StoreUpload` run yields `<name>_<arch>.msixupload` (containing a single `<name>_<arch>.msix` with resources embedded) and `_Test\<name>_<arch>.msix`.
- **Bad**: relying on files sitting in the publish directory to end up in the package; putting `Package.appxmanifest` into the default build; editing the source manifest's version at build time.

### 6. Tests Required

- `WindBoard.Tests/Publishing/WindBoardProjectPublishConfigurationTests.cs`:
  - exactly one `dotnet publish $project` command for the portable build, and **within that command slice** both `-p:PublishReadyToRun=false` and `--self-contained true` must appear (do not weaken this back into a whole-file occurrence count);
  - no Inno/`iscc` packaging steps remain in `release.yml`;
  - Release `files:` contains only `dist/*.zip` and `dist/latest.json`.
- Regression on every packaging change: `dotnet build WindBoard.slnx -c Release -p:CodeAnalysisTreatWarningsAsErrors=true` and `dotnet test WindBoard.slnx`.

### 7. Wrong vs Correct

#### Wrong
```xml
<!-- payload relies on disk contents; version edited in the source manifest -->
<Target Name="AddCrashReporter" AfterTargets="Publish" />
<!-- ...and CI then runs: dotnet msbuild /p:GenerateAppxPackageOnBuild=true -->
```

#### Correct
```xml
<Target Name="WindBoard_AddCrashReporterToMsixPayload"
        Condition="'$(WindBoardPackage)' == 'Msix'"
        BeforeTargets="_ComputeAppxPackagePayload">
  <!-- publish with an ABSOLUTE PublishDir first, then inject into @(PackagingOutputs) -->
</Target>
```
```powershell
msbuild WindBoard/WindBoard.csproj /t:Publish `
  -p:WindBoardPackage=Msix -p:GenerateAppxPackageOnBuild=true -p:AppxBundle=Never `
  -p:AppxPackageDir="<abs>/" -p:PublishDir="<abs>/" -p:UapAppxPackageBuildMode=StoreUpload
```

---

## Fragile seams (re-verify on dependency upgrade)

> **Warning**: the version-injection target hooks an **internal** build-tools target name (`_ValidatePresenceOfAppxManifestItems`). If a future `Microsoft.Windows.SDK.BuildTools.MSIX` release renames it, injection silently stops happening (no error) and every Store submission would carry a stale version. After upgrading that package, re-run a MSIX build and assert the packaged `AppxManifest.xml` version.

> **Warning**: this project must build with `AppxBundle=Never` on top of `UapAppxPackageBuildMode=StoreUpload`, so each architecture yields a single `.msixupload` (one `.msix`, resources embedded). Producing per-architecture **bundles** instead makes Partner Center reject the whole submission with duplicate full names (`<Name>_<ver>_Neutral_split.scale-*`), because single-project MSIX cannot merge architectures into one bundle. Verified with `Microsoft.Windows.SDK.BuildTools.MSIX` 1.7.251221100.

---

## Pending verification (cannot be validated without installing the package)

These block Store submission sign-off, not the code currently in the repo:

| # | Item | How to verify |
|---|---|---|
| V4 | Where MSIX virtualization redirects newly created `AppData` files | **Answered (2026-09-23)**: `%LOCALAPPDATA%\Packages\<PFN>\LocalCache\Local\...` — a Store-installed 2.10.0.0 app logs `%LOCALAPPDATA%\WindBoard\Logs\...` while its files physically sit under `...\LocalCache\Local\WindBoard\Logs\`. Residual risk: the `Local` level is not documented, so re-check after an OS / Windows App SDK upgrade (see §4.2) |
| V5 | What `GetFolderPath(LocalApplicationData)` / `LOCALAPPDATA` return inside a packaged process | **Answered (2026-09-23)**: both return the real `C:\Users\<u>\AppData\Local` (the migration log line prints `legacy == own`, both derived from that path). The redirected private location is therefore **not** reachable through environment variables — a friendly path must be mapped explicitly (§4.2) |
| V6 | Whether the legacy real `%LOCALAPPDATA%\WindBoard\settings.json` is readable from the package | Run the migration flow against a real legacy install |
| V7 | CrashReporter launching from the read-only package directory + its WinForms writes | Trigger a crash path in the installed package |
| — | File pickers (`FileSavePicker`/`FileOpenPicker` + `InitializeWithWindow`) behavior under packaging (affects settings export/import and `.wbix` import/export) | Manual check in the installed package |

Evidence for V4/V5 (2026-09-22 loose layout, 2026-09-23 Store-installed 2.10.0.0): `AppLog` writes aimed at `%LOCALAPPDATA%\WindBoard\Logs\` land in `%LOCALAPPDATA%\Packages\<PFN>\LocalCache\Local\WindBoard\Logs\`, i.e. AppData writes are virtualized as expected. Store-installed (read-only `Program Files\WindowsApps`) behaviour matches the loose layout for this concern. Contract + failure fallback: §4.2.
| — | Single-instance interaction between the MSIX form and the portable form | Launch both, observe |

> Related design record: `.trellis/tasks/09-12-msix-packaging-migration/design.md` §6 and `research/msix-facts.md` §9.
