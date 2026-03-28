using Microsoft.UI.Xaml;

namespace WindBoard.Settings.Pages
{
    public sealed partial class AboutSettingsPage
    {
        private void OnSettingsManagementClicked(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SettingsManagementPage));
        }
    }
}
