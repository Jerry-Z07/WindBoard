# 调研结论（2026-09-09）

## 各包最新稳定版结论

| 包 | 当前 | 最新稳定版 | 结论与依据 |
|---|---|---|---|
| Microsoft.WindowsAppSDK | 1.8.260209005 | **2.4.0**（2026-08-13） | Microsoft Learn 下载页 Stable 渠道列表：2.0.1(04-29) → 2.1.3(05-21) → 2.2.0(06-09) → 2.3.1(07-16) → 2.4.0(08-13)；NuGet 页确认 2.4.0 为最新稳定版（另有 2.4.1-experimental 为预发布） |
| DevWinUI.Controls | 9.9.4 | 9.9.4 即最终版，**已弃用 + unlisted** | NuGet 页：弃用原因是 DevWinUI v10.0.0 包重构；官方指引 `DevWinUI.Controls` → `DevWinUI`（v10+），旧 `DevWinUI` → `DevWinUI.Base`。`DevWinUI` 最新稳定版 **10.4.1**（2026-08-28），TFM `net10.0-windows10.0.19041` |
| CommunityToolkit.WinUI.Helpers / Controls.SettingsControls | 8.2.251219 | **8.2.251219（不变）** | GitHub Releases：8.2.251219 仍是最新稳定版（Latest 标记）；8.3 仅有 preview1(2026-02)/preview2(2026-04) |
| Markdig | 1.1.1 | **1.3.0** | NuGet 页（xoofx profile）确认最新稳定版 1.3.0 |
| System.Drawing.Common | 10.0.3 | **10.0.x 系列**（快照见 10.0.11） | NuGet 页确认 10.0.11 存在；实现时以实时结果为准 |
| Vortice.Direct2D1 / Direct3D11 | 3.8.2 | **3.8.3** | NuGet 页确认 Vortice.Direct3D11 3.8.3 |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.7705 | **10.0.28000.2705** | NuGet 页标题确认 |
| 测试包 | 17.14.1 / 2.9.3 / 3.1.4 / 6.0.4 | 实现时确认 | `xunit.runner.visualstudio` 已出现 4.0.0，需确认是否要求 xUnit v3；xUnit v3 走独立迁移路线，本次不动 |

## WinAppSDK 2.0 相对 1.8 的破坏性变更（官方 release notes）

1. 版本方案切换为 SemVer 2.0.0，NuGet 包版本与 SDK 版本对齐；包家族名对齐主版本号；**破坏性变更只允许发生在主版本之间**
2. Windows ML NuGet 包重构（拆分到 `Microsoft.Windows.AI.MachineLearning` 基础包）
3. Phi Silica API 强制纳入 Limited Access Feature (LAF)
4. **FileSavePicker 行为变更**：用户选择不存在的文件时不再自动创建空文件
5. API 变更：`SystemBackdropHost` → `SystemBackdropElement`；`IXamlPredicate` → `IXamlCondition`
6. 2.3.1：`DISABLE_XAML_GENERATED_MAIN` 行为变更（重命名而非移除生成的 main）
7. 2.2.0 新增 `ApplicationData.GetForUnpackaged()`（unpackaged 应用一等公民数据 API）

## 关键依赖关系

- `DevWinUI` 10.4.1 依赖：`DevWinUI.Base >= 10.4.0`、`Microsoft.Graphics.Win2D >= 1.4.0`、`Microsoft.WindowsAppSDK.WinUI >= 2.3.6` → 要求 WinAppSDK 2.x，与 2.4.0 兼容；也解释了为何两个包必须同批升级
- `Microsoft.WindowsAppSDK` 2.4.0 为聚合包，依赖 `Runtime = 2.4.0` 及 `WinUI >= 2.3.6`、`Base >= 2.0.4` 等子包
- `CommunityToolkit.WinUI.Helpers` 8.2.251219 依赖 `Microsoft.WindowsAppSDK >= 1.6.250108002`（`>=` 约束，2.4.0 满足）
- DevWinUI v10 资源路径变更：`ms-appx:///DevWinUI.Controls/Themes/Generic.xaml` → `ms-appx:///DevWinUI/Themes/Generic.xaml`（本项目无 XAML 引用，不涉及）

## 来源

- Windows App SDK 下载页（Stable 渠道版本列表）：https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads
- Windows App SDK 2.0 release notes（破坏性变更）：https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0
- NuGet：https://www.nuget.org/packages/Microsoft.WindowsAppSDK 、https://www.nuget.org/packages/DevWinUI 、https://www.nuget.org/packages/DevWinUI.Controls 、https://www.nuget.org/packages/CommunityToolkit.WinUI.Helpers 、https://www.nuget.org/packages/Vortice.Direct3D11 、https://www.nuget.org/packages/Markdig 、https://www.nuget.org/packages/System.Drawing.Common 、https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools
- CommunityToolkit/Windows Releases：https://github.com/CommunityToolkit/Windows/releases
