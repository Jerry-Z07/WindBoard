# WinUI 平台依赖契约（WinAppSDK / CommunityToolkit，DevWinUI 已移除）

> 记录 2026-09 依赖大版本升级（WinAppSDK 1.8→2.4.0、DevWinUI 9.9.4→10.4.1，同月彻底移除 DevWinUI）中确认的平台行为契约与迁移约定。升级依赖前必读。

---

## 依赖版本约定

- 版本目标一律以升级当日 `dotnet list package --outdated` 实时结果为准，禁止照搬文档快照
- **禁用预发布包**（preview/experimental）：CommunityToolkit 8.3 系列仅有 preview，生产保持 8.2 稳定线（当前 8.2.251219）
- **DevWinUI 已于 2026-09 移除**：唯一使用点 WindowedContentDialog 已被原生 ContentDialog 自适应方案替代（见下文设计决策）；不要再引用 `DevWinUI` / `DevWinUI.Controls`（后者已被官方弃用并 unlist）

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

## 设计决策：更新结果弹窗回归原生 ContentDialog（2026-09 移除 DevWinUI）

**Context**：`ContentDialog` 宿主于 `XamlRoot`，尺寸受窗口约束且模板不自带滚动，默认窗口尺寸下更新日志（两栏布局 MinWidth 980）被截断。曾为此引入 DevWinUI 的 `WindowedContentDialog`（独立窗口承载）。

**Options Considered**：
1. 独立窗口承载（DevWinUI / 自写）——为单一功能引入整包依赖，模态语义、焦点、DPI 全要自管，DevWinUI v10 还整体重写过属性名
2. 应用内 overlay 自绘——需自行处理焦点圈闭 / light-dismiss / 无障碍，风险最高
3. 原生 `ContentDialog` + 内容自适应窗口尺寸 + 内部滚动——模态/焦点/无障碍由原生保证

**Decision**：选 3。约定：
- 滚动区 MaxHeight 与两栏决策由纯决策类型 `UpdateResultDialogLayoutPlanBuilder` 产出（两栏阈值 1060 = 内容 MinWidth 980 + 弹窗左右 chrome ~80；高度 = clamp(窗口高 − 180, 布局区间)），UI 层必须复用其静态方法，**禁止在 UI 层另写一套数值**
- 弹窗内容任何窗口尺寸下不得截断：滚动由内容区 ScrollViewer 承担，单栏外层统一滚动，避免双层滚动条

### Gotcha：XamlRoot 没有 SizeChanged

> **Warning**：WinUI 3 的 `XamlRoot` 不存在 `SizeChanged` 事件（UWP 有）；窗口尺寸变化通知走 `XamlRoot.Changed`。且 `XamlRootChangedEventArgs` 不携带新尺寸（与 UWP 不同）——回调中直接读取 `sender.Size` 当前值即可，非尺寸触发的变更（主题切换等）重算结果幂等。

### Common Mistake：ShowAsync 异常路径的事件订阅泄漏

**Symptom**：弹窗关闭后窗口尺寸变化仍对游离控件回调（COMException / 订阅泄漏）。

**Cause**：`ContentDialog.ShowAsync()` 抛异常（典型：同 XamlRoot 已有其它 ContentDialog 打开）时，`Closed` 事件**不会触发**——仅依赖 `Closed` 退订会悬挂订阅。

**Fix / Prevention**：随弹窗订阅的任何事件（如 `XamlRoot.Changed`）必须在 `finally` 兜底退订，与 `Closed` 路径重复退订是幂等的：

```csharp
dialog.Closed += OnDialogClosed;          // 正常路径退订
xamlRoot.Changed += OnXamlRootChanged;
try
{
    await dialog.ShowAsync();
}
finally
{
    // 兜底：ShowAsync 抛异常时 Closed 不触发
    xamlRoot.Changed -= OnXamlRootChanged;
}
```

## 构建 Gotcha：XAML 错误可能是 C# 错误的级联

> **Warning**：WinUI XAML 编译器（MarkupCompilePass2）依赖前序 C# 编译产出的 LocalAssembly。当 C# 编译失败（如引用了第三方库变更后的 API），XAML 编译会附带大量 `WMC0001: Unknown type 'xxx' in XML namespace` 与 `WMC1509` 警告——这些是级联噪声，不是真实 XAML 错误。

处理顺序：先修 C# 编译错误并重新构建，XAML 错误通常随之消失。不要先去改 XAML 或本地化代码。

## 测试栈约定

- 保持 xUnit v2 技术栈；`xunit.runner.visualstudio` 4.0.0 实测与 xUnit v2 兼容
- xUnit v3 迁移（含 MTP 平台）属独立任务，不在常规依赖升级范围内
