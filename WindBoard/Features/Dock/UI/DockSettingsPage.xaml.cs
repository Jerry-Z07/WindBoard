using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard;
using WindBoard.Features.Dock.Models;
using WindBoard.Localization;
using WindBoard.Settings;
using WindBoard.UI.Common;

namespace WindBoard.Features.Dock.UI
{
    public sealed partial class DockSettingsPage : Page
    {
        public DockSettingsPage()
        {
            InitializeComponent();
        }

        private void OnEditDockClicked(object sender, RoutedEventArgs e)
        {
            MainWindow? mainWindow = (Application.Current as App)?.TryGetMainWindow();
            if (mainWindow is null)
            {
                return;
            }

            mainWindow.EnterDockEditMode();
            SettingsWindow.Active?.Close();
        }

        private async void OnResetToDefaultClicked(object sender, RoutedEventArgs e)
        {
            if (XamlRoot is null)
            {
                return;
            }

            bool confirmed = await DialogHelpers.ShowConfirmAsync(
                XamlRoot,
                L10n.Get("Settings_Dock_ResetLayout_Button"),
                L10n.Get("Settings_Dock_ResetConfirm_Content"),
                L10n.Get("Common_ResetToDefault"),
                L10n.Get("Common_Cancel"));
            if (!confirmed)
            {
                return;
            }

            AppSettingsService.Instance.Update(s => s.Dock = new DockSettings());
        }
    }
}
