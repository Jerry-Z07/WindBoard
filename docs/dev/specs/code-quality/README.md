# 代码质量清单（复杂度 / 可维护性基线）

本文档用于把当前仓库的复杂度热点与可维护性风险“落盘”，作为后续持续治理的**基线**与 PR/迭代的对照标准。

持续治理路线图：见 `docs/dev/spec/CODE_QUALITY_PLAN.md`。

## 元信息

- 生成时间：`2026-02-28 00:58:06 +08:00`
- 仓库版本：`250152c1ee57c8ee0a1f2eeedd385725d6caa230`
- 工具版本：`qlty 0.612.0 windows-x64 (e4b1bad 2026-02-11)`
- 统计口径：
  - 仅统计仓库源码：排除 `**/bin/**`、`**/obj/**`
  - `qlty metrics/smells/check` 以仓库现有 `.qlty/qlty.toml` 配置为准

## 复现命令

> 建议在 PowerShell 下执行；若输出有颜色，可临时设置：`$env:NO_COLOR=1`。

- 源码文件数（排除 `bin/obj`）：
  - `Get-ChildItem -Recurse -File -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Measure-Object`
  - `Get-ChildItem -Recurse -File -Filter *.xaml | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Measure-Object`
- 指标统计：
  - `qlty metrics .`
  - `qlty metrics --sort complexity .`
- 异味清单（不含代码片段，便于粘贴与 diff）：
  - `qlty smells --no-snippets .`
- 规范/安全检查（linters）：
  - `qlty check --summary --no-progress .`

## 规模概览（排除 bin/obj）

| 工程 | `.cs` 文件数 | `.xaml` 文件数 |
| --- | ---: | ---: |
| `WindBoard/` | 192 | 16 |
| `WindBoard.CrashReporter/` | 5 | 0 |
| `WindBoard.Tests/` | 56 | 0 |
| **合计** | **253** | **16** |

## qlty metrics 总览（TOTAL）

```
TOTAL | classes=278 | funcs=1452 | fields=906 | cyclo=5577 | complexity=3455 | LCOM=153 | lines=38158 | LOC=25202
```

## 热点文件（按 complexity 降序，Top 20）

| # | 文件 | complexity | cyclo | LOC |
| ---: | --- | ---: | ---: | ---: |
| 1 | `WindBoard/Features/Import/UI/ImportDialog.xaml.cs` | 146 | 163 | 777 |
| 2 | `WindBoard/Board/Persistence/Wbix/WbixWorkspaceSerializer.cs` | 123 | 174 | 550 |
| 3 | `WindBoard/Controls/BoardCanvasControl.Rendering.cs` | 122 | 153 | 473 |
| 4 | `WindBoard/Rendering/Board/BoardSceneRenderer.cs` | 120 | 247 | 844 |
| 5 | `WindBoard/Settings/Pages/AboutSettingsPage.xaml.cs` | 103 | 140 | 595 |
| 6 | `WindBoard/Localization/L10n.cs` | 93 | 90 | 288 |
| 7 | `WindBoard/Features/Camouflage/Services/CamouflageService.cs` | 83 | 131 | 503 |
| 8 | `WindBoard/Interaction/BoardInputController/BoardInputController.Operations.cs` | 82 | 111 | 430 |
| 9 | `WindBoard.Tests/Localization/LocalizationKeyAuditTests.cs` | 81 | 104 | 293 |
| 10 | `WindBoard/Interaction/BoardInputController/BoardInputController.Manipulation.cs` | 76 | 131 | 305 |
| 11 | `WindBoard/Features/Dock/UI/DockSettingsPage.xaml.cs` | 74 | 105 | 586 |
| 12 | `WindBoard/Features/Dock/Services/DockSettingsApplier.cs` | 67 | 69 | 303 |
| 13 | `WindBoard/Settings/AppSettingsStore.cs` | 65 | 67 | 294 |
| 14 | `WindBoard/Features/Dock/Services/ShortcutDockIconLoader.cs` | 62 | 91 | 239 |
| 15 | `WindBoard/Features/Import/Wbi/WbiWorkspaceImporter.cs` | 60 | 84 | 330 |
| 16 | `WindBoard/Updates/SemanticVersion.cs` | 58 | 90 | 200 |
| 17 | `WindBoard/Features/Export/Services/BoardExportService.cs` | 56 | 69 | 256 |
| 18 | `WindBoard/MainWindow.xaml.cs` | 54 | 86 | 407 |
| 19 | `WindBoard/Interaction/BoardInputController/BoardInputController.Pointer.cs` | 53 | 99 | 277 |
| 20 | `WindBoard/Features/Camouflage/UI/CamouflageSettingsPage.xaml.cs` | 53 | 80 | 357 |

## smells 清单（`qlty smells --no-snippets .`）

> 说明：该清单更偏“结构风险提示”（参数过多、return 过多、复杂表达式、函数高复杂度等），适合用于拆分任务与设定门禁策略。

```text
WindBoard/Board/Persistence/BoardWorkspaceSnapshotConverter.cs
  74  Function with many returns (count = 6): TryCreateElementSnapshot

WindBoard/Board/Persistence/Wbix/WbixWorkspaceSerializer.cs
 488  Function with many parameters (count = 8): TryParseElement
 310  Function with many returns (count = 6): CreateWbixPageElements
 488  Function with many returns (count = 6): TryParseElement
 598  Function with many returns (count = 8): TryExtractResourceToTempFile
   1  High total complexity (count = 123)
  55  Function with high complexity (count = 20): SaveAsync

WindBoard/Controls/BoardCanvasControl.Rendering.cs
   1  High total complexity (count = 122)
 235  Function with high complexity (count = 18): ShowSelectionDockOverlay
 348  Function with high complexity (count = 19): OnSelectionBringToFrontClicked

WindBoard/Errors/AppCrashReportStore.cs
  22  Function with many parameters (count = 6): TryWriteCrashReport
  40  Function with many parameters (count = 7): TryWriteCrashReport
 146  Function with many parameters (count = 8): BuildReportText

WindBoard/Errors/AppErrorService.cs
 282  Function with many returns (count = 6): TryLaunchCrashReporter

WindBoard/Features/Camouflage/Services/CamouflageService.cs
 213  Function with many parameters (count = 6): TryUpdateDesktopShortcut
 661  Function with many parameters (count = 8): PrivateExtractIcons
   1  High total complexity (count = 83)

WindBoard/Features/Camouflage/UI/CamouflageSettingsPage.xaml.cs
 107  Function with many returns (count = 6): SyncIconPreviewFromSettingsAsync
   1  High total complexity (count = 53)
 107  Function with high complexity (count = 18): SyncIconPreviewFromSettingsAsync

WindBoard/Features/Dock/Services/DockSettingsApplier.cs
 207  Function with many returns (count = 7): GetShortcutTitle
 309  Function with many returns (count = 7): OnShortcutDockItemClicked
   1  High total complexity (count = 67)
 309  Function with high complexity (count = 25): OnShortcutDockItemClicked

WindBoard/Features/Dock/Services/ShortcutDockIconLoader.cs
 226  Function with many returns (count = 7): IsLikelyImageBytes
   1  High total complexity (count = 62)
 146  Function with high complexity (count = 20): TryLoadFaviconAsync

WindBoard/Features/Dock/UI/DockSettingsPage.xaml.cs
   1  High total complexity (count = 74)

WindBoard/Features/Export/ExportFlow.cs
  43  Function with many returns (count = 7): StartAsync

WindBoard/Features/Export/Services/BoardExportService.cs
  98  Function with many parameters (count = 6): ExportPngPagesToFolderAsync
   1  High total complexity (count = 56)
 269  Function with high complexity (count = 19): TryAddWbixEmbeddedImageResources

WindBoard/Features/Export/Services/BoardRasterExporter.cs
 280  Function with many parameters (count = 6): SaveWicBitmapToFile
 368  Function with many parameters (count = 6): RenderedWicBitmap

WindBoard/Features/Export/Services/IBoardExportService.cs
  20  Function with many parameters (count = 6): ExportPngPagesToFolderAsync

WindBoard/Features/Export/UI/ExportDialog.cs
  15  Function with high complexity (count = 18): ShowAsync

WindBoard/Features/Import/Services/BoardImportService.cs
   1  High total complexity (count = 50)
  20  Function with high complexity (count = 41): ImportElementsAsync

WindBoard/Features/Import/Services/ImportFileTypeResolver.cs
  26  Function with many returns (count = 10): Resolve

WindBoard/Features/Import/UI/ImportDialog.xaml.cs
 143  Function with many returns (count = 7): OnPrimaryButtonClick
   1  High total complexity (count = 146)
 143  Function with high complexity (count = 37): OnPrimaryButtonClick
 400  Function with high complexity (count = 20): AddFilesToQueueAsync

WindBoard/Features/Import/Wbi/WbiPreviewReader.cs
  34  Function with many returns (count = 6): TryReadAsync

WindBoard/Features/Import/Wbi/WbiWorkspaceImporter.cs
 154  Function with many parameters (count = 6): ImportPageAsync
 270  Function with many parameters (count = 6): TryCreateElementFromAttachmentAsync
 330  Function with many parameters (count = 6): CreateImageElementAsync
  63  Function with many returns (count = 7): ImportAsync
 270  Function with many returns (count = 7): TryCreateElementFromAttachmentAsync
   1  High total complexity (count = 60)
 154  Function with high complexity (count = 21): ImportPageAsync
 164  Function with high complexity (count = 21): ImportPageAsyncCore

WindBoard/Features/Shortcuts/Models/KeyboardShortcutGesture.cs
  36  Function with many returns (count = 6): TryParse

WindBoard/Interaction/BoardInputController/BoardInputController.Manipulation.cs
 294  Function with many returns (count = 7): TryHandleSelectionManipulationDelta
 173  Complex binary expression
   1  High total complexity (count = 76)
 294  Function with high complexity (count = 25): TryHandleSelectionManipulationDelta

WindBoard/Interaction/BoardInputController/BoardInputController.Operations.cs
   1  High total complexity (count = 82)

WindBoard/Interaction/BoardInputController/BoardInputController.Pointer.MoveEnd.cs
 140  Function with high complexity (count = 20): HandlePointerEnded

WindBoard/Interaction/BoardInputController/BoardInputController.Pointer.cs
   1  High total complexity (count = 53)

WindBoard/Localization/L10n.cs
   1  High total complexity (count = 93)
 178  Function with high complexity (count = 19): Get

WindBoard/Logging/AppLogLevelParser.cs
  10  Function with many returns (count = 8): TryParse

WindBoard/MainWindow.xaml.cs
   1  High total complexity (count = 54)

WindBoard/Persistence/AppDataPaths.cs
 171  Function with many returns (count = 6): TryMigrateSettingsFileIfNeeded

WindBoard/Reminders/AppReminderToastArguments.cs
  31  Function with many returns (count = 6): TryParseClickAction

WindBoard/Rendering/Board/BoardSceneRenderer.cs
 822  Function with many returns (count = 7): TryGetOrCreateImageBitmap
 396  Complex binary expression
 402  Complex binary expression
 441  Complex binary expression
 447  Complex binary expression
   1  High total complexity (count = 120)
 625  Function with high complexity (count = 21): GetElementCardVisual

WindBoard/Settings/AppLanguagePreference.cs
  35  Function with many returns (count = 10): TryNormalize
 185  Function with many returns (count = 6): TryResolveByLanguageCodeToSupported

WindBoard/Settings/AppLanguageService.cs
 117  Function with many returns (count = 7): ApplyPrimaryLanguageOverride

WindBoard/Settings/AppSettingsStore.cs
   1  High total complexity (count = 65)
 187  Function with high complexity (count = 22): NormalizeShortcutDockSettingsInPlace

WindBoard/Settings/DownloadSourceId.cs
  26  Function with many returns (count = 6): TryParse

WindBoard/Settings/Pages/AboutSettingsPage.xaml.cs
   1  High total complexity (count = 103)
 339  Function with high complexity (count = 19): ShowUpdateResultDialogAsync
 532  Function with high complexity (count = 19): DownloadAssetWithProgressAsync

WindBoard/Settings/UpdateCheckInterval.cs
  26  Function with many returns (count = 6): TryParse

WindBoard/Updates/AppInstallProbe.cs
  45  Function with high complexity (count = 25): ProbeCore

WindBoard/Updates/AppUpdateService.cs
 411  Function with many returns (count = 6): TryGetChangelog

WindBoard/Updates/BackgroundDownloadService.cs
 116  Function with many parameters (count = 6): DownloadFromSourceAsync
 116  Function with many returns (count = 7): DownloadFromSourceAsync
  23  Function with high complexity (count = 21): DownloadWithFailoverAsync
 116  Function with high complexity (count = 25): DownloadFromSourceAsync

WindBoard/Updates/SemanticVersion.cs
  27  Function with many returns (count = 8): CompareTo
 177  Function with many returns (count = 6): TryParsePrerelease
 169  Complex binary expression
   1  High total complexity (count = 58)

WindBoard.Tests/Localization/LocalizationKeyAuditTests.cs
 267  Function with many returns (count = 7): TryParseKeyStringLiteral
   1  High total complexity (count = 81)
 267  Function with high complexity (count = 21): TryParseKeyStringLiteral
```

## 当前评分（10 分制，分数越高越健康/越易维护）

- 复杂度控制：`7.0/10`（整体 OK，但存在少数“巨型文件/巨型函数”集中点）
- 架构与分层：`8.0/10`（模块边界清晰：`Board/`、`Interaction/`、`Rendering/`、`Features/`）
- 可维护性（可读性/一致性/工程化）：`8.3/10`（工具链可用，`qlty check` 无告警）
- 可测试性：`6.5/10`（有 `WindBoard.Tests`，但 UI/渲染/输入热点逻辑天然较难细粒度单测）
- 可观测性与鲁棒性：`8.5/10`（启动早期日志与全局异常捕获链路较完整）

## 持续治理方案（建议落地顺序）

### 1）先“可见化”：让风险变成清单与趋势

- 固化本文件为“基线”，每个迭代/版本更新一次（或每月一次）。
- PR 级别最低要求：开发者在本地至少跑 `qlty check --summary .` 与 `dotnet test WindBoard.slnx`（若本 PR 涉及核心逻辑）。

### 2）再“防回归”：先不追求立刻降低总量，先保证不变差

- 软门禁（推荐起步）：PR 中若修改了 Top 20 热点文件，要求：
  - 不新增 `Function with high complexity`
  - 不让该文件 complexity 增长（或增长必须给出理由）
- 硬门禁（成熟后）：把软门禁变为 CI 失败条件（例如 GitHub Actions）。

### 3）最后“做预算”：按热点模块拆解治理任务（每迭代 1～2 个点）

优先级建议（收益/风险比最高）：

1. `WindBoard/Features/Import/UI/ImportDialog.xaml.cs`：把导入流程与 UI 状态机拆成服务/流程对象，页面仅保留输入收集与 UI 更新。
2. `WindBoard/Board/Persistence/Wbix/WbixWorkspaceSerializer.cs`：将“压缩包 IO / 资源提取 / 元数据解析 / 模型映射”拆为更小的职责单元，减少多 return/多参数函数。
3. `WindBoard/Rendering/Board/BoardSceneRenderer.cs`、`WindBoard/Interaction/BoardInputController/*`：把复杂表达式与大分支拆成命名条件/小函数，并补齐关键失败路径测试（避免交互回归难定位）。
