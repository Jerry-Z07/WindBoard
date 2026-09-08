# 执行计划

## Checklist（按序）

1. [ ] `UpdateResultDialogLayoutPlanBuilder` 加入窗口尺寸输入，产出两栏决策与滚动区 MaxHeight；先补/改单测（`UpdateResultDialogLayoutPlanBuilderTests`，覆盖宽度足够/不足两分支）。
2. [x] `AboutSettingsPage.Updates.cs`：移除 `using DevWinUI`、windowed 分支与 `WrapWindowedDialogContent`；按 plan 应用单栏外层 ScrollViewer / 两栏各自 ScrollViewer 与自适应 MaxHeight；接入 `XamlRoot.Changed` 响应式调整、`Closed` 退订与 `ShowAsync` 异常路径 `finally` 兜底退订。
3. [x] 删除 `UI/Common/WindowedDialogPresentationPlan.cs` 与 `WindBoard.Tests/UI/Common/WindowedDialogPresentationPlanBuilderTests.cs`。
4. [x] `WindBoard.csproj` 移除 `DevWinUI` 引用；`dotnet build` 确认无残留类型引用（注意 XAML 级联报错：先修 C#，见 winui-dependencies.md gotcha）。
5. [x] 手工验证（用户已确认，2026-09-09）：默认窗口尺寸 / 拉宽窗口 / 弹窗打开时缩放窗口，三种场景下无截断、滚动正常、两栏退化正确。
6. [x] 全量构建与测试验证。

## 执行记录

- **API 偏差**：design 原写 `XamlRoot.SizeChanged`，WinUI 3 实际不存在该事件，改用 `XamlRoot.Changed`；`XamlRootChangedEventArgs` 不携带新尺寸，实现直接读当前 `sender.Size` 重算（幂等）。
- **检查阶段修复**：`ShowAsync` 异常路径的 `Changed` 订阅泄漏（`finally` 兜底退订）、注释与实际事件名一致性、空行风格。
- **验证结果**：`dotnet build WindBoard.slnx -c Release` 0 警告 0 错误；`dotnet test WindBoard.slnx` 374/374 全绿；全仓库代码零 DevWinUI 残留。

## 验证命令

- `dotnet build WindBoard.slnx -c Release`
- `dotnet test WindBoard.slnx`
- 全仓库搜索 `DevWinUI`（应无代码引用，spec 文档除外）

## 评审门 / 回滚点

- 步骤 1 完成后：决策单测先绿再动 UI 层（回滚点 A：仅纯决策改动）。
- 步骤 4 完成后：构建通过即可进入手工验证（回滚点 B：单提交整体 revert）。
