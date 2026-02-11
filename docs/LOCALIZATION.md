# 本地化（Localization）

本文档约定 WindBoard 的“用户可见文本”如何统一提取到资源字典（`.resx`），为后续多语言扩展做准备。

## 资源位置

- 资源文件：`WindBoard/Localization/Strings.resx`（默认/invariant：当前填中文）
- C# 入口：`WindBoard/Localization/L10n.cs`
- XAML 入口：`WindBoard/Localization/LocExtension.cs`

说明：

- 当前仅提供 `Strings.resx`（中文默认）。后续新增其它语言时，增加 `Strings.en-US.resx`、`Strings.ja-JP.resx` 等即可。
- 缺语言/缺 key 的回退策略：
  - 缺语言：自动回退到默认 `Strings.resx`（invariant）。
  - 缺 key：回退到 `fallback`（若提供）或 key 本身，并在 Debug 输出中记录（每个 key 只记录一次）。

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

## 新增语言步骤

1. 复制 `WindBoard/Localization/Strings.resx` 为 `WindBoard/Localization/Strings.en-US.resx`
2. 逐项翻译资源值（Key 不变）
3. 运行 `dotnet test WindBoard.slnx` 确认本地化 Key 审计通过

当前不做“运行时切换语言自动刷新 UI”，约定切换语言后需要重启应用（后续如需可引入可绑定的动态资源机制）。

## 质量门禁（防漏 Key）

- 单测：`WindBoard.Tests/Localization/LocalizationKeyAuditTests.cs`
  - 扫描 `WindBoard/**/*.xaml` 中 `{l10n:Loc Key=...}` 的 key
  - 扫描 `WindBoard/**/*.cs` 中 `L10n.Get/Format("...")` 的 key
  - 断言所有引用到的 key 都存在于 `Strings.resx`

