using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Features.Camouflage.UI;
using WindBoard.Features.Dock.UI;
using WindBoard.Features.Shortcuts.UI;
using WindBoard.Localization;
using WindBoard.Settings.Pages;

namespace WindBoard.Settings
{
    public sealed partial class SettingsWindow : Window
    {
        private static readonly SettingsRootDefinition[] RootDefinitions =
        [
            new("general", typeof(GeneralSettingsPage), static () => L10n.Get("Settings_General_Title")),
            new("appearance", typeof(AppearanceSettingsPage), static () => L10n.Get("Settings_Appearance_Title")),
            new("writing", typeof(WritingSettingsPage), static () => L10n.Get("Settings_Writing_Title")),
            new("shortcuts", typeof(ShortcutsSettingsPage), static () => L10n.Get("Settings_Shortcuts_Title")),
            new("debug", typeof(DebugSettingsPage), static () => L10n.Get("Settings_Debug_Title")),
            new("about", typeof(AboutSettingsPage), static () => L10n.Get("Settings_About_Title")),
        ];

        private static readonly SearchTargetDefinition[] SearchTargetDefinitions =
        [
            new("general", null, static () => L10n.Get("Settings_General_Title")),
            new("general", null, static () => L10n.Get("Settings_General_Language_Title"), static () => L10n.Get("Settings_General_Language_Description"), "LanguageComboBox"),
            new("general", null, static () => L10n.Get("Settings_General_StartupWindowMode_Title"), static () => L10n.Get("Settings_General_StartupWindowMode_Description"), "StartupWindowModeComboBox"),
            new("general", null, static () => L10n.Get("Settings_General_EnterScreenAnnotationWhenMinimized_Title"), static () => L10n.Get("Settings_General_EnterScreenAnnotationWhenMinimized_Description"), "EnterScreenAnnotationWhenMinimizedToggleSwitch"),
            new("general", typeof(CamouflageSettingsPage), static () => L10n.Get("Settings_Camouflage_Title"), static () => L10n.Get("Settings_Camouflage_Description"), "EnabledToggleSwitch"),

            new("appearance", null, static () => L10n.Get("Settings_Appearance_Title")),
            new("appearance", null, static () => L10n.Get("Settings_Background_CanvasBackgroundColor_Title"), static () => L10n.Get("Settings_Background_CanvasBackgroundColor_Description"), "CanvasBackgroundCard"),
            new("appearance", null, static () => L10n.Get("Settings_Appearance_ElementCardTheme_Title"), static () => L10n.Get("Settings_Appearance_ElementCardTheme_Description"), "ElementCardThemeCard"),
            new("appearance", typeof(DockSettingsPage), static () => L10n.Get("Settings_Dock_Title"), static () => L10n.Get("Settings_Dock_Description"), "UndoRedoVisibleToggleSwitch"),

            new("writing", null, static () => L10n.Get("Settings_Writing_Title")),
            new("writing", typeof(PenSettingsPage), static () => L10n.Get("Settings_Pen_Title"), static () => L10n.Get("Settings_Writing_Pen_Description"), "PaletteCountNumberBox"),

            new("shortcuts", null, static () => L10n.Get("Settings_Shortcuts_Title"), static () => L10n.Get("Settings_Shortcuts_Description")),
            new("shortcuts", null, static () => L10n.Get("Settings_Shortcuts_ConflictReminder_Header"), null, "ConflictReminderToggleSwitch"),
            new("shortcuts", null, static () => L10n.Get("Settings_Shortcuts_Undo_Title"), null, "UndoShortcutCard"),
            new("shortcuts", null, static () => L10n.Get("Settings_Shortcuts_Redo_Title"), null, "RedoShortcutCard"),

            new("about", null, static () => L10n.Get("Settings_About_Title")),
            new("about", typeof(SettingsManagementPage), static () => L10n.Get("Settings_About_SettingsManagement_SectionTitle"), static () => L10n.Get("Settings_About_SettingsManagement_Entry_Description"), "ExportSettingsCard"),
            new("about", null, static () => L10n.Get("Settings_About_AutoCheckUpdates"), null, "AutoCheckUpdatesComboBox"),
            new("about", null, static () => L10n.Get("Updates_DownloadSource_Title"), null, "DownloadSourceComboBox"),
            new("about", null, static () => L10n.Get("Settings_About_CheckUpdates"), null, "CheckUpdatesButton"),
        ];

        private static readonly SearchTargetDefinition[] DebugSearchTargetDefinitions =
        [
            new("debug", null, static () => L10n.Get("Settings_Debug_Title")),
            new("debug", null, static () => L10n.Get("Settings_Debug_OpenLogDir_Title"), static () => L10n.Get("Settings_Debug_OpenLogDir_Description")),
            new("debug", null, static () => L10n.Get("Settings_Debug_OpenSettingsDir_Title"), static () => L10n.Get("Settings_Debug_OpenSettingsDir_Description")),
            new("debug", null, static () => L10n.Get("Settings_Debug_SendTestToast_Title"), static () => L10n.Get("Settings_Debug_SendTestToast_Description")),
        ];

        private static readonly Dictionary<string, SettingsRootDefinition> RootDefinitionsByTag = CreateRootDefinitionsByTag();
        private static readonly Dictionary<Type, Func<string>> PageTitleProviders = CreatePageTitleProviders();

        private readonly List<SettingsSearchTarget> _searchTargets = new();
        private readonly List<SettingsSearchTarget> _filteredSearchTargets = new();
        private AppWindowTitleBar? _appWindowTitleBar;
        private string? _pendingBringIntoViewElementName;
        private Type? _pendingBringIntoViewPageType;

        internal static SettingsWindow? Active { get; private set; }

        internal IntPtr Hwnd
        {
            get
            {
                try
                {
                    return WinRT.Interop.WindowNative.GetWindowHandle(this);
                }
                catch
                {
                    return IntPtr.Zero;
                }
            }
        }

        public SettingsWindow()
        {
            InitializeComponent();

            ConfigureTitleBar();
            RebuildSearchTargets();
            UpdateCurrentPageTitle();

            Active = this;
            Closed += (_, _) =>
            {
                if (ReferenceEquals(Active, this))
                {
                    Active = null;
                }

                DebugToolsGate.Changed -= OnDebugToolsGateChanged;
            };

            DebugToolsGate.Changed += OnDebugToolsGateChanged;
            UpdateDebugNavItemVisibility();

            // 首次打开时默认进入“常规”。
            NavView.Loaded += (_, _) =>
            {
                if (NavView.SelectedItem is null && NavView.MenuItems.Count > 0)
                {
                    NavView.SelectedItem = NavView.MenuItems[0];
                }

                if (NavView.SelectedItem is NavigationViewItem item)
                {
                    NavigateFromTag(item.Tag as string);
                }
            };

        }

        private void ConfigureTitleBar()
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            _appWindowTitleBar = AppWindow.TitleBar;
            _appWindowTitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            _appWindowTitleBar.ButtonBackgroundColor = Colors.Transparent;
            _appWindowTitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }

        private void OnTitleBarPaneToggleRequested(TitleBar sender, object args)
        {
            bool shouldExpand = NavView.PaneDisplayMode != NavigationViewPaneDisplayMode.Left || !NavView.IsPaneOpen;
            NavView.PaneDisplayMode = shouldExpand
                ? NavigationViewPaneDisplayMode.Left
                : NavigationViewPaneDisplayMode.LeftCompact;
            NavView.IsPaneOpen = shouldExpand;
        }

        private void OnTitleBarBackRequested(TitleBar sender, object args)
        {
            TryGoBack();
        }

        private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer is not NavigationViewItem item)
            {
                return;
            }

            NavigateFromTag(item.Tag as string);
        }

        private void OnContentFrameNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            UpdateBackButtonState();
            UpdateCurrentPageTitle();
            TryBringPendingElementIntoView();
        }

        private void NavigateFromTag(string? tag)
        {
            // 顶层分类切换：根据 Tag 导航，并清空二级页面的返回栈。
            Type pageType = GetPageTypeFromTag(tag);

            if (ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType);
            }

            // 切换顶层分类时，重置二级页面历史。
            if (ContentFrame.BackStack.Count > 0)
            {
                ContentFrame.BackStack.Clear();
            }

            UpdateBackButtonState();
        }

        private static Type GetPageTypeFromTag(string? tag)
        {
            if (!string.IsNullOrWhiteSpace(tag) && RootDefinitionsByTag.TryGetValue(tag, out SettingsRootDefinition? definition))
            {
                return definition.PageType;
            }

            return typeof(GeneralSettingsPage);
        }

        private void OnDebugToolsGateChanged(object? sender, EventArgs e)
        {
            // Gate 事件可能来自非 UI 线程，这里统一切回 UI 线程更新。
            if (!DispatcherQueue.TryEnqueue(UpdateDebugNavItemVisibility))
            {
                UpdateDebugNavItemVisibility();
            }
        }

        private void UpdateDebugNavItemVisibility()
        {
            if (DebugNavItem is null)
            {
                return;
            }

            bool visible = DebugToolsGate.IsVisible;
            DebugNavItem.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            RebuildSearchTargets();
            RefreshSearchSuggestions(SettingsSearchBox.Text);

            // 防御：入口被隐藏时，如果用户仍停留在调试页，自动回到“常规”。
            if (!visible && ContentFrame.CurrentSourcePageType == typeof(DebugSettingsPage))
            {
                try
                {
                    if (NavView.MenuItems.Count > 0)
                    {
                        NavView.SelectedItem = NavView.MenuItems[0];
                    }

                    NavigateFromTag("general");
                }
                catch
                {
                    // 忽略导航失败：不阻断设置窗口使用。
                }
            }
        }

        private void UpdateBackButtonState()
        {
            bool canGoBack = ContentFrame.CanGoBack;
            NavView.IsBackEnabled = canGoBack;
            AppTitleBar.IsBackButtonVisible = canGoBack;
            AppTitleBar.IsBackButtonEnabled = canGoBack;
        }

        private bool TryGoBack()
        {
            if (!ContentFrame.CanGoBack)
            {
                return false;
            }

            ContentFrame.GoBack();
            return true;
        }

        private void UpdateCurrentPageTitle()
        {
            string pageTitle = GetPageTitle(ContentFrame.CurrentSourcePageType);
            if (string.IsNullOrWhiteSpace(pageTitle))
            {
                pageTitle = L10n.Get("Common_Settings");
            }

            AppTitleBar.Title = pageTitle;
            Title = $"{L10n.Get("Common_Settings")} - {pageTitle}";
        }

        private static string GetPageTitle(Type? pageType)
        {
            if (pageType is not null && PageTitleProviders.TryGetValue(pageType, out Func<string>? titleProvider))
            {
                return titleProvider();
            }

            return string.Empty;
        }

        private static string GetRootTitle(string rootTag)
        {
            if (RootDefinitionsByTag.TryGetValue(rootTag, out SettingsRootDefinition? definition))
            {
                return definition.TitleProvider();
            }

            return L10n.Get("Common_Settings");
        }

        private void RebuildSearchTargets()
        {
            _searchTargets.Clear();

            foreach (SearchTargetDefinition definition in SearchTargetDefinitions)
            {
                AddSearchTarget(definition);
            }

            if (DebugToolsGate.IsVisible)
            {
                foreach (SearchTargetDefinition definition in DebugSearchTargetDefinitions)
                {
                    AddSearchTarget(definition);
                }
            }
        }

        private void AddSearchTarget(SearchTargetDefinition definition)
        {
            string rootTitle = GetRootTitle(definition.RootTag);
            string title = definition.TitleProvider();
            string displayText = string.Equals(rootTitle, title, StringComparison.Ordinal)
                ? title
                : $"{rootTitle} / {title}";

            string searchText = displayText;
            if (definition.DescriptionProvider is not null)
            {
                searchText = $"{searchText} {definition.DescriptionProvider()}";
            }

            _searchTargets.Add(new SettingsSearchTarget(displayText, searchText, definition.RootTag, definition.DetailPageType, definition.FocusElementName));
        }

        private void OnSettingsSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                return;
            }

            RefreshSearchSuggestions(sender.Text);
        }

        private void RefreshSearchSuggestions(string? query)
        {
            _filteredSearchTargets.Clear();
            SettingsSearchBox.ItemsSource = null;

            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            foreach (SettingsSearchTarget target in _searchTargets)
            {
                if (!target.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _filteredSearchTargets.Add(target);
                if (_filteredSearchTargets.Count >= 8)
                {
                    break;
                }
            }

            if (_filteredSearchTargets.Count == 0)
            {
                return;
            }

            List<string> suggestions = new(_filteredSearchTargets.Count);
            foreach (SettingsSearchTarget target in _filteredSearchTargets)
            {
                suggestions.Add(target.DisplayText);
            }

            SettingsSearchBox.ItemsSource = suggestions;
        }

        private void OnSettingsSearchSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is not string displayText)
            {
                return;
            }

            SettingsSearchTarget? target = FindSearchTarget(displayText);
            if (target is null)
            {
                return;
            }

            sender.Text = target.DisplayText;
        }

        private void OnSettingsSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            SettingsSearchTarget? target = null;

            if (args.ChosenSuggestion is string displayText)
            {
                target = FindSearchTarget(displayText);
            }

            target ??= _filteredSearchTargets.Count > 0 ? _filteredSearchTargets[0] : null;
            if (target is null)
            {
                return;
            }

            sender.Text = target.DisplayText;
            NavigateToSearchTarget(target);
        }

        private SettingsSearchTarget? FindSearchTarget(string displayText)
        {
            foreach (SettingsSearchTarget target in _searchTargets)
            {
                if (string.Equals(target.DisplayText, displayText, StringComparison.Ordinal))
                {
                    return target;
                }
            }

            return null;
        }

        private void NavigateToSearchTarget(SettingsSearchTarget target)
        {
            Type targetPageType = target.DetailPageType ?? GetPageTypeFromTag(target.RootTag);
            _pendingBringIntoViewPageType = targetPageType;
            _pendingBringIntoViewElementName = target.FocusElementName;

            NavigationViewItem? rootItem = FindNavigationItemByTag(target.RootTag);
            if (rootItem is not null && !ReferenceEquals(NavView.SelectedItem, rootItem))
            {
                NavView.SelectedItem = rootItem;
            }
            else
            {
                NavigateFromTag(target.RootTag);
            }

            if (target.DetailPageType is not null && ContentFrame.CurrentSourcePageType != target.DetailPageType)
            {
                ContentFrame.Navigate(target.DetailPageType);
            }

            TryBringPendingElementIntoView();
        }

        private NavigationViewItem? FindNavigationItemByTag(string rootTag)
        {
            foreach (object item in NavView.MenuItems)
            {
                if (item is NavigationViewItem navItem && string.Equals(navItem.Tag as string, rootTag, StringComparison.Ordinal))
                {
                    return navItem;
                }
            }

            foreach (object item in NavView.FooterMenuItems)
            {
                if (item is NavigationViewItem navItem && string.Equals(navItem.Tag as string, rootTag, StringComparison.Ordinal))
                {
                    return navItem;
                }
            }

            return null;
        }

        private void TryBringPendingElementIntoView()
        {
            if (_pendingBringIntoViewPageType is null || _pendingBringIntoViewPageType != ContentFrame.CurrentSourcePageType)
            {
                return;
            }

            string? elementName = _pendingBringIntoViewElementName;
            _pendingBringIntoViewPageType = null;
            _pendingBringIntoViewElementName = null;

            if (string.IsNullOrWhiteSpace(elementName))
            {
                return;
            }

            if (ContentFrame.Content is FrameworkElement page
                && page.FindName(elementName) is UIElement targetElement)
            {
                targetElement.StartBringIntoView();
            }
        }

        private static Dictionary<string, SettingsRootDefinition> CreateRootDefinitionsByTag()
        {
            Dictionary<string, SettingsRootDefinition> definitions = new(StringComparer.Ordinal);
            foreach (SettingsRootDefinition definition in RootDefinitions)
            {
                if (!definitions.TryAdd(definition.Tag, definition))
                {
                    throw new InvalidOperationException($"Settings 根节点 Tag 重复：{definition.Tag}");
                }
            }

            return definitions;
        }

        private static Dictionary<Type, Func<string>> CreatePageTitleProviders()
        {
            Dictionary<Type, Func<string>> providers = new()
            {
                [typeof(CamouflageSettingsPage)] = static () => L10n.Get("Settings_Camouflage_Title"),
                [typeof(DockSettingsPage)] = static () => L10n.Get("Settings_Dock_Title"),
                [typeof(PenSettingsPage)] = static () => L10n.Get("Settings_Pen_Title"),
                [typeof(SettingsManagementPage)] = static () => L10n.Get("Settings_About_SettingsManagement_SectionTitle"),
            };

            foreach (SettingsRootDefinition definition in RootDefinitions)
            {
                if (!providers.TryAdd(definition.PageType, definition.TitleProvider))
                {
                    throw new InvalidOperationException($"Settings 页面标题提供器重复：{definition.PageType.FullName}");
                }
            }

            return providers;
        }

        private sealed class SettingsRootDefinition
        {
            internal SettingsRootDefinition(string tag, Type pageType, Func<string> titleProvider)
            {
                Tag = tag;
                PageType = pageType;
                TitleProvider = titleProvider;
            }

            internal string Tag { get; }

            internal Type PageType { get; }

            internal Func<string> TitleProvider { get; }
        }

        private sealed class SearchTargetDefinition
        {
            internal SearchTargetDefinition(string rootTag, Type? detailPageType, Func<string> titleProvider, Func<string>? descriptionProvider = null, string? focusElementName = null)
            {
                RootTag = rootTag;
                DetailPageType = detailPageType;
                TitleProvider = titleProvider;
                DescriptionProvider = descriptionProvider;
                FocusElementName = focusElementName;
            }

            internal string RootTag { get; }

            internal Type? DetailPageType { get; }

            internal Func<string> TitleProvider { get; }

            internal Func<string>? DescriptionProvider { get; }

            internal string? FocusElementName { get; }
        }

        private sealed class SettingsSearchTarget
        {
            internal SettingsSearchTarget(string displayText, string searchText, string rootTag, Type? detailPageType, string? focusElementName)
            {
                DisplayText = displayText;
                SearchText = searchText;
                RootTag = rootTag;
                DetailPageType = detailPageType;
                FocusElementName = focusElementName;
            }

            internal string DisplayText { get; }

            internal string SearchText { get; }

            internal string RootTag { get; }

            internal Type? DetailPageType { get; }

            internal string? FocusElementName { get; }
        }
    }
}
