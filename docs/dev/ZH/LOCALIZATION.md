# 本地化（Localization）

本文档约定 WindBoard 的“用户可见文本”如何统一提取到资源字典（`.resx`），为后续多语言扩展做准备。

## 资源位置

- 资源文件（按“语言/功能”拆分）：
  - `WindBoard/Localization/zh-CN/*.resx`（默认语言：简体中文）
  - `WindBoard/Localization/en-US/*.resx`（英文）
- C# 入口：`WindBoard/Localization/L10n.cs`
- XAML 入口：`WindBoard/Localization/LocExtension.cs`

说明：

- 当前提供 `zh-CN`（简体中文，默认）与 `en-US`（英文）。后续新增其它语言时，按相同结构新增语言目录（例如 `ja-JP`）。
- 运行时按 key 的第一个前缀段选择功能资源文件（例如 `Settings_*` -> `Settings.resx`）。
- 缺语言/缺 key 的回退策略：
  - 缺语言：自动回退到默认 `zh-CN`。
  - 缺 key：回退到 `fallback`（若提供）或 key 本身，并通过 `WindBoard.Logging.AppLog` 记录（每个 key 只记录一次）。
  - 部分翻译缺失：若某语言已提供了部分资源文件，但某个 key 仍缺失，则会记录“缺少翻译”并回退到默认语言，便于后续补齐。

## C# 用法

统一使用 `L10n.Get / L10n.Format`：

```csharp
string title = L10n.Get("Common_Settings");
string message = L10n.Format("Export_Completed_File_Fmt", filePath);
```

约束：

- `L10n.Get/Format` 的 key 参数必须是**字符串字面量**，不允许动态拼 key（单测会审计）。

## XAML 用法

在 XAML 根节点引入命名空间：

```xml
xmlns:l10n="using:WindBoard.Localization"
```

然后对用户可见文本统一使用：

```xml
Text="{l10n:Loc Key=Common_Close}"
Header="{l10n:Loc Key=Settings_Dock_ShowUndoRedo_Header}"
```

## 语言切换

在“设置 → 常规 → 语言”中切换应用显示语言：

- 入口页：`WindBoard/Settings/Pages/GeneralSettingsPage.xaml`
- 持久化：`settings.json` → `general.languagePreference`
- 当前可选值：
  - `system`：跟随系统（默认）
  - `<CultureName>`：任意“已提供资源”的语言（例如 `zh-CN` / `en-US` / `ja-JP` 等）

实现要点：

- `L10n` 基于 `CultureInfo.CurrentUICulture` 读取资源；启动时会先加载设置并在创建任何 UI 前应用语言偏好（避免 XAML MarkupExtension 使用旧语言）。
- 设置页运行中切换语言会立即写入设置并尝试应用，但**已加载的界面文本通常不会自动刷新**，因此约定提示用户重启应用后完全生效。

## 新增语言步骤

1. 复制 `WindBoard/Localization/zh-CN/` 为 `WindBoard/Localization/en-US/`（或其它目标语言目录，例如 `ja-JP`，以 `BCP 47` 规范命名）
2. 逐项翻译资源值（Key 不变；文件按功能拆分）
3. 运行 `dotnet test WindBoard.slnx` 确认本地化 Key 审计通过

说明：
- 设置页会从程序包内嵌入的本地化资源里动态枚举“已提供资源”的语言；
- 下拉框显示文案使用 `CultureInfo` 的 `NativeName`（并附带 `CultureName`），因此不需要为每种语言额外新增本地化 key。

验证步骤：
1. 运行 `dotnet test WindBoard.slnx` 确认本地化 Key 审计通过
2. 启动应用 → 打开“设置 → 常规 → 语言”确认新语言已出现并可选择
3. 选择后按提示重启应用，确认界面语言生效

## 测试

- 单测：`WindBoard.Tests/Localization/LocalizationKeyAuditTests.cs`
  - 扫描 `WindBoard/**/*.xaml` 中 `{l10n:Loc Key=...}` 的 key
  - 扫描 `WindBoard/**/*.cs` 中 `L10n.Get/Format("...")` 的 key
  - 声明所有引用到的 key 都存在于默认语言资源（`zh-CN/*.resx`）
