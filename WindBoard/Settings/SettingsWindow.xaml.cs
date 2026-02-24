using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Settings.Pages;

namespace WindBoard.Settings
{
    public sealed partial class SettingsWindow : Window
    {
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

        private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (!ContentFrame.CanGoBack)
            {
                return;
            }

            ContentFrame.GoBack();
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
            NavView.IsBackButtonVisible = canGoBack
                ? NavigationViewBackButtonVisible.Visible
                : NavigationViewBackButtonVisible.Collapsed;
        }
    }
}
