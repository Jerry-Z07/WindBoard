# AGENTS.md

## 通用规则（General Rules）

- 关键路径需要有必要的日志输出与错误处理
  - 主程序统一使用 `WindBoard.Logging.AppLog`（`Info/Warn/Error` 等）
  - `WindBoard.CrashReporter` 为降低依赖，不使用主程序日志系统，统一使用 `WindBoard.CrashReporter.CrashReporterLog`

## 编码规范（Coding Style & Naming）

- 缩进 4 空格；保持现有 `namespace {}` 与大括号风格一致。
- 命名：类型/方法用 `PascalCase`；私有字段用 `_camelCase`。

## 测试与验证（Testing）

- 测试工程：`WindBoard.Tests`（xUnit）。为了避免把实现细节暴露为 `public`，主工程通过：
  - `WindBoard/InternalsVisibleTo.cs`
  - `WindBoard.CrashReporter/InternalsVisibleTo.cs`
  允许测试访问 `internal` 类型。
- 运行测试：`dotnet test WindBoard.slnx`（默认平台已映射到 x64；如需显式指定可用 `dotnet test WindBoard.slnx -p:Platform=x64`）。
- 本地化 Key 审计：`WindBoard.Tests/Localization/LocalizationKeyAuditTests.cs`（要求 C# 中 `L10n.Get/Format` 的 key 为字符串字面量；XAML 使用 `{l10n:Loc Key=...}`）。
- 测试分层建议：
  - UI/渲染集成验证放到更高层（后续可考虑 UI 自动化/端到端 smoke），避免单测依赖 WinUI 线程与设备环境。

## 相关文档（Docs）

- `docs/dev/guides/localization.zh-CN.md`：本地化约定。
- `docs/dev/guides/wbix.zh-CN.md`：WBIX（`.wbix`）格式说明。
- 不要阅读 `docs/release-notes/` 和 `docs/dev/archive/` 中的内容。
- `docs/dev/rules`：编码前需阅读，包括项目架构。
