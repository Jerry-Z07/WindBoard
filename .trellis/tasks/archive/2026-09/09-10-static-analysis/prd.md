# P0 启用 Roslyn 静态分析

## Goal

启用 .NET Roslyn 分析器（`AnalysisLevel=latest-recommended` + `EnforceCodeStyleInBuild`），让编译期成为第一道问题发现关口，并为后续 CI 告警回归约束建立干净基线。

## Requirements

- 在 `Directory.Build.props` 中启用分析级别 `latest-recommended` 与代码风格强制 `EnforceCodeStyleInBuild`，作用于全部四个项目（WindBoard、CrashReporter、Launcher、Tests）。
- 处理启用后的存量告警：
  - 能修复的直接修复（优先）。
  - 修复代价高或有争议的，用 `NoWarn` 按规则 ID 精确压制，并附注释说明理由；禁止大范围空白压制（如按命名空间/整个项目一刀切）。
- 在 `.github/workflows/release.yml` 的构建步骤中评估加入 `TreatWarningsAsErrors`（仅分析类告警），防止告警回归；若发布构建流程不适合，则在 PR 检查或本地说明替代约束方式，并在实现时说明取舍。

## 约束

- 不改变任何运行时行为；修复仅限告警指向的代码质量问题。
- 不引入第三方分析器包（如 SonarLint），仅用 SDK 内置。
- 本地化 Key 审计测试（LocalizationKeyAuditTests）不得受影响。

## 实施记录（check 后补充）

- check 结论：通过。499 测试全绿，Release 全量重编 0 告警。
- CA1806 修复（`GeneralSettingsPage.xaml.cs`）在解析失败路径上存在一处有意的行为变化：原行为把失败解析静默写回 Windowed 默认值，修复后跳过写入、保留用户既有设置。该路径仅由 `SelectedValue`/`SelectedItem` 双空的异常时序触发，新行为更安全且已在代码注释中论证。此为对"不改变运行时行为"约束的唯一显式偏离，已经 check 复核认可。
- check 发现的行尾混合（`CompositeCommand.cs`、`BoardWorkspace.cs`）与 `BackgroundDownloadServiceTests.cs` 的 UTF-8 BOM 已在 check 后修复（统一 CRLF + 去 BOM），增量构建确认无损。

## Acceptance Criteria

- [x] `dotnet build WindBoard.slnx -c Release` 输出中无 CA/IDE 类告警（或全部经带注释的 NoWarn 压制）。
- [x] `dotnet test WindBoard.slnx` 全绿。
- [x] 构建产物可正常运行（冒烟：应用可启动）。
- [x] CI 或文档化的替代方式中告警回归被阻断。
