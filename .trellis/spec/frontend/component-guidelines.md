# Component Guidelines

> How components are built in this project.

---

## Overview

WindBoard does not use MVVM, DI containers, or ViewModel binding. UI components use the direct code-behind control pattern - event handling lives in code-behind, business logic is delegated to Services, and state is accessed through static singleton services.

---

## Component Structure

### Standard UserControl structure

```
ControlName.xaml          - XAML layout
ControlName.xaml.cs       - main body: fields, properties, initialization, event subscription/unsubscription, Dispose
ControlName.{Feature}.cs  - partial split (by feature area)
```

**Key conventions**:
- The main `.xaml.cs` file owns all field and property declarations
- Partial files contain methods only (no independent field declarations, except for a few drag-state fields)
- All partial files share the same `public sealed partial class` declaration

### XAML page structure

```
FeaturePage.xaml          - Page layout (settings page/content page)
FeaturePage.xaml.cs       - code-behind + ViewModel (embedded in the same file)
FeatureDialog.xaml        - ContentDialog layout (modal dialog)
FeatureDialog.xaml.cs     - code-behind + internal classes
FeatureWindow.xaml        - Window layout (standalone window)
FeatureWindow.xaml.cs     - code-behind
```

---

## Event Handling Patterns

### Pattern 1: XAML-declared event binding

```xml
<Button Click="OnSelectionBringToFrontClicked" />
<ToggleSwitch Toggled="OnEnabledToggled" />
<TextBox TextChanged="OnTitleTextChanged" />
```

### Pattern 2: Dynamic binding in code

```csharp
// In the MainWindow constructor
BoardCanvas.CommandStateChanged += (_, _) => UpdateCommandStates();
SelectToolToggleButton.Click += (_, _) => ApplyToolSelection(BoardTool.Select);
```

### Pattern 3: AddHandler for already-handled events

```csharp
// Still receive the event after InputController marks it as Handled
CanvasPanel.AddHandler(UIElement.PointerMovedEvent, _cursorPointerMovedHandler, true);
```

### Pattern 4: `_isSyncingFromSettings` reentrancy guard

```csharp
// Almost every settings page uses this pattern
private bool _isSyncingFromSettings;

private void OnEnabledToggled(object sender, RoutedEventArgs e)
{
    if (_isSyncingFromSettings) return;
    AppSettingsService.Instance.Update(s => s.General.Camouflage.Enabled = enabled);
}
```

---

## Control Communication

### Event-driven (C# events)

```csharp
// State change notifications
BoardCanvas.CommandStateChanged -> MainWindow subscribes and updates button states
BoardSession.StateChanged -> BoardCanvasControl subscribes and triggers redraw
AppSettingsService.Instance.Changed -> each settings page subscribes to sync the UI
```

### Direct method calls

```csharp
MainWindow -> BoardCanvas.Tool = ... / BoardCanvas.Undo()
BoardCanvas -> _input.CancelActiveToolOperation() / _session.Execute()
```

### Singleton service + callback/closure

```csharp
// Lambda write-back
AppSettingsService.Instance.Update(s => s.Dock.IsUndoRedoVisible = isVisible);
// Pass a closure when constructing Flow
new ExportFlow(_workspace, getViewportState: () => BoardCanvas.GetViewportState(...));
```

### Host object bridge

```csharp
// MainWindow builds a Host and passes UI element references to the Feature Flow
var host = new DockMainWindowHost(this, panel, button);
_dockFlow = new DockFlow(host, ...);
```

---

## Localization in Components

### In XAML

```xml
xmlns:l10n="using:WindBoard.Localization"

<TextBlock Text="{l10n:Loc Key=Settings_Dock_Title}" />
<Button Content="{l10n:Loc Key=Common_SelectEllipsis}" />
<ContentDialog Title="{l10n:Loc Key=Common_ConfirmOverwrite_Title}"
               PrimaryButtonText="{l10n:Loc Key=Common_Overwrite}" />
<ToolTipService.ToolTip="{l10n:Loc Key=Some_Tooltip}" />
```

### In C#

```csharp
L10n.Get("Common_BringToFront")
L10n.Format("Settings_Camouflage_CreateShortcut_Success_Fmt", shortcutPath)
```

**Key naming convention**: `Domain_SubItem` (for example `Settings_Dock_Title`, `Common_Delete`, `Import_Failed_Title`)

**Resource storage and build conventions**:

- Resource source files live under `WindBoard/Strings/<culture>/<Feature>.resw`
- `<Feature>` must match the first segment of the key prefix; for example `Settings_Dock_Title` must live in `Strings/<culture>/Settings.resw`
- Do not use `x:Uid` directly in XAML; keep `{l10n:Loc Key=...}` and let `Localization/L10n.cs` read `WindBoard.pri` at runtime
- After adding a new language or a new feature resource, the build automatically refreshes the available language/feature metadata through `Build/GenerateLocalizationMetadata.ps1`

**Validation points**:

- Good: add `Strings/ja-JP/Settings.resw` with `Settings_*` keys, and `ja-JP` appears automatically in the settings-page language picker
- Base: after adding `Settings_NewOption_Title`, the same key exists in `Settings.resw`, and `LocalizationKeyAuditTests` passes
- Bad: put `Settings_*` keys into `Common.resw`; runtime routing will fail by feature and fall back to the key/fallback
- Must run: `dotnet build WindBoard.slnx -c Release` and `dotnet test WindBoard.slnx -c Release`

---

## WinUI Best Practices

### Recommended (from winui-app skill)

- Use native `CommandBar` or other standard WinUI command surfaces instead of inventing a custom toolbar
- Prefer composing/restyling built-in WinUI controls first, then CommunityToolkit, and only then custom controls
- Use native WinUI implementations for component and UI interactions; do not build painted substitutes for ordinary controls such as buttons, back buttons, or sidebar toggle buttons
- When native controls such as `TitleBar`, `NavigationView`, `CommandBar`, or `Button` already provide the required capability, use the built-in capability directly instead of wrapping it in another "native-like" implementation
- Support Light/Dark themes by default and use theme-aware resources and system brushes
- Use `x:Bind` to improve compile-time safety and performance
- Keep the visual tree simple and avoid deep XAML nesting

### Native control convention

**What**: ordinary interaction components should default to WinUI native controls or built-in capabilities and should not use painting to mimic existing system controls.

**Why**: native controls automatically get system interactions, theming, accessibility, title-bar integration, and future platform behavior updates; painted "native-like" buttons easily drift from system behavior in size, borders, states, focus feedback, and accessibility.

**Example**:

```xaml
<!-- Wrong: native TitleBar already provides BackButton/PaneToggleButton, but two buttons are still added manually -->
<TitleBar>
    <TitleBar.LeftHeader>
        <StackPanel Orientation="Horizontal">
            <Button>
                <SymbolIcon Symbol="Back" />
            </Button>
            <Button>
                <FontIcon Glyph="&#xE700;" />
            </Button>
        </StackPanel>
    </TitleBar.LeftHeader>
</TitleBar>

<!-- Correct: use the built-in TitleBar buttons and handle only the events -->
<TitleBar
    IsBackButtonVisible="True"
    IsPaneToggleButtonVisible="True"
    BackRequested="OnTitleBarBackRequested"
    PaneToggleRequested="OnTitleBarPaneToggleRequested" />
```

### Convention: shared ResourceDictionary for settings pages

**What**: when multiple settings pages need the same spacing, padding, or card-grid rhythm, put the constants and base container styles in `WindBoard/Settings/SettingsPageResources.xaml` and merge them once through `WindBoard/App.xaml`'s `MergedDictionaries`.

**Why**: when settings-page constants are scattered across XAML pages, one visual adjustment turns into a multi-file edit, which is easy to miss and leads to density drift. A shared ResourceDictionary turns a single visual decision into a single configuration point.

**Current contract**:

- Resource file path: `WindBoard/Settings/SettingsPageResources.xaml`
- Merge entry: `WindBoard/App.xaml`
- Current shared keys: `SettingsCardGroupSpacing`, `SettingsPagePadding`
- Current shared styles: `SettingsPageRootStackPanelStyle`, `SettingsPageSectionStackPanelStyle`, `SettingsPageCardGridStyle`

**Use when**:

- 3 or more settings pages need the same visual constants
- The reuse is layout rhythm, not single-page private form layout
- The target containers are the settings-page root `StackPanel`, a grouped `StackPanel`, or a card list `Grid`

**Example**:

```xaml
<!-- WindBoard/App.xaml -->
<ResourceDictionary.MergedDictionaries>
    <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
    <ResourceDictionary Source="ms-appx:///Settings/SettingsPageResources.xaml" />
</ResourceDictionary.MergedDictionaries>

<!-- Settings page root -->
<StackPanel Style="{StaticResource SettingsPageRootStackPanelStyle}">
    <StackPanel Style="{StaticResource SettingsPageSectionStackPanelStyle}">
        <TextBlock Style="{ThemeResource SubtitleTextBlockStyle}" Text="Section" />
        <controls:SettingsCard Header="Example" />
    </StackPanel>

    <Grid Style="{StaticResource SettingsPageCardGridStyle}">
        <controls:SettingsCard Header="Left" />
        <controls:SettingsCard Grid.Column="1" Header="Right" />
    </Grid>
</StackPanel>
```

**Don't**:

- Do not force single-page private preview areas or dialog content spacing into this dictionary
- Do not create global keys for a single page or a local control
- Do not repeat shared layout constants such as `Spacing="4" Padding="24"` across multiple settings pages

### Avoid (from winui-app skill + deslop skill)

- Scattered theme brushes and styles (they should be centralized in App.xaml or a shared ResourceDictionary)
- Unnecessary `Border` wrapping ("double card" anti-pattern)
- Hard-coded color values (theme resources should be used)
- Using painted `Button`, `Border`, `Path`, and similar elements to simulate the look and behavior of existing native controls
- Overly defensive checks, such as adding null checks on already verified internal call paths
- Extra AI-generated comments (comments should explain "why", not repeat code meaning)

---

## Common Mistakes

### ❌ DON'T
- Write business logic directly in code-behind (delegate to Services/)
- Use MVVM binding or the INotifyPropertyChanged pattern
- Hard-code user-visible strings (must use `{l10n:Loc Key=...}` or `L10n.Get()`)
- Use `Binding` in XAML when `x:Bind` is available
- Forget the `_isSyncingFromSettings` reentrancy guard (almost every settings page needs it)
- Replace system buttons/title-bar buttons/navigation buttons with painted components when native control capability already exists

### ✅ DO
- Handle events in code-behind and keep business logic in Services
- Prefer `x:Bind` over `Binding`
- Modify settings through `AppSettingsService.Instance.Update()`
- New Features follow the unified Flow + Models + Services + UI structure
- UI operations in crash paths must be wrapped in try-catch
- Confirm whether native WinUI controls already satisfy the requirement before deciding to restyle or add extra controls
