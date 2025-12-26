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
        }

        private void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_pageService.CurrentPageIndex >= Pages.Count - 1) return;
            _pageService.SwitchToPage(_pageService.CurrentPageIndex + 1);
        }

        private void BtnAddPage_Click(object sender, RoutedEventArgs e)
        {
            _pageService.AddPage();
        }

        private void BtnPageIndicator_Click(object sender, RoutedEventArgs e)
        {
            if (!IsMultiPage) return;

            _pageService.RefreshAllPreviews();
        }

        private void PageNavigator_PageSelected(object sender, BoardPageEventArgs e)
        {
            int index = Pages.IndexOf(e.Page);
            if (index >= 0)
            {
                _pageService.SwitchToPage(index);
            }
        }

        private void PageNavigator_PageDeleteRequested(object sender, BoardPageEventArgs e)
        {
            _pageService.DeletePage(e.Page);
        }

        private void PageNavigatorControl_Loaded(object sender, RoutedEventArgs e)
        {
            NotifyPageUiChanged();
        }
    }
}
