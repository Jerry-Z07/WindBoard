# 技术设计：NuGet 依赖升级到最新稳定版

## 1. 目标版本矩阵（2026-09-09 调研快照，来源见 research.md）

| 包 | 当前版本 | 目标版本 | 变更性质 | 风险 |
|---|---|---|---|---|
| Microsoft.WindowsAppSDK | 1.8.260209005 | **2.4.0**（2026-08-13） | 大版本 1.8→2.x（SemVer 化后第 5 个稳定版） | **高** |
| DevWinUI.Controls | 9.9.4（已弃用、unlisted） | **DevWinUI 10.4.1**（2026-08-28） | 包迁移：Controls 合并进主包 | 中 |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.7705 | 10.0.28000.2705 | 构建期工具 | 低 |
| Markdig | 1.1.1 | 1.3.0 | 次要版本 | 低 |
| System.Drawing.Common | 10.0.3 | 10.0.x 最新补丁 | 补丁 | 低 |
| Vortice.Direct2D1 / Direct3D11 | 3.8.2 | 3.8.3 | 补丁 | 低 |
| CommunityToolkit.WinUI.Helpers / Controls.SettingsControls | 8.2.251219 | 不变（已是最新稳定版） | 无 | — |
| Microsoft.NET.Test.Sdk / xunit / runner / coverlet | 17.14.1 / 2.9.3 / 3.1.4 / 6.0.4 | 实现时以 `dotnet list package --outdated` 确认 | 补丁 | 低 |

## 2. WinAppSDK 1.8 → 2.x 破坏性变更影响分析

官方 2.0 release notes 列出的破坏性/行为变更，逐项对照本项目：

| 官方变更 | 本项目影响评估 |
|---|---|
| 版本方案 SemVer 化；NuGet 版本与 SDK 版本对齐；运行时族变更（2.x Windows App Runtime） | 项目为 `WindowsPackageType=None`（unpackaged），CI 发布未设 `WindowsAppSDKSelfContained`，framework-dependent 变体依赖系统安装的运行时。升级后需 2.x 运行时 → **分发策略问题，本任务不处理，完成后单独报告** |
| FileSavePicker 不再自动创建空文件 | `ExportPickers.PickSaveFileWithOverwriteConfirmAsync` 存在一段针对"预创建空文件"的 `DateCreated` workaround。2.0 下逻辑仍自洽：新文件 `File.Exists=false` 直接返回；覆盖旧文件（创建时间早于打开对话框）弹确认。**需冒烟验证两条保存路径** |
| Windows ML 包重构 / Phi Silica 纳入 LAF | 项目未使用 ML/AI API，无影响 |
| `SystemBackdropHost` → `SystemBackdropElement` | 项目未使用该 API（XAML 中的 `SystemBackdrop` 是通用属性而非该类型），无影响 |
| `IXamlPredicate` → `IXamlCondition` | 项目未使用，无影响 |
| `DISABLE_XAML_GENERATED_MAIN` 行为变更（2.3.1） | 全仓搜索无该属性定义，无影响 |
| 2.2.0 新增 `ApplicationData.GetForUnpackaged()` | 纯新增 API，对本 unpackaged 应用是利好；本任务不启用 |

## 3. DevWinUI.Controls 9.9.4 → DevWinUI 10.4.1 迁移方案

使用面排查结论（影响面极小）：

- **C# 引用**：全仓仅 `AboutSettingsPage.Updates.cs` 一处 `using DevWinUI`（使用 `WindowedContentDialog`）；`WindowedDialogPresentationPlan.cs` 与其测试仅含同名枚举字符串，不依赖 DevWinUI 类型
- **XAML 引用**：无任何 `ms-appx:///DevWinUI.Controls/...` 资源字典合并（v10 的资源路径变更 `DevWinUI.Controls/Themes/Generic.xaml` → `DevWinUI/Themes/Generic.xaml` 不涉及本项目）
- **迁移步骤**：替换 `PackageReference` → 编译验证 `WindowedContentDialog` API 兼容 → 运行验证更新弹窗
- **传递依赖**（自动引入）：`DevWinUI.Base >= 10.4.0`、`Microsoft.Graphics.Win2D >= 1.4.0`、`Microsoft.WindowsAppSDK.WinUI >= 2.3.6`（与 WinAppSDK 2.4.0 主包兼容，主包自身即依赖该子包 >= 2.3.6）

## 4. CommunityToolkit 8.2.251219 与 WinAppSDK 2.4.0 兼容性判定

- 8.2.251219 依赖 `Microsoft.WindowsAppSDK >= 1.6.250108002`（`>=` 约束，2.4.0 满足）
- 8.3 系列仅有 preview（preview2 基于 WinAppSDK 1.8 新包结构），官方仍推荐生产环境使用 8.2 稳定版 → 保持不动
- 残余风险：toolkit 程序集编译于 WinAppSDK 1.x API 表面；2.0 的破坏性变更清单均不在 toolkit（Helpers/SettingsControls）的使用面内 → 判定兼容概率高，以**编译 + 11 个设置页冒烟**兜底验证

## 5. 执行顺序与提交策略

按风险从低到高分批推进，每批独立构建/测试验证，失败可定位到具体批次：

```
基线验证 → 批次A: 测试工程包 → 批次B: 低风险包(Markdig/System.Drawing/Vortice/BuildTools)
        → 批次C: WinAppSDK 2.4.0 + DevWinUI 10.4.1（同批，二者存在依赖耦合）
        → 冒烟验证 → 完成报告
```

批次 C 中 WinAppSDK 与 DevWinUI 必须同批升级：DevWinUI v10 依赖 WinAppSDK.WinUI >= 2.3.6 子包，二者拆开会出现中间不一致状态。

## 6. 回滚方案

- 所有变更仅涉及 `.csproj` 的 `PackageReference` 版本号（及 DevWinUI 包名替换），无数据/格式迁移
- 任一批次失败：`git checkout -- <csproj>` 还原对应文件即可回滚；无其他持久化影响
