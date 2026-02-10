using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using WindBoard.Board.Editing;
using WindBoard.Interaction;
using WindBoard.Settings;

namespace WindBoard
{
    /// <summary>
    /// 主窗口：页面管理与列表交互相关代码。
    /// </summary>
    public sealed partial class MainWindow
    {
        private void OnAddClicked(object sender, RoutedEventArgs e)
        {
            // “+”约定为新增页面。
            _workspace.AddPage();
        }

        private void OnPageIndicatorButtonClicked(object sender, RoutedEventArgs e)
        {
            // 页面管理弹出层锚定到右侧 Dock 容器，保证弹出区域正对 Dock 上方。
            FlyoutBase.ShowAttachedFlyout(PagesDockBorder);
        }

        private void InitializePages()
        {
            // 绑定 UI 数据源
            PagesListView.ItemsSource = _pageItems;

            _workspace.PagesChanged += OnWorkspacePagesChanged;
            _workspace.CurrentPageChanged += OnWorkspaceCurrentPageChanged;

            RefreshPageItems();
            ApplyCurrentPageToCanvas();
        }

        private void OnWorkspacePagesChanged()
        {
            RefreshPageItems();
        }

        private void OnWorkspaceCurrentPageChanged()
        {
            ApplyCurrentPageToCanvas();
            UpdatePageNavigator();
            SelectCurrentPageInListView();
        }

        private void RefreshPageItems()
        {
            _pageItems.Clear();
            for (int i = 0; i < _workspace.Pages.Count; i++)
            {
                _pageItems.Add(new PageListItem(_workspace.Pages[i], number: i + 1));
            }

            UpdatePageNavigator();
            SelectCurrentPageInListView();
        }

        private void ApplyCurrentPageToCanvas()
        {
            // 将当前页会话绑定到画板，确保渲染/撤销重做与页面一致。
            BoardCanvas.BindSession(_workspace.CurrentPage.Session);
            UpdatePageNavigator();
            SelectCurrentPageInListView();
        }

        private void UpdatePageNavigator()
        {
            int total = _workspace.Pages.Count;
            int current = _workspace.CurrentIndex + 1;

            // 只有一页时，右侧 Dock 仅展示“+”，避免信息噪音。
            bool showPager = total > 1;
            PagePrevButton.Visibility = showPager ? Visibility.Visible : Visibility.Collapsed;
            PageNextButton.Visibility = showPager ? Visibility.Visible : Visibility.Collapsed;
            PageIndicatorButton.Visibility = showPager ? Visibility.Visible : Visibility.Collapsed;

            if (!showPager)
            {
                PagesFlyout.Hide();
                return;
            }

            PageIndicatorText.Text = $"{current} / {total}";
            PagePrevButton.IsEnabled = _workspace.CurrentIndex > 0;
            PageNextButton.IsEnabled = _workspace.CurrentIndex < total - 1;
        }

        private void SelectCurrentPageInListView()
        {
            if (_isUpdatingPageSelection)
            {
                return;
            }

            _isUpdatingPageSelection = true;
            try
            {
                PagesListView.SelectedIndex = _workspace.CurrentIndex;

                if (PagesListView.SelectedItem is not null)
                {
                    PagesListView.ScrollIntoView(PagesListView.SelectedItem);
                }
            }
            finally
            {
                _isUpdatingPageSelection = false;
            }
        }

        private void OnPagesListItemClick(object sender, ItemClickEventArgs e)
        {
            if (_isUpdatingPageSelection)
            {
                return;
            }

            if (e.ClickedItem is not PageListItem item)
            {
                return;
            }

            int index = _pageItems.IndexOf(item);
            if (index < 0)
            {
                return;
            }

            _workspace.SetCurrentIndex(index);
        }

        private void OnDeletePageClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not PageListItem item)
            {
                return;
            }

            if (item.Page is not BoardPage page)
            {
                return;
            }

            _workspace.RemovePage(page);
        }

    }
}
