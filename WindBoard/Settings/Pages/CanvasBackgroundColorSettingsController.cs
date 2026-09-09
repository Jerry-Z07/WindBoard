using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WindBoard.Settings.Pages
{
    /// <summary>
    /// "画布背景颜色"设置的 UI 协调器：
    /// - 负责预览区与弹窗（ColorPicker ⇄ HEX）的同步；
    /// - 订阅设置变更并刷新 UI；
    /// - 处理"可预览但可撤销"：取消弹窗时回退到打开前的颜色。
    /// </summary>
    internal sealed class CanvasBackgroundColorSettingsController
    {
        internal sealed class UiRefs
        {
            public DispatcherQueue? DispatcherQueue { get; init; }
            public Func<XamlRoot?>? GetXamlRoot { get; init; }
            public ContentDialog? Dialog { get; init; }
            public ColorPicker? DialogColorPicker { get; init; }
            public TextBox? DialogHexTextBox { get; init; }
            public InfoBar? DialogHexErrorBar { get; init; }
            public SolidColorBrush? PreviewBrush { get; init; }
            public TextBlock? CurrentHexTextBlock { get; init; }
        }

        private readonly DispatcherQueue _dispatcherQueue;
        private readonly ContentDialog _dialog;
        private readonly Func<XamlRoot?> _getXamlRoot;
        private readonly ColorPicker _dialogColorPicker;
        private readonly TextBox _dialogHexTextBox;
        private readonly InfoBar _dialogHexErrorBar;
        private readonly SolidColorBrush _previewBrush;
        private readonly TextBlock _currentHexTextBlock;

        // 弹窗内控件需要相互同步（ColorPicker ⇄ HEX 输入），用此标记避免递归触发事件。
        private bool _isDialogSyncing;

        // 弹窗打开时记录原始颜色，便于点击"取消"时回退。
        private Color _dialogOriginalColor;

        // 标记弹窗是否处于显示期，用于外部设置变更时同步弹窗 UI。
        private bool _isDialogOpen;

        internal CanvasBackgroundColorSettingsController(UiRefs ui)
        {
            ArgumentNullException.ThrowIfNull(ui);

            // CA2208：paramName 须为方法真实参数。ArgumentNullException.ThrowIfNull 以
            // CallerArgumentExpression（如 "ui.DispatcherQueue"）作为参数名，能准确标识哪个 UI 引用为空。
            // UiRefs 为 init-only 自动属性，重复访问幂等，先校验后赋值等价于原 ?? throw 写法。
            ArgumentNullException.ThrowIfNull(ui.DispatcherQueue);
            ArgumentNullException.ThrowIfNull(ui.GetXamlRoot);
            ArgumentNullException.ThrowIfNull(ui.Dialog);
            ArgumentNullException.ThrowIfNull(ui.DialogColorPicker);
            ArgumentNullException.ThrowIfNull(ui.DialogHexTextBox);
            ArgumentNullException.ThrowIfNull(ui.DialogHexErrorBar);
            ArgumentNullException.ThrowIfNull(ui.PreviewBrush);
            ArgumentNullException.ThrowIfNull(ui.CurrentHexTextBlock);
            _dispatcherQueue = ui.DispatcherQueue;
            _getXamlRoot = ui.GetXamlRoot;
            _dialog = ui.Dialog;
            _dialogColorPicker = ui.DialogColorPicker;
            _dialogHexTextBox = ui.DialogHexTextBox;
            _dialogHexErrorBar = ui.DialogHexErrorBar;
            _previewBrush = ui.PreviewBrush;
            _currentHexTextBlock = ui.CurrentHexTextBlock;
        }

        internal void OnLoaded()
        {
            SyncUiFromSettings();
            AppSettingsService.Instance.Changed += OnSettingsChanged;
        }

        internal void OnUnloaded()
        {
            AppSettingsService.Instance.Changed -= OnSettingsChanged;
        }

        internal async Task ShowDialogAsync()
        {
            Color current = AppSettingsService.Instance.GetCanvasBackgroundColor();
            _dialogOriginalColor = current;

            SyncDialogFromColor(current);
            _dialog.XamlRoot = _getXamlRoot();
            _isDialogOpen = true;

            ContentDialogResult result = await _dialog.ShowAsync();
            _isDialogOpen = false;

            if (result == ContentDialogResult.Primary)
            {
                return;
            }

            // 点击"取消"则回退到打开弹窗前的颜色（提供可预览但可撤销的体验）。
            string originalHex = ColorHex.ToHexRgb(_dialogOriginalColor);
            AppSettingsService.Instance.Update(s => s.Appearance.CanvasBackgroundHex = originalHex);
        }

        internal void OnDialogColorChanged(Color color)
        {
            if (_isDialogSyncing)
            {
                return;
            }

            string hex = ColorHex.ToHexRgb(color);

            _isDialogSyncing = true;
            try
            {
                _dialogHexTextBox.Text = hex;
                _dialogHexErrorBar.IsOpen = false;
            }
            finally
            {
                _isDialogSyncing = false;
            }

            AppSettingsService.Instance.Update(s => s.Appearance.CanvasBackgroundHex = hex);
            _previewBrush.Color = color;
            _currentHexTextBlock.Text = hex;
        }

        internal void OnDialogHexTextChanged(string text)
        {
            if (_isDialogSyncing)
            {
                return;
            }

            if (!ColorHex.TryParse(text, out Color color))
            {
                // 允许输入过程中的"中间态"（例如只输入了 # 或不满 6 位），但给出错误提示。
                _dialogHexErrorBar.IsOpen = !string.IsNullOrWhiteSpace(text);
                return;
            }

            string normalized = ColorHex.ToHexRgb(color);

            _isDialogSyncing = true;
            try
            {
                if (_dialogColorPicker.Color != color)
                {
                    _dialogColorPicker.Color = color;
                }

                // 把用户输入归一化为统一格式（大写 + #RRGGBB）。
                if (!string.Equals(_dialogHexTextBox.Text, normalized, StringComparison.Ordinal))
                {
                    _dialogHexTextBox.Text = normalized;
                    _dialogHexTextBox.SelectionStart = normalized.Length;
                }

                _dialogHexErrorBar.IsOpen = false;
            }
            finally
            {
                _isDialogSyncing = false;
            }

            AppSettingsService.Instance.Update(s => s.Appearance.CanvasBackgroundHex = normalized);
            _previewBrush.Color = color;
            _currentHexTextBlock.Text = normalized;
        }

        internal void ResetDialogToDefault()
        {
            Color defaultColor = ColorHex.DefaultCanvasBackgroundColor;
            SyncDialogFromColor(defaultColor);
            AppSettingsService.Instance.Update(s => s.Appearance.CanvasBackgroundHex = ColorHex.DefaultCanvasBackgroundHex);
        }

        internal void ResetToDefault()
        {
            AppSettingsService.Instance.Update(s => s.Appearance.CanvasBackgroundHex = ColorHex.DefaultCanvasBackgroundHex);
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            // 设置变更可能来自非 UI 线程，这里统一切回 UI 线程刷新。
            if (!_dispatcherQueue.TryEnqueue(SyncUiFromSettings))
            {
                SyncUiFromSettings();
            }
        }

        private void SyncUiFromSettings()
        {
            Color color = AppSettingsService.Instance.GetCanvasBackgroundColor();
            _previewBrush.Color = color;
            _currentHexTextBlock.Text = ColorHex.ToHexRgb(color);

            if (_isDialogOpen)
            {
                SyncDialogFromColor(color);
            }
        }

        private void SyncDialogFromColor(Color color)
        {
            _isDialogSyncing = true;
            try
            {
                _dialogColorPicker.Color = color;
                _dialogHexTextBox.Text = ColorHex.ToHexRgb(color);
                _dialogHexErrorBar.IsOpen = false;
            }
            finally
            {
                _isDialogSyncing = false;
            }
        }
    }
}
