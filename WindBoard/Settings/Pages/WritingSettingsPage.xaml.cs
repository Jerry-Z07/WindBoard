using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WindBoard.Settings.Pages
{
    public sealed partial class WritingSettingsPage : Page
    {
        public WritingSettingsPage()
        {
            InitializeComponent();
        }

        private void OnPenSettingsClicked(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(PenSettingsPage));
        }
    }
}

