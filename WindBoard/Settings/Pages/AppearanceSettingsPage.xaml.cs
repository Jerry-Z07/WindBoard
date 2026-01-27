using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WindBoard.Settings.Pages
{
    public sealed partial class AppearanceSettingsPage : Page
    {
        public AppearanceSettingsPage()
        {
            InitializeComponent();
        }

        private void OnBackgroundClicked(object sender, RoutedEventArgs e)
        {
            // 二级导航：进入“背景”详细设置页（便于后续扩展更多外观项）。
            Frame.Navigate(typeof(BackgroundSettingsPage));
        }
    }
}
