# WinUI 平台依赖契约（WinAppSDK / DevWinUI / CommunityToolkit）

> 记录 2026-09 依赖大版本升级（WinAppSDK 1.8→2.4.0、DevWinUI 9.9.4→10.4.1）中确认的平台行为契约与迁移约定。升级依赖前必读。

---

## 依赖版本约定

- 版本目标一律以升级当日 `dotnet list package --outdated` 实时结果为准，禁止照搬文档快照
- **禁用预发布包**（preview/experimental）：CommunityToolkit 8.3 系列仅有 preview，生产保持 8.2 稳定线（当前 8.2.251219）
- `DevWinUI.Controls` 已被官方弃用并 unlist，v10 起合并进 `DevWinUI` 主包（旧 `DevWinUI` → `DevWinUI.Base`）；不要再引用 `DevWinUI.Controls`
- `DevWinUI` v10 依赖 `Microsoft.WindowsAppSDK.WinUI >= 2.3.6` 子包：**WinAppSDK 与 DevWinUI 必须同批升级**，拆开会出现中间不一致状态

## WinAppSDK 2.x 平台行为契约

- 版本方案 SemVer 化：NuGet 包版本 = SDK 版本（如 2.4.0）；破坏性变更只允许发生在主版本之间
- **FileSavePicker 不再预创建空文件**（2.0 起）：用户新输入文件名时 `File.Exists(file.Path)` 为 false

### Don't: 保留 FileSavePicker 预创建 workaround

```csharp
// Don't：WinAppSDK 1.x 时代的 DateCreated 时间窗口判断，2.0 下分支不可达（死代码）
DateTimeOffset pickStarted = DateTimeOffset.Now;
StorageFile? file = await picker.PickSaveFileAsync();
if (file.DateCreated >= pickStarted - TimeSpan.FromSeconds(2)) { return file; }
```

```csharp
// Correct：以 File.Exists 为唯一权威判断（本项目约定写法，见 ExportPickers.PickSaveFileWithOverwriteConfirmAsync）
StorageFile? file = await PickSaveFileAsync(xamlRoot, hwnd, format);
if (file is null) { return null; }
if (!File.Exists(file.Path)) { return file; }   // 新文件直接返回
bool overwrite = await ConfirmOverwriteFileAsync(xamlRoot, file.Path);  // 已存在弹覆盖确认
```

> **Warning**：本项目为 `WindowsPackageType=None`（unpackaged）且 CI 发布未设 `WindowsAppSDKSelfContained`。升级 WinAppSDK 主版本后，framework-dependent 变体要求用户机器安装对应大版本的 Windows App Runtime（2.x）——升级主版本时必须单独评估运行时分发策略。

## DevWinUI v10 WindowedContentDialog API 映射

v10 完全重写该控件（移植自 SuGarToolkit），本项目唯一使用点在 `AboutSettingsPage.Updates.cs`（关于页"检查更新"弹窗）。属性映射契约：

| v9（旧） | v10（新） | 说明 |
|---|---|---|
| `Title` / `WindowTitle` | `Header` | 标题合并为单一属性 |
| `PrimaryButtonText` | `PrimaryButtonContent` | 类型 object?，语义同 ContentDialog |
| `CloseButtonText` | `CloseButtonContent` | 同上 |
| `OwnerWindow` | `Owner`（Window?） | CLR 属性，非 DP |
| `IsResizable` | `CanResize` | |
| `ContentMinWidth` | `MinWidth` | 窗口级最小宽度 |
| `CenterInParent` | （移除） | 窗口默认居中于属主 |
| `RequestedTheme` | （移除） | 类基类变为 DependencyObject；主题跟随系统 |
| `ShowAsync()` | `ShowAsync()` | 签名不变，仍返回 `Task<ContentDialogResult>` |

- `HasTitleBar` 在 v10 仍存在（partial 定义），可继续使用
- 上游已知问题：`PrimaryButtonTemplate` 属性注册无 PropertyChanged 回调（未转发到内部视图），避免依赖该属性

## 构建 Gotcha：XAML 错误可能是 C# 错误的级联

> **Warning**：WinUI XAML 编译器（MarkupCompilePass2）依赖前序 C# 编译产出的 LocalAssembly。当 C# 编译失败（如引用了第三方库变更后的 API），XAML 编译会附带大量 `WMC0001: Unknown type 'xxx' in XML namespace` 与 `WMC1509` 警告——这些是级联噪声，不是真实 XAML 错误。

处理顺序：先修 C# 编译错误并重新构建，XAML 错误通常随之消失。不要先去改 XAML 或本地化代码。

## 测试栈约定

- 保持 xUnit v2 技术栈；`xunit.runner.visualstudio` 4.0.0 实测与 xUnit v2 兼容
- xUnit v3 迁移（含 MTP 平台）属独立任务，不在常规依赖升级范围内
