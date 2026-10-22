# CrashReporter UI Contract (WinForms)

> `WindBoard.CrashReporter` is the **only WinForms UI** in the repository (everything else is WinUI 3).
> It runs as a separate process after an unhandled exception, so it must be the most defensive code in the repo:
> it must open on a machine where the main app just died, and it must never throw.

---

## 1. Scope / Trigger

Read this spec before touching any of:

- `WindBoard.CrashReporter/WindBoard.CrashReporter.csproj` (DPI configuration)
- `WindBoard.CrashReporter/CrashReporterArgs.cs` (command-line contract)
- `WindBoard.CrashReporter/CrashReporterForm.cs` (layout / view logic)
- the `TryLaunchCrashReporter` call site in `WindBoard/Errors/AppErrorService.cs` (cross-process contract)

**Trigger**: this is a cross-layer contract (main process → separate process). Any parameter change is a contract change and needs code-spec depth plus tests on both sides.

---

## 2. Signatures

### Command line (`CrashReporterArgs.Parse`)

| Argument | Required | Value | Notes |
|---|---|---|---|
| `--report` | no | file path | Crash report written by `AppCrashReportStore` |
| `--logs-dir` | no | directory path | Log directory; also where `CrashReporter.log` is appended |
| `--source` | no | enum name | `AppCrashSource` value |
| `--occurred-at` | no | ISO 8601 (`"O"`, InvariantCulture) | Crash time, produced by the main process |
| `--exception-type` | no | type full name | `exception?.GetType().FullName ?? exceptionObject?.GetType().FullName ?? string.Empty` |
| `--exception-message` | no | single-line string | Already normalized by the main process (see §4) |

`Parse` never throws: unknown keys are ignored, a key without a value keeps the default, `null` input returns a default instance.

### View text builders (testable pure functions)

| Signature | Contract |
|---|---|
| `internal static string BuildSummaryText(CrashReporterArgs args)` | Exactly 4 lines: type / message / time / source. Empty field renders `(unknown)` |
| `internal static string BuildFallbackDiagnosticsText(CrashReporterArgs args)` | Clipboard fallback when the report body is empty |

Both must stay pure (no control access, no file IO) so they can be asserted without a UI thread.

---

## 3. Contracts

### 3.1 High-DPI configuration (must not be "fixed" back to defaults)

```xml
<!-- WindBoard.CrashReporter.csproj -->
<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
```

```csharp
// CrashReporterForm constructor — scaling is self-managed
AutoScaleMode = AutoScaleMode.None;                        // see the measured warning below
ClientSize    = LogicalToDeviceUnits(new Size(900, 640));  // 96 DPI logical -> device px
MinimumSize   = LogicalToDeviceUnits(new Size(780, 540));
layout.Padding = ScalePadding(new Padding(12));            // no Padding overload exists
layout.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(132)));
```

> **Warning (measured on .NET 10 / 144 DPI)**: `AutoScaleDimensions` is normalized by WinForms to the current DPI
> (`Dpi` mode → the current DPI value; `Font` mode → the current font metrics), so
> `AutoScaleDimensions == CurrentAutoScaleDimensions` always holds and `PerformAutoScale` runs with a factor of exactly **1**.
> A code-only `Form` that sets `ClientSize = 900x640` therefore stays at **900x640 physical pixels** under both
> `AutoScaleMode.Dpi` and `AutoScaleMode.Font` — the window never grows with the display scaling.

- **Do** convert every fixed pixel value (`ClientSize` / `MinimumSize` / `Padding` / `Margin` / `Absolute` row height) with `LogicalToDeviceUnits`, keeping all literals at a 96 DPI baseline. `Control.LogicalToDeviceUnits` has **no `Padding` overload** — convert each edge through a helper.
- **Do** pass the already-converted value to `MinimumSize` as well: it keeps the literal at the 96 DPI baseline and is **not** scaled a second time (measured at 144 DPI: `LogicalToDeviceUnits(780, 540)` stays 1170x810) — do not "un-convert" it to dodge a double scale.
- **Do** keep fonts in pt: GDI+ resolves them against the device DPI (measured: 9pt → 23 px at 144 DPI), so text is already physically correct.
- **Do** let `Dock` / `AutoSize` size the controls — auto-sized controls follow the font and need no conversion.
- **Do not** declare a DPI mode in an `app.manifest`: the official guidance is the csproj property, and a manifest triggers compiler warning **WFO0003** and can conflict with app configuration.
- **Do not** rely on `AutoScaleMode` (`Dpi` / `Font`) to make a code-only window scale at startup. Starting with .NET 8 a `PerMonitorV2` top-level window *does* scale according to `AutoScaleMode` (official breaking change `top-level-window-scaling`), but the normalization above collapses the factor to 1 in practice.
- **Do not** use `AutoScaleMode.Inherit` on the top-level `Form`: it has no parent container.

### 3.2 Layout row allocation

`TableLayoutPanel` allocates `Absolute` → `AutoSize` → `Percent`, and a `Percent` row receives only the *remaining* space — when that is insufficient, **its content is clipped**.

Mandatory row semantics for the crash window:

| Row | Style | Content |
|---|---|---|
| Title | `AutoSize` | Fixed short title + one fixed subtitle (never wraps) |
| Error summary | `Absolute` (logical 132) | `GroupBox` + read-only multiline `TextBox` (wraps) |
| Full report | `Percent(100)` | `GroupBox` + read-only `TextBox` (no wrap, both scrollbars) |
| Footer | `AutoSize` | Status label (`AutoSize = false` + `AutoEllipsis`) + button row (`WrapContents = false`) |

> **Warning**: never put **wrapping** text into an `AutoSize` row. At 150% scaling the measured height of wrapping text inflates, the `AutoSize` row is satisfied first, and every `Percent` row is squeezed to a few pixels. A three-line "suggested actions" label was the root cause of the 150%/2K layout collapse.

### 3.3 Label property pairing

- Growable text → `AutoSize = true` (never combined with `Dock = Fill`).
- Fill-the-row text → `Dock = Fill` + `AutoSize = false` (+ `AutoEllipsis` when the text is bounded).

---

## 4. Validation & Error Matrix

| Condition | Required behavior |
|---|---|
| `--exception-message` contains line breaks | Main process folds them to single spaces **before** building the argument (the summary is laid out as `消息：<single line>`) |
| `--exception-message` longer than `MaxExceptionMessageChars` (2000) | Truncated by the main process. Windows command lines cap at 32767 chars; the full text stays in the report file |
| `exception.Message` getter throws (custom exceptions may override it) | Caught at the extraction point; the summary falls back to an empty message. **The crash window must still launch** |
| New args absent (older main process) | Summary renders 4 lines with `(unknown)`; the `Absolute` summary row keeps the layout stable |
| New args present but an older CrashReporter is deployed | Unknown keys and their values are skipped by `Parse`; no side effect |
| Report file missing / empty | Report pane shows the reason; "copy diagnostics" uses `BuildFallbackDiagnosticsText` |
| Any UI action throws | `SafeUiAction` catches, appends `CrashReporterLog.Warn`, and reports on the status line — never rethrows |

---

## 5. Good/Base/Bad Cases

- **Good**: `--exception-type System.NullReferenceException` + a folded one-line message → the summary shows type, message, time and source in four lines at every scaling factor.
- **Base**: only `--source` is supplied → summary shows three `(unknown)` lines plus the source; the report pane still shows the full text.
- **Bad**: passing a raw multi-line `exception.Message`, or deriving the summary inside the window by parsing the report file — the first breaks the single-line layout, the second produces nothing whenever the report write failed (`writable == false` → empty `--report`).

---

## 6. Tests Required

| Test | Assertion points |
|---|---|
| `CrashReporterArgsTests` | All six keys parsed in one call; unknown key + value ignored; key at the end without a value does not throw; `null` input returns defaults; all new fields default to `string.Empty` |
| `CrashReporterFormLayoutTests` | `BuildSummaryText` returns exactly 4 lines and renders `(unknown)` for every empty field; `BuildFallbackDiagnosticsText` includes the new fields; `WindBoard.CrashReporter.csproj` contains `ApplicationHighDpiMode` = `PerMonitorV2`; the project has **no** `app.manifest` |
| `AppErrorServiceTests` | `NormalizeSingleLine`: `null`/empty → empty; `\r\n` / `\n` / `\r` folded to a single space plus trim; exactly `MaxExceptionMessageChars` unchanged; longer input truncated to the limit |

DPI behavior itself is **not** unit-testable (it depends on a real monitor DPI) — it is verified manually per `docs`-style step: 100% / 125% / 150% / 200%.

---

## 7. Wrong vs Correct

#### Wrong
```csharp
// No AutoScaleMode + hard-coded pixel sizes -> window stops scaling,
// fonts still grow with DPI, wrapping labels inflate and squeeze the Percent rows.
MinimumSize = new Size(760, 420);
var suggestions = new Label { AutoSize = true, Text = "建议操作：\r\n- ...\r\n- ..." };
layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
```

#### Correct
```csharp
// Self-managed scaling + semantic row allocation; wrapping text lives in Absolute/Percent rows.
AutoScaleMode = AutoScaleMode.None;
MinimumSize = LogicalToDeviceUnits(new Size(780, 540));             // 96 DPI logical px -> device px
layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));              // title: fixed short text
layout.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(SummaryGroupHeight))); // summary: wraps, fixed height
layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));          // report: eats the rest
layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));              // footer: status + buttons
```

---

## Related

- `.trellis/spec/backend/error-handling.md` §Crash Reporter — launch flow, reentrancy guard, report store.
- `.trellis/spec/backend/logging-guidelines.md` §CrashReporter Independent Logging — `CrashReporterLog` contract.
