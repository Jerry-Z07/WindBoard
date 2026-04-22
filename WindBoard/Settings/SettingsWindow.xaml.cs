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
            Type pageType = tag switch
            {
                "general" => typeof(GeneralSettingsPage),
                "appearance" => typeof(AppearanceSettingsPage),
                "writing" => typeof(WritingSettingsPage),
                "shortcuts" => typeof(WindBoard.Features.Shortcuts.UI.ShortcutsSettingsPage),
                "debug" => typeof(DebugSettingsPage),
                "about" => typeof(AboutSettingsPage),
                _ => typeof(GeneralSettingsPage),
            };

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
            return tag switch
            {
                "general" => typeof(GeneralSettingsPage),
                "appearance" => typeof(AppearanceSettingsPage),
                "writing" => typeof(WritingSettingsPage),
                "shortcuts" => typeof(ShortcutsSettingsPage),
                "debug" => typeof(DebugSettingsPage),
                "about" => typeof(AboutSettingsPage),
                _ => typeof(GeneralSettingsPage),
            };
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
            if (pageType == typeof(GeneralSettingsPage))
            {
                return L10n.Get("Settings_General_Title");
            }

            if (pageType == typeof(AppearanceSettingsPage))
            {
                return L10n.Get("Settings_Appearance_Title");
            }

            if (pageType == typeof(WritingSettingsPage))
            {
                return L10n.Get("Settings_Writing_Title");
            }

            if (pageType == typeof(ShortcutsSettingsPage))
            {
                return L10n.Get("Settings_Shortcuts_Title");
            }

            if (pageType == typeof(DebugSettingsPage))
            {
                return L10n.Get("Settings_Debug_Title");
            }

            if (pageType == typeof(AboutSettingsPage))
            {
                return L10n.Get("Settings_About_Title");
            }

            if (pageType == typeof(CamouflageSettingsPage))
            {
                return L10n.Get("Settings_Camouflage_Title");
            }

            if (pageType == typeof(DockSettingsPage))
            {
                return L10n.Get("Settings_Dock_Title");
            }

            if (pageType == typeof(PenSettingsPage))
            {
                return L10n.Get("Settings_Pen_Title");
            }

            if (pageType == typeof(SettingsManagementPage))
            {
                return L10n.Get("Settings_About_SettingsManagement_SectionTitle");
            }

            return string.Empty;
        }

        private static string GetRootTitle(string rootTag)
        {
            return rootTag switch
            {
                "general" => L10n.Get("Settings_General_Title"),
                "appearance" => L10n.Get("Settings_Appearance_Title"),
                "writing" => L10n.Get("Settings_Writing_Title"),
                "shortcuts" => L10n.Get("Settings_Shortcuts_Title"),
                "debug" => L10n.Get("Settings_Debug_Title"),
                "about" => L10n.Get("Settings_About_Title"),
                _ => L10n.Get("Common_Settings"),
            };
        }

        private void RebuildSearchTargets()
        {
            _searchTargets.Clear();

            AddSearchTarget("general", null, "Settings_General_Title");
            AddSearchTarget("general", null, "Settings_General_Language_Title", "Settings_General_Language_Description", "LanguageComboBox");
            AddSearchTarget("general", null, "Settings_General_StartupWindowMode_Title", "Settings_General_StartupWindowMode_Description", "StartupWindowModeComboBox");
            AddSearchTarget("general", null, "Settings_General_EnterScreenAnnotationWhenMinimized_Title", "Settings_General_EnterScreenAnnotationWhenMinimized_Description", "EnterScreenAnnotationWhenMinimizedToggleSwitch");
            AddSearchTarget("general", typeof(CamouflageSettingsPage), "Settings_Camouflage_Title", "Settings_Camouflage_Description", "EnabledToggleSwitch");

            AddSearchTarget("appearance", null, "Settings_Appearance_Title");
            AddSearchTarget("appearance", null, "Settings_Background_CanvasBackgroundColor_Title", "Settings_Background_CanvasBackgroundColor_Description", "CanvasBackgroundCard");
            AddSearchTarget("appearance", null, "Settings_Appearance_ElementCardTheme_Title", "Settings_Appearance_ElementCardTheme_Description", "ElementCardThemeCard");
            AddSearchTarget("appearance", typeof(DockSettingsPage), "Settings_Dock_Title", "Settings_Dock_Description", "UndoRedoVisibleToggleSwitch");

            AddSearchTarget("writing", null, "Settings_Writing_Title");
            AddSearchTarget("writing", typeof(PenSettingsPage), "Settings_Pen_Title", "Settings_Writing_Pen_Description", "PaletteCountNumberBox");

            AddSearchTarget("shortcuts", null, "Settings_Shortcuts_Title", "Settings_Shortcuts_Description");
            AddSearchTarget("shortcuts", null, "Settings_Shortcuts_ConflictReminder_Header", null, "ConflictReminderToggleSwitch");
            AddSearchTarget("shortcuts", null, "Settings_Shortcuts_Undo_Title", null, "UndoShortcutCard");
            AddSearchTarget("shortcuts", null, "Settings_Shortcuts_Redo_Title", null, "RedoShortcutCard");

            AddSearchTarget("about", null, "Settings_About_Title");
            AddSearchTarget("about", typeof(SettingsManagementPage), "Settings_About_SettingsManagement_SectionTitle", "Settings_About_SettingsManagement_Entry_Description", "ExportSettingsCard");
            AddSearchTarget("about", null, "Settings_About_AutoCheckUpdates", null, "AutoCheckUpdatesComboBox");
            AddSearchTarget("about", null, "Updates_DownloadSource_Title", null, "DownloadSourceComboBox");
            AddSearchTarget("about", null, "Settings_About_CheckUpdates", null, "CheckUpdatesButton");

            if (DebugToolsGate.IsVisible)
            {
                AddSearchTarget("debug", null, "Settings_Debug_Title");
                AddSearchTarget("debug", null, "Settings_Debug_OpenLogDir_Title", "Settings_Debug_OpenLogDir_Description");
                AddSearchTarget("debug", null, "Settings_Debug_OpenSettingsDir_Title", "Settings_Debug_OpenSettingsDir_Description");
                AddSearchTarget("debug", null, "Settings_Debug_SendTestToast_Title", "Settings_Debug_SendTestToast_Description");
            }
        }

        private void AddSearchTarget(string rootTag, Type? detailPageType, string titleKey, string? descriptionKey = null, string? focusElementName = null)
        {
            string rootTitle = GetRootTitle(rootTag);
            string title = GetSearchResource(titleKey);
            string displayText = string.Equals(rootTitle, title, StringComparison.Ordinal)
                ? title
                : $"{rootTitle} / {title}";

            string searchText = displayText;
            if (!string.IsNullOrWhiteSpace(descriptionKey))
            {
                searchText = $"{searchText} {GetSearchResource(descriptionKey)}";
            }

            _searchTargets.Add(new SettingsSearchTarget(displayText, searchText, rootTag, detailPageType, focusElementName));
        }

        private static string GetSearchResource(string key)
        {
            return key switch
            {
                "Settings_General_Title" => L10n.Get("Settings_General_Title"),
                "Settings_General_Language_Title" => L10n.Get("Settings_General_Language_Title"),
                "Settings_General_Language_Description" => L10n.Get("Settings_General_Language_Description"),
                "Settings_General_StartupWindowMode_Title" => L10n.Get("Settings_General_StartupWindowMode_Title"),
                "Settings_General_StartupWindowMode_Description" => L10n.Get("Settings_General_StartupWindowMode_Description"),
                "Settings_General_EnterScreenAnnotationWhenMinimized_Title" => L10n.Get("Settings_General_EnterScreenAnnotationWhenMinimized_Title"),
                "Settings_General_EnterScreenAnnotationWhenMinimized_Description" => L10n.Get("Settings_General_EnterScreenAnnotationWhenMinimized_Description"),
                "Settings_Camouflage_Title" => L10n.Get("Settings_Camouflage_Title"),
                "Settings_Camouflage_Description" => L10n.Get("Settings_Camouflage_Description"),
                "Settings_Appearance_Title" => L10n.Get("Settings_Appearance_Title"),
                "Settings_Background_CanvasBackgroundColor_Title" => L10n.Get("Settings_Background_CanvasBackgroundColor_Title"),
                "Settings_Background_CanvasBackgroundColor_Description" => L10n.Get("Settings_Background_CanvasBackgroundColor_Description"),
                "Settings_Appearance_ElementCardTheme_Title" => L10n.Get("Settings_Appearance_ElementCardTheme_Title"),
                "Settings_Appearance_ElementCardTheme_Description" => L10n.Get("Settings_Appearance_ElementCardTheme_Description"),
                "Settings_Dock_Title" => L10n.Get("Settings_Dock_Title"),
                "Settings_Dock_Description" => L10n.Get("Settings_Dock_Description"),
                "Settings_Writing_Title" => L10n.Get("Settings_Writing_Title"),
                "Settings_Pen_Title" => L10n.Get("Settings_Pen_Title"),
                "Settings_Writing_Pen_Description" => L10n.Get("Settings_Writing_Pen_Description"),
                "Settings_Shortcuts_Title" => L10n.Get("Settings_Shortcuts_Title"),
                "Settings_Shortcuts_Description" => L10n.Get("Settings_Shortcuts_Description"),
                "Settings_Shortcuts_ConflictReminder_Header" => L10n.Get("Settings_Shortcuts_ConflictReminder_Header"),
                "Settings_Shortcuts_Undo_Title" => L10n.Get("Settings_Shortcuts_Undo_Title"),
                "Settings_Shortcuts_Redo_Title" => L10n.Get("Settings_Shortcuts_Redo_Title"),
                "Settings_About_Title" => L10n.Get("Settings_About_Title"),
                "Settings_About_SettingsManagement_SectionTitle" => L10n.Get("Settings_About_SettingsManagement_SectionTitle"),
                "Settings_About_SettingsManagement_Entry_Description" => L10n.Get("Settings_About_SettingsManagement_Entry_Description"),
                "Settings_About_AutoCheckUpdates" => L10n.Get("Settings_About_AutoCheckUpdates"),
                "Updates_DownloadSource_Title" => L10n.Get("Updates_DownloadSource_Title"),
                "Settings_About_CheckUpdates" => L10n.Get("Settings_About_CheckUpdates"),
                "Settings_Debug_Title" => L10n.Get("Settings_Debug_Title"),
                "Settings_Debug_OpenLogDir_Title" => L10n.Get("Settings_Debug_OpenLogDir_Title"),
                "Settings_Debug_OpenLogDir_Description" => L10n.Get("Settings_Debug_OpenLogDir_Description"),
                "Settings_Debug_OpenSettingsDir_Title" => L10n.Get("Settings_Debug_OpenSettingsDir_Title"),
                "Settings_Debug_OpenSettingsDir_Description" => L10n.Get("Settings_Debug_OpenSettingsDir_Description"),
                "Settings_Debug_SendTestToast_Title" => L10n.Get("Settings_Debug_SendTestToast_Title"),
                "Settings_Debug_SendTestToast_Description" => L10n.Get("Settings_Debug_SendTestToast_Description"),
                _ => key,
            };
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
