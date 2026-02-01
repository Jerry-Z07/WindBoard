using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WindBoard.Settings.Pages
{
    public sealed partial class BackgroundSettingsPage : Page
    {
        private readonly CanvasBackgroundColorSettingsController _canvasBackgroundController;

        public BackgroundSettingsPage()
        {
            InitializeComponent();
            _canvasBackgroundController = new CanvasBackgroundColorSettingsController(new CanvasBackgroundColorSettingsController.UiRefs
            {
                DispatcherQueue = DispatcherQueue,
                GetXamlRoot = () => XamlRoot,
                Dialog = CanvasBackgroundDialog,
                DialogColorPicker = DialogColorPicker,
                DialogHexTextBox = DialogHexTextBox,
                DialogHexErrorBar = DialogHexErrorBar,
                PreviewBrush = CanvasBackgroundPreviewBrush,
                CurrentHexTextBlock = CurrentHexTextBlock,
            });
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _canvasBackgroundController.OnLoaded();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _canvasBackgroundController.OnUnloaded();
        }

        private async void OnCanvasBackgroundClicked(object sender, RoutedEventArgs e)
        {
            await _canvasBackgroundController.ShowDialogAsync();
        }

        private void OnDialogColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            _canvasBackgroundController.OnDialogColorChanged(args.NewColor);
        }

        private void OnDialogHexTextChanged(object sender, TextChangedEventArgs e)
        {
            _canvasBackgroundController.OnDialogHexTextChanged(DialogHexTextBox.Text);
        }

        private void OnDialogResetToDefaultClicked(object sender, RoutedEventArgs e)
        {
            _canvasBackgroundController.ResetDialogToDefault();
        }

        private void OnResetToDefaultClicked(object sender, RoutedEventArgs e)
        {
            _canvasBackgroundController.ResetToDefault();
        }
    }
}
