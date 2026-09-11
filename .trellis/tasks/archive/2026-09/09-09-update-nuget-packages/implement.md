# 执行计划：NuGet 依赖升级到最新稳定版

## 前置：基线验证（升级前必须先确认现状是绿的）

- [ ] `dotnet build WindBoard.slnx -c Release` 成功（基线）
- [ ] `dotnet test WindBoard.slnx` 全部通过（基线）
- [ ] `dotnet list package --outdated`（solution 级）记录实时最新稳定版，作为版本号最终依据

## 批次 A：测试工程包（低风险先行）

- [ ] `WindBoard.Tests.csproj`：升级 `Microsoft.NET.Test.Sdk` / `xunit` / `xunit.runner.visualstudio` / `coverlet.collector` 到最新稳定补丁版
  - 约束：保持 xUnit v2 栈；若 `xunit.runner.visualstudio` 4.x 要求 xUnit v3，则 runner 保持 3.x 最新并在报告中记录
- [ ] 验证：`dotnet test WindBoard.slnx`
- [ ] 回滚点：还原 `WindBoard.Tests.csproj`

## 批次 B：低风险包（主工程，无 API 变更预期）

- [ ] `WindBoard.csproj`：
  - `Markdig` 1.1.1 → 1.3.0
  - `System.Drawing.Common` 10.0.3 → 最新 10.0.x 补丁
  - `Vortice.Direct2D1` / `Vortice.Direct3D11` 3.8.2 → 3.8.3
  - `Microsoft.Windows.SDK.BuildTools` → 最新稳定版
- [ ] 验证：`dotnet build WindBoard.slnx -c Release` + `dotnet test WindBoard.slnx`
- [ ] 回滚点：还原 `WindBoard.csproj`

## 批次 C：主升级（高风险，WinAppSDK 与 DevWinUI 同批）

- [ ] `WindBoard.csproj`：
  - `Microsoft.WindowsAppSDK` 1.8.260209005 → 2.4.0
  - `DevWinUI.Controls` 9.9.4 → `DevWinUI` 10.4.1
- [ ] `dotnet build WindBoard.slnx -c Release`
  - 若 `WindowedContentDialog` API 报错：对照 DevWinUI v10 迁移文档修正调用点（当前已知 v10 变更为包合并与资源路径，API 预期兼容）
- [ ] `dotnet test WindBoard.slnx`
- [ ] 回滚点：还原 `WindBoard.csproj`（同时还原两个包）

## 冒烟验证（批次 C 之后，人工执行）

- [ ] 启动应用：主窗口显示、画板绘制/缩放/撤销重做正常（Vortice 渲染回归）
- [ ] 设置页：打开各设置页，SettingsCard/SettingsExpander 渲染正常（CommunityToolkit 兼容性）
- [ ] 导出 PNG（`ExportPickers`，重点验证 WinAppSDK 2.0 FileSavePicker 行为变更）：
  - 新文件名保存 → 成功生成文件
  - 覆盖已有文件 → 弹覆盖确认
- [ ] 关于页 → 检查更新：`WindowedContentDialog` 弹窗正常显示与关闭
- [ ] 导入 WBIX/图片 快速过一遍（FileOpenPicker 路径）

## 收尾

- [ ] `dotnet list package --outdated` 复查：确认无剩余可用稳定版升级项（或剩余项均有记录说明）
- [ ] 汇总报告：升级结果、遇到的问题与处理、与升级无关的独立发现（单列，不在本任务内修改）
- [ ] 提醒：framework-dependent 发布变体需要 Windows App Runtime 2.x —— 属分发策略问题，单独报告不擅自处理

## 验证命令

```powershell
dotnet build WindBoard.slnx -c Release
dotnet test WindBoard.slnx
dotnet list package --outdated
```

## 执行结果（2026-09-09）

- 基线：Release 构建 ✓（0 警告 0 错误）、371 测试全过 ✓
- 批次 A ✓：coverlet.collector 10.0.1、Microsoft.NET.Test.Sdk 18.9.0、xunit.runner.visualstudio 4.0.0（实测与 xUnit v2 兼容）、xunit 保持 2.9.3
- 批次 B ✓：Markdig 1.3.2（实时最新，高于调研快照 1.3.0）、System.Drawing.Common 10.0.11、Vortice.* 3.8.3、BuildTools 10.0.28000.2705
- 批次 C ✓：WindowsAppSDK 2.4.0 + DevWinUI 10.4.1；`AboutSettingsPage.Updates.cs` 按 v10 API 迁移（见 design.md 第 3 节延伸的属性映射，实测 `HasTitleBar` 在 v10 仍存在）
- 验证 ✓：Release 构建 0 警告 0 错误、371 测试全过、`dotnet list package --outdated` 无任何剩余升级项、lint 无告警
- 清理 ✓：按用户指示移除失效的 FileSavePicker 预创建 workaround（两处：`ExportPickers.cs`、`SettingsManagementPage.xaml.cs`），每处清理后回归（构建 + 371 测试）全绿
- trellis-check ✓：全量检查 PASS（有条件）；已还原范围外的 `.qlty/` 目录删除；PRD 约束已同步决策变更
- 待办：人工冒烟验证（见上方冒烟清单）尚未执行，需要用户运行应用确认

