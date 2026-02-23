using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Features.Dock.UI;
using WindBoard.Localization;

namespace WindBoard.Settings.Pages
{
    public sealed partial class AppearanceSettingsPage : Page
    {
        private readonly CanvasBackgroundColorSettingsController _canvasBackgroundController;
        private bool _isElementCardThemeDialogOpen;

        public AppearanceSettingsPage()
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
            SyncElementCardThemeUiFromSettings();
            AppSettingsService.Instance.Changed += OnAppSettingsChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _canvasBackgroundController.OnUnloaded();
            AppSettingsService.Instance.Changed -= OnAppSettingsChanged;
        }

        private async void OnCanvasBackgroundClicked(object sender, RoutedEventArgs e)
        {
            await _canvasBackgroundController.ShowDialogAsync();
        }

        private void OnAppSettingsChanged(object? sender, EventArgs e)
        {
            // 设置变更可能来自非 UI 线程，这里统一切回 UI 线程刷新。
            if (!DispatcherQueue.TryEnqueue(SyncElementCardThemeUiFromSettings))
            {
                SyncElementCardThemeUiFromSettings();
            }
        }

        private void SyncElementCardThemeUiFromSettings()
        {
            ElementCardTheme theme = AppSettingsService.Instance.GetElementCardTheme();

            if (CurrentElementCardThemeTextBlock is not null)
            {
                CurrentElementCardThemeTextBlock.Text = theme == ElementCardTheme.Light
                    ? L10n.Get("Settings_Appearance_ElementCardTheme_Light")
                    : L10n.Get("Settings_Appearance_ElementCardTheme_Dark");
            }

            if (_isElementCardThemeDialogOpen)
            {
                ElementCardThemeDarkRadioButton.IsChecked = theme == ElementCardTheme.Dark;
                ElementCardThemeLightRadioButton.IsChecked = theme == ElementCardTheme.Light;
            }
        }

        private async void OnElementCardThemeClicked(object sender, RoutedEventArgs e)
        {
            // 元素卡片主题：点击打开弹窗，用户在“深色/浅色”中选择，点击确定后写入设置。
            ElementCardTheme current = AppSettingsService.Instance.GetElementCardTheme();
            ElementCardThemeDarkRadioButton.IsChecked = current == ElementCardTheme.Dark;
            ElementCardThemeLightRadioButton.IsChecked = current == ElementCardTheme.Light;

            _isElementCardThemeDialogOpen = true;
            try
            {
                ElementCardThemeDialog.XamlRoot = XamlRoot;
                ContentDialogResult result = await ElementCardThemeDialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    return;
                }

                ElementCardTheme selected = ElementCardThemeLightRadioButton.IsChecked == true
                    ? ElementCardTheme.Light
                    : ElementCardTheme.Dark;

                AppSettingsService.Instance.Update(s => s.Appearance.ElementCardTheme = ElementCardThemeParser.ToSettingValue(selected));
            }
            finally
            {
                _isElementCardThemeDialogOpen = false;
            }
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

        private void OnDockSettingsClicked(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(DockSettingsPage));
        }
    }
}
