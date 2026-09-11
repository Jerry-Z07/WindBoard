# 更新结果弹窗回归原生 ContentDialog 并移除 DevWinUI 依赖

## Goal

移除 DevWinUI/WindowedContentDialog 依赖，更新结果弹窗回归原生 ContentDialog，内容高度自适应窗口并内部滚动，两栏布局按窗口实际宽度决策，根治默认窗口尺寸下更新日志被截断的问题。

## Background

- DevWinUI 10.4.1 在主工程的唯一使用点是 `AboutSettingsPage.Updates.cs` 的 `WindowedContentDialog`：更新结果弹窗在有属主窗口时改用独立窗口承载，以支持两栏宽布局（MinWidth 980 / MaxWidth 1240）。
- 原生 `ContentDialog` 宿主于 `XamlRoot`，尺寸受窗口约束且模板不自带滚动，默认窗口尺寸下两栏布局会被截断——这是当初引入 DevWinUI 的动机。
- 为单一功能引入整个 DevWinUI 包依赖维护成本高（v10 重写属性名即为例证，见 `AboutSettingsPage.Updates.cs` 内的映射注释）。

## Requirements

- 移除 `WindBoard.csproj` 中 `DevWinUI` 包引用，主工程与测试工程不再出现 DevWinUI 类型引用。
- 更新结果弹窗（检查更新入口的全部状态：UpdateAvailable / UpToDate / Indeterminate / Error）一律使用原生 `ContentDialog`，删除独立窗口承载路径。
- 弹窗内容在任何窗口尺寸下完整可见、不截断：更新日志等内容通过 ScrollViewer 内部滚动，滚动区高度按窗口实际尺寸自适应。
- 两栏布局不再以固定 MinWidth 硬撑弹窗宽度，改为按窗口实际可用宽度决策：宽度足够时两栏，不足时退化为单栏。
- 既有行为保持不变：下载按钮触发下载流程、下载进度/完成/失败弹窗、Markdown 渲染与降级、链接跳转、下载源切换链接。
- 不引入新依赖；本地化 key 变化（如涉及）需通过本地化审计测试。

## Constraints

- 遵循项目"无 MVVM、原生控件优先"约定（AGENTS.md）。
- `.trellis/spec/frontend/winui-dependencies.md` 记录了 DevWinUI v10 契约，依赖移除后需在收尾阶段（Phase 3.3）同步更新该 spec。

## Acceptance Criteria

- [ ] `WindBoard.csproj` 无 `DevWinUI` 引用；全仓库代码无 DevWinUI 类型使用。
- [ ] `dotnet build WindBoard.slnx -c Release` 通过；`dotnet test WindBoard.slnx` 全绿。
- [ ] 默认窗口尺寸下打开"检查更新"弹窗：弹窗整体位于窗口内，更新日志可滚动查看，无内容截断。
- [ ] 窗口拉伸至足够宽度时：有更新状态下仍呈现两栏布局；窗口较窄时自动退化为单栏。
- [ ] `UI/Common/WindowedDialogPresentationPlan.cs` 及对应测试删除，无残留引用。
- [ ] 布局决策测试覆盖"宽度足够/不足"两种分支。
