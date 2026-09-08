# 更新项目 NuGet 依赖包到最新稳定版本

## Goal

将主程序及测试工程的 NuGet 依赖（WinAppSDK、CommunityToolkit、Vortice、Markdig、System.Drawing.Common、xUnit 等统一）升级到当前最新稳定版本，完成兼容性验证并保持构建与测试通过。

## Requirements

- **R1 主工程依赖升级**（`WindBoard/WindBoard.csproj`）：
  - `Microsoft.WindowsAppSDK`：1.8.260209005 → 2.4.0（2026-09-09 调研时的最新稳定版）
  - `DevWinUI.Controls`：9.9.4 → `DevWinUI` 10.4.1（原包已被官方弃用并 unlist，v10 起合并进 `DevWinUI` 主包）
  - `Markdig`：1.1.1 → 1.3.0
  - `Microsoft.Windows.SDK.BuildTools`：10.0.26100.7705 → 最新稳定版（调研快照：10.0.28000.2705）
  - `System.Drawing.Common`：10.0.3 → 最新 10.0.x 补丁（调研快照：10.0.11）
  - `Vortice.Direct2D1` / `Vortice.Direct3D11`：3.8.2 → 3.8.3
- **R2 测试工程依赖升级**（`WindBoard.Tests/WindBoard.Tests.csproj`）：`coverlet.collector`、`Microsoft.NET.Test.Sdk`、`xunit`、`xunit.runner.visualstudio` 升级到最新稳定补丁版本；保持 xUnit v2 技术栈，不迁移 v3
- **R3 CommunityToolkit 保持不变**：`CommunityToolkit.WinUI.Helpers` / `Controls.SettingsControls` 当前 8.2.251219 已是最新稳定版（8.3 仅有 preview），不升级；仅需验证与 WinAppSDK 2.4.0 的兼容性
- **R4 功能保持**：DevWinUI 迁移必须保持 `WindowedContentDialog`（关于页"检查更新"弹窗）功能不变
- **R5 质量门槛**：升级后 Release 构建成功、全部单元测试通过、关键路径冒烟验证通过（见验收标准）

## Constraints

- 不引入新依赖；不改变架构分层与目录结构
- 清理范围说明（2026-09-09 决策变更）：任务初期约束为不动既有代码；用户随后明确指示清理已失效的 FileSavePicker 预创建 workaround（WinAppSDK 2.0 起该行为已不存在，`DateCreated` 分支不可达），共两处：`ExportPickers.cs` 与 `SettingsManagementPage.xaml.cs`（设置导出 JSON 保存路径），均已清理
- 不使用任何预发布（preview/experimental）包
- 文中版本号为 2026-09-09 调研快照；实现时以 nuget.org / `dotnet list package --outdated` 实时结果为准
- WinAppSDK 运行时分发策略（self-contained / framework-dependent 变体的运行时捆绑）不属于本任务范围

## Acceptance Criteria

- [ ] `dotnet build WindBoard.slnx -c Release` 成功
- [ ] `dotnet test WindBoard.slnx` 全部通过
- [ ] 启动冒烟：主窗口正常显示、画板可正常绘制（Vortice D2D/D3D 渲染）
- [ ] 设置页正常打开，SettingsCard/SettingsExpander（CommunityToolkit）渲染正常
- [ ] 导出 PNG 正常：FileSavePicker 在 WinAppSDK 2.0 行为变更（不再预创建空文件）下，"新文件名保存"与"覆盖已有文件弹确认"两条路径均正确
- [ ] 关于页 → 检查更新：`WindowedContentDialog` 弹窗正常显示
- [ ] `dotnet list package --outdated` 复查：无可用稳定版升级项，或剩余项均为有记录说明的不升级项（如 xUnit v3、CommunityToolkit 8.3-preview）
- [ ] 过程中发现与本次升级无关的问题单独列出报告，不在本任务内顺手修改

## Out of Scope

- xUnit v3 迁移（涉及测试平台变更，独立任务）
- CommunityToolkit 8.3-preview 升级
- WinAppSDK 2.x 运行时分发策略调整（若升级导致发布产物/安装包必须处理运行时，完成后单独报告）
- 任何功能性改动
