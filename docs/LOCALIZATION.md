# 本地化（Localization）

本文档约定 WindBoard 的“用户可见文本”如何统一提取到资源字典（`.resx`），为后续多语言扩展做准备。

## 资源位置

- 资源文件（按“语言/功能”拆分）：
  - `WindBoard/Localization/zh-CN/*.resx`（默认语言：中文）
  - `WindBoard/Localization/en-US/*.resx`（英文）
- C# 入口：`WindBoard/Localization/L10n.cs`
- XAML 入口：`WindBoard/Localization/LocExtension.cs`

说明：

- 当前提供 `zh-CN`（中文默认）与 `en-US`（英文）。后续新增其它语言时，按相同结构新增语言目录（例如 `ja-JP`）。
- 运行时按 key 的第一个前缀段选择功能资源文件（例如 `Settings_*` -> `Settings.resx`）。
- 缺语言/缺 key 的回退策略（均会记录日志，且会做去重避免刷屏）：
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

## 语言切换（设置 - 常规）

WindBoard 支持在“设置 → 常规 → 语言”中切换应用显示语言：

- 入口页：`WindBoard/Settings/Pages/GeneralSettingsPage.xaml`
- 持久化：`settings.json` → `general.languagePreference`
- 当前可选值：
  - `system`：跟随系统（默认）
  - `zh-CN`：中文
  - `en-US`：English

实现要点：

- `L10n` 基于 `CultureInfo.CurrentUICulture` 读取资源；启动时会先加载设置并在创建任何 UI 前应用语言偏好（避免 XAML MarkupExtension 使用旧文化）。
- 设置页运行中切换语言会立即写入设置并尝试应用，但**已加载的界面文本通常不会自动刷新**，因此约定提示用户重启应用后完全生效。

## 新增语言步骤

### 仅支持“跟随系统”

仅新增资源文件，不改代码。适用于：用户系统语言匹配到该语言时自动生效。

1. 复制 `WindBoard/Localization/zh-CN/` 为 `WindBoard/Localization/en-US/`（或其它目标语言目录，例如 `ja-JP`）
2. 逐项翻译资源值（Key 不变；文件按功能拆分）
3. 运行 `dotnet test WindBoard.slnx` 确认本地化 Key 审计通过

当前不做“运行时切换语言自动刷新 UI”，约定切换语言后需要重启应用（后续如需可引入可绑定的动态资源机制）。

### 让语言出现在“设置 → 常规 → 语言”（可手动选择）

在完成上一节“仅支持跟随系统”的资源新增后，还需要把该语言加入“语言偏好”的枚举与 UI 下拉框。

以新增 `ja-JP`（日语）为例：

1. 扩展语言偏好枚举/解析
   - 修改 `WindBoard/Settings/AppLanguagePreference.cs`
   - 在 `AppLanguagePreference` 中新增枚举值（例如 `Japanese`）
   - 在 `AppLanguagePreferenceParser` 中新增常量与解析分支，并确保 `ToSettingValue` 返回**唯一且稳定**的设置值（建议直接用 CultureName，例如 `ja-JP`）

2. 扩展语言应用逻辑
   - 修改 `WindBoard/Settings/AppLanguageService.cs`
   - 在 `Apply` 中增加新语言分支：把偏好映射到 `CultureInfo` 与 `Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride`（通常使用同一个 CultureName，例如 `ja-JP`）

3. 扩展设置页下拉框选项
   - 修改 `WindBoard/Settings/Pages/GeneralSettingsPage.xaml`
   - 为 `LanguageComboBox` 增加一项 `ComboBoxItem`，并让 `Tag` 与解析器输出一致（例如 `Tag="ja-JP"`）

4. 增加默认语言资源（下拉框显示文案）
   - 修改 `WindBoard/Localization/zh-CN/Settings.resx`
   - 新增一个 key（例如 `Settings_General_Language_Japanese`），用于对应新增的下拉框项 `Content`
   - 注意：本仓库的本地化 Key 审计以默认语言 `zh-CN` 为准，**XAML 引用到的新 key 必须在 `zh-CN/*.resx` 中存在**，否则单测会失败

5. 更新单元测试（建议）
   - 修改 `WindBoard.Tests/Settings/AppLanguagePreferenceParserTests.cs`：新增 `InlineData("ja-JP", ...)` 等用例
   - 修改 `WindBoard.Tests/Settings/AppSettingsStoreTests.cs`：新增归一化用例，确保 `general.languagePreference` 能被识别并写回规范值

6. 运行验证
   - `dotnet test WindBoard.slnx -p:Platform=x64`

## 质量门禁（防漏 Key）

- 单测：`WindBoard.Tests/Localization/LocalizationKeyAuditTests.cs`
  - 扫描 `WindBoard/**/*.xaml` 中 `{l10n:Loc Key=...}` 的 key
  - 扫描 `WindBoard/**/*.cs` 中 `L10n.Get/Format("...")` 的 key
  - 断言所有引用到的 key 都存在于默认语言资源（`zh-CN/*.resx`）
