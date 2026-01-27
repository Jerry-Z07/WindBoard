using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace WindBoard.Settings.Pages
{
    public sealed partial class BackgroundSettingsPage : Page
    {
        // 弹窗内控件需要相互同步（ColorPicker ⇄ HEX 输入），用此标记避免递归触发事件。
        private bool _isDialogSyncing;

        // 弹窗打开时记录原始颜色，便于点击“取消”时回退。
        private Color _dialogOriginalColor;

        // 标记弹窗是否处于显示期，用于外部设置变更时同步弹窗 UI。
        private bool _isDialogOpen;

        public BackgroundSettingsPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SyncUiFromSettings();
            AppSettingsService.Instance.Changed += OnSettingsChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            AppSettingsService.Instance.Changed -= OnSettingsChanged;
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            // 设置变更可能来自非 UI 线程，这里统一切回 UI 线程刷新。
            if (!DispatcherQueue.TryEnqueue(SyncUiFromSettings))
            {
                SyncUiFromSettings();
            }
        }

        private void SyncUiFromSettings()
        {
            Color color = AppSettingsService.Instance.GetCanvasBackgroundColor();
            CanvasBackgroundPreviewBrush.Color = color;
            CurrentHexTextBlock.Text = ColorHex.ToHexRgb(color);

            if (_isDialogOpen)
            {
                SyncDialogFromColor(color);
            }
        }

        private async void OnCanvasBackgroundClicked(object sender, RoutedEventArgs e)
        {
            Color current = AppSettingsService.Instance.GetCanvasBackgroundColor();
            _dialogOriginalColor = current;

            SyncDialogFromColor(current);
            CanvasBackgroundDialog.XamlRoot = XamlRoot;
            _isDialogOpen = true;

            ContentDialogResult result = await CanvasBackgroundDialog.ShowAsync();
            _isDialogOpen = false;

            if (result == ContentDialogResult.Primary)
            {
                return;
            }

            // 点击“取消”则回退到打开弹窗前的颜色（提供可预览但可撤销的体验）。
            string originalHex = ColorHex.ToHexRgb(_dialogOriginalColor);
            AppSettingsService.Instance.Update(s => s.Appearance.CanvasBackgroundHex = originalHex);
        }

        private void SyncDialogFromColor(Color color)
        {
            _isDialogSyncing = true;
            try
            {
                DialogColorPicker.Color = color;
                DialogHexTextBox.Text = ColorHex.ToHexRgb(color);
                DialogHexErrorBar.IsOpen = false;
            }
            finally
            {
                _isDialogSyncing = false;
            }
        }

        private void OnDialogColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            if (_isDialogSyncing)
            {
                return;
            }

            Color color = args.NewColor;
            string hex = ColorHex.ToHexRgb(color);

            _isDialogSyncing = true;
            try
            {
                DialogHexTextBox.Text = hex;
                DialogHexErrorBar.IsOpen = false;
            }
            finally
            {
                _isDialogSyncing = false;
            }

            AppSettingsService.Instance.Update(s => s.Appearance.CanvasBackgroundHex = hex);
            CanvasBackgroundPreviewBrush.Color = color;
            CurrentHexTextBlock.Text = hex;
        }

        private void OnDialogHexTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isDialogSyncing)
            {
                return;
            }

            string text = DialogHexTextBox.Text;
            if (!ColorHex.TryParse(text, out Color color))
            {
                // 允许输入过程中的“中间态”（例如只输入了 # 或不满 6 位），但给出错误提示。
                DialogHexErrorBar.IsOpen = !string.IsNullOrWhiteSpace(text);
                return;
            }

            string normalized = ColorHex.ToHexRgb(color);

            _isDialogSyncing = true;
            try
            {
                if (DialogColorPicker.Color != color)
                {
                    DialogColorPicker.Color = color;
                }

                // 把用户输入归一化为统一格式（大写 + #RRGGBB）。
                if (!string.Equals(DialogHexTextBox.Text, normalized, StringComparison.Ordinal))
                {
                    DialogHexTextBox.Text = normalized;
                    DialogHexTextBox.SelectionStart = normalized.Length;
                }

                DialogHexErrorBar.IsOpen = false;
            }
            finally
            {
                _isDialogSyncing = false;
            }

            AppSettingsService.Instance.Update(s => s.Appearance.CanvasBackgroundHex = normalized);
            CanvasBackgroundPreviewBrush.Color = color;
            CurrentHexTextBlock.Text = normalized;
        }

        private void OnDialogResetToDefaultClicked(object sender, RoutedEventArgs e)
        {
            Color defaultColor = ColorHex.DefaultCanvasBackgroundColor;
            SyncDialogFromColor(defaultColor);
            AppSettingsService.Instance.Update(s => s.Appearance.CanvasBackgroundHex = ColorHex.DefaultCanvasBackgroundHex);
        }

        private void OnResetToDefaultClicked(object sender, RoutedEventArgs e)
        {
            AppSettingsService.Instance.Update(s => s.Appearance.CanvasBackgroundHex = ColorHex.DefaultCanvasBackgroundHex);
        }
    }
}
