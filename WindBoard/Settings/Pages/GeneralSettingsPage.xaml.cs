using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WindBoard.Settings.Pages
{
    public sealed partial class GeneralSettingsPage : Page
    {
        public GeneralSettingsPage()
        {
            InitializeComponent();
        }

        private void OnCamouflageSettingsClicked(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(CamouflageSettingsPage));
        }
    }
}

