# 技术设计：更新结果弹窗回归原生 ContentDialog

## 改动面

| 文件 | 动作 |
|---|---|
| `WindBoard/WindBoard.csproj` | 移除 `DevWinUI` PackageReference |
| `WindBoard/Settings/Pages/AboutSettingsPage.Updates.cs` | 删除 windowed 路径（`ShowWindowedUpdateResultDialogAsync` / `WrapWindowedDialogContent` / `using DevWinUI` 及 v10 映射注释）；接入内容高度自适应与宽度决策 |
| `WindBoard/Updates/UpdateResultDialogLayoutPlan.cs` | 决策输入加入窗口可用尺寸，输出滚动区 MaxHeight 建议 |
| `WindBoard/UI/Common/WindowedDialogPresentationPlan.cs` | 删除 |
| `WindBoard.Tests/UI/Common/WindowedDialogPresentationPlanBuilderTests.cs` | 删除 |
| `WindBoard.Tests/Updates/UpdateResultDialogLayoutPlanBuilderTests.cs` | 按新决策输入改写并补宽度分支用例 |

## 决策与布局设计

延续现有"纯决策类型"模式（`UpdateResultDialogLayoutPlanBuilder` 保持可单测、无 UI 依赖）：

- 输入：`AppUpdateCheckResult`、culture、`windowSize`（来自 `XamlRoot.Size`，单位 DIP）。
- 两栏决策：`State == UpdateAvailable && windowSize.Width >= TwoColumnThreshold`。
  阈值 = 两栏内容 MinWidth(980) + 弹窗左右 chrome 预留（约 80）≈ 1060，实现时按实测微调。
- 高度自适应（单栏）：外层 ScrollViewer 包裹整个内容面板，
  `MaxHeight = clamp(windowSize.Height - DialogVerticalChrome, MinContentHeight, MaxContentHeight)`
  （chrome 预留标题 + 命令区约 180；下限 240、上限 560）。单栏下移除现有 changelog 内层 ScrollViewer 的 260 上限，由外层统一滚动，避免双层滚动条。
- 高度自适应（两栏）：两栏各自 ScrollViewer
  `MaxHeight = clamp(windowSize.Height - DialogVerticalChrome, 280, 480)`。

## 响应式处理

弹窗打开期间窗口仍可缩放：订阅 `XamlRoot.Changed` 更新滚动区 MaxHeight，`dialog.Closed` 时退订，并在 `ShowAsync` 异常路径用 `finally` 兜底退订（`ShowAsync` 抛异常时 `Closed` 不触发）。

> 实施修正：WinUI 3 的 `XamlRoot` 没有 `SizeChanged` 事件，尺寸变化通知走 `XamlRoot.Changed`；且 `XamlRootChangedEventArgs` 不携带新尺寸（与 UWP 不同），实现直接读取当前 `sender.Size` 重算，非尺寸触发的变更重算结果幂等。

## 兼容与回滚

- 无持久化/格式变更；fd/sc 发布变体无关。
- 单提交原子改动，回滚 = `git revert`。
- 收尾（Phase 3.3）更新 `.trellis/spec/frontend/winui-dependencies.md`：删除 DevWinUI v10 契约小节，补充"弹窗内容自适应窗口"约定。

## 已接受的取舍

- 独立窗口"可缩放看两栏大图"的体验不再存在；宽设置窗口下仍保留两栏。
- 弹窗高度上限受窗口高度约束（这正是"不截断"的来源）；超长更新日志靠滚动查看。
