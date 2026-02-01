using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Settings.Pages;

namespace WindBoard.Settings
{
    public sealed partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();

            // 首次打开时默认进入“外观”。
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
                "appearance" => typeof(AppearanceSettingsPage),
                "writing" => typeof(WritingSettingsPage),
                _ => typeof(AppearanceSettingsPage),
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
