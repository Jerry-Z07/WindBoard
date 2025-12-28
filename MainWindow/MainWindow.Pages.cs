using System.Windows;
using WindBoard.Controls;

namespace WindBoard
{
    public partial class MainWindow
    {
        private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_pageService.CurrentPageIndex <= 0) return;
            _pageService.SwitchToPage(_pageService.CurrentPageIndex - 1);
            AttachUndoToCurrentStrokes();
            ClearInkCanvasSelectionPreserveEditingMode();
            SelectAttachment(null);
        }

        private void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_pageService.CurrentPageIndex >= Pages.Count - 1) return;
            _pageService.SwitchToPage(_pageService.CurrentPageIndex + 1);
            AttachUndoToCurrentStrokes();
            ClearInkCanvasSelectionPreserveEditingMode();
            SelectAttachment(null);
        }

        private void BtnAddPage_Click(object sender, RoutedEventArgs e)
        {
            _pageService.AddPage();
            AttachUndoToCurrentStrokes();
            ClearInkCanvasSelectionPreserveEditingMode();
            SelectAttachment(null);
        }

        private void BtnPageIndicator_Click(object sender, RoutedEventArgs e)
        {
            if (!IsMultiPage) return;

            // 弹窗打开前只保存当前页状态；缩略图改为按需生成（可视项 Loaded 时触发）。
            _pageService.SaveCurrentPage();
            if (_pageService.CurrentPage != null)
            {
                _pageService.EnsurePagePreview(_pageService.CurrentPage);
            }
        }

        private void PageNavigator_PageSelected(object sender, BoardPageEventArgs e)
        {
            int index = Pages.IndexOf(e.Page);
            if (index >= 0)
            {
                _pageService.SwitchToPage(index);
                AttachUndoToCurrentStrokes();
                ClearInkCanvasSelectionPreserveEditingMode();
                SelectAttachment(null);
            }
        }

        private void PageNavigator_PageDeleteRequested(object sender, BoardPageEventArgs e)
        {
            _pageService.DeletePage(e.Page);
            AttachUndoToCurrentStrokes();
            ClearInkCanvasSelectionPreserveEditingMode();
            SelectAttachment(null);
        }

        private void PageNavigatorControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is PageNavigatorControl c)
            {
                c.PreviewNeeded -= PageNavigator_PreviewNeeded;
                c.PreviewNeeded += PageNavigator_PreviewNeeded;
            }
            NotifyPageUiChanged();
        }

        private void PageNavigator_PreviewNeeded(object? sender, BoardPageEventArgs e)
        {
            if (!IsMultiPage) return;
            _pageService.EnsurePagePreview(e.Page);
        }
    }
}
