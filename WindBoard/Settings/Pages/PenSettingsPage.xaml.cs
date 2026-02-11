using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using WindBoard.Localization;

namespace WindBoard.Settings.Pages
{
    public sealed partial class PenSettingsPage : Page
    {
        private readonly DispatcherQueue _dispatcherQueue;

        // UI 同步标记：避免“设置变更 -> 刷新控件 -> 再次触发 ValueChanged”的递归。
        private bool _isSyncing;

        // 色板编辑：当前选中的色块索引（以 0 开始）。
        private int? _selectedPaletteIndex;

        public PenSettingsPage()
        {
            InitializeComponent();
            _dispatcherQueue = DispatcherQueue;
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
            if (!_dispatcherQueue.TryEnqueue(SyncUiFromSettings))
            {
                SyncUiFromSettings();
            }
        }

        private void SyncUiFromSettings()
        {
            PenSettingsSnapshot snapshot = AppSettingsService.Instance.GetPenSettingsSnapshot();

            // 选择索引超界时自动清空，避免后续写入越界。
            if (_selectedPaletteIndex is int idx && (idx < 0 || idx >= snapshot.PaletteHexes.Count))
            {
                _selectedPaletteIndex = null;
            }

            _isSyncing = true;
            try
            {
                PaletteCountNumberBox.Value = snapshot.PaletteHexes.Count;
                UseThicknessSliderToggleSwitch.IsOn = snapshot.UseThicknessSlider;

                ThicknessPreset1NumberBox.Value = snapshot.ThicknessPresets[0];
                ThicknessPreset2NumberBox.Value = snapshot.ThicknessPresets[1];
                ThicknessPreset3NumberBox.Value = snapshot.ThicknessPresets[2];

                ThicknessPresetPreviewLine1.StrokeThickness = snapshot.ThicknessPresets[0];
                ThicknessPresetPreviewLine2.StrokeThickness = snapshot.ThicknessPresets[1];
                ThicknessPresetPreviewLine3.StrokeThickness = snapshot.ThicknessPresets[2];

                ThicknessPreviewSlider.Value = snapshot.ThicknessPresets[1];

                ThicknessPresetsPreviewPanel.Visibility = snapshot.UseThicknessSlider
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                ThicknessSliderPreviewPanel.Visibility = snapshot.UseThicknessSlider
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                ThicknessPresetsEditorPanel.Visibility = snapshot.UseThicknessSlider
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
            finally
            {
                _isSyncing = false;
            }

            RebuildPalettePreview(snapshot.PaletteHexes);
            SyncColorEditorFromSelection(snapshot.PaletteHexes);
        }

        private void RebuildPalettePreview(IReadOnlyList<string?> paletteHexes)
        {
            // 说明：
            // - 色板数量允许 3~24；
            // - UI 需自适应：根据数量计算列数（最少 3 列，最多 6 列），并自动换行。
            PalettePreviewGrid.Children.Clear();
            PalettePreviewGrid.RowDefinitions.Clear();
            PalettePreviewGrid.ColumnDefinitions.Clear();

            int count = paletteHexes.Count;
            if (count <= 0)
            {
                return;
            }

            int columns = ComputePaletteColumns(count);
            int rows = (int)Math.Ceiling(count / (double)columns);

            for (int c = 0; c < columns; c++)
            {
                PalettePreviewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            for (int r = 0; r < rows; r++)
            {
                PalettePreviewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (int i = 0; i < count; i++)
            {
                ToggleButton button = CreatePaletteSwatchButton(i, paletteHexes[i]);
                int row = i / columns;
                int col = i % columns;
                Grid.SetRow(button, row);
                Grid.SetColumn(button, col);
                PalettePreviewGrid.Children.Add(button);
            }
        }

        private static int ComputePaletteColumns(int count)
        {
            int columns = (int)Math.Ceiling(Math.Sqrt(count));
            columns = Math.Clamp(columns, 3, 6);
            return columns;
        }

        private ToggleButton CreatePaletteSwatchButton(int index, string? hex)
        {
            var button = new ToggleButton
            {
                Tag = index,
                Style = (Style)Resources["PenColorSwatchToggleButtonStyle"],
            };
            button.Click += OnPaletteSwatchClicked;

            // 内容：非空为实心圆；空色块保持透明（仅保留描边，便于识别与点击）。
            var ellipse = new Ellipse { Margin = new Thickness(2) };
            if (ColorHex.TryParse(hex, out Color color))
            {
                ellipse.Fill = new SolidColorBrush(Color.FromArgb(0xFF, color.R, color.G, color.B));
            }
            else
            {
                ellipse.Fill = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            }

            button.Content = ellipse;

            // 刷新“选中态”：保持当前选中的索引一致。
            button.IsChecked = _selectedPaletteIndex == index;
            return button;
        }

        private void OnPaletteSwatchClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton button)
            {
                return;
            }

            if (button.Tag is not int index)
            {
                return;
            }

            _selectedPaletteIndex = index;
            SetExclusiveToggleChecked(PalettePreviewGrid, button);

            // 选择变化后，根据最新设置刷新颜色编辑器（避免读取到旧值）。
            PenSettingsSnapshot snapshot = AppSettingsService.Instance.GetPenSettingsSnapshot();
            SyncColorEditorFromSelection(snapshot.PaletteHexes);
        }

        private void SyncColorEditorFromSelection(IReadOnlyList<string?> paletteHexes)
        {
            if (_selectedPaletteIndex is not int index || index < 0 || index >= paletteHexes.Count)
            {
                _isSyncing = true;
                try
                {
                    ColorEditorPanel.Visibility = Visibility.Collapsed;
                    SelectColorHintPanel.Visibility = Visibility.Visible;
                }
                finally
                {
                    _isSyncing = false;
                }

                return;
            }

            SelectColorHintPanel.Visibility = Visibility.Collapsed;
            ColorEditorPanel.Visibility = Visibility.Visible;
            SelectedColorTitleTextBlock.Text = L10n.Format("Settings_Pen_DefaultColorEdit_IndexedTitle_Fmt", index + 1);

            Color color = Color.FromArgb(0xFF, 0, 0, 0);
            if (ColorHex.TryParse(paletteHexes[index], out Color parsed))
            {
                color = Color.FromArgb(0xFF, parsed.R, parsed.G, parsed.B);
            }

            _isSyncing = true;
            try
            {
                if (SelectedColorPicker.Color != color)
                {
                    SelectedColorPicker.Color = color;
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void OnPaletteCountValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_isSyncing)
            {
                return;
            }

            if (double.IsNaN(args.NewValue))
            {
                return;
            }

            int newCount = (int)Math.Round(args.NewValue);
            newCount = Math.Clamp(newCount, PenSettingsDefaults.MinPaletteCount, PenSettingsDefaults.MaxPaletteCount);

            PenSettingsSnapshot snapshot = AppSettingsService.Instance.GetPenSettingsSnapshot();
            if (snapshot.PaletteHexes.Count == newCount)
            {
                return;
            }

            var palette = new List<string?>(snapshot.PaletteHexes);
            if (newCount < palette.Count)
            {
                palette.RemoveRange(newCount, palette.Count - newCount);
            }
            else
            {
                while (palette.Count < newCount)
                {
                    palette.Add(null);
                }
            }

            AppSettingsService.Instance.Update(s => s.Writing.Pen.PaletteHexes = palette);
        }

        private void OnSelectedColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        {
            if (_isSyncing)
            {
                return;
            }

            if (_selectedPaletteIndex is not int index)
            {
                return;
            }

            string hex = ColorHex.ToHexRgb(args.NewColor);

            AppSettingsService.Instance.Update(s =>
            {
                List<string?> palette = s.Writing.Pen.PaletteHexes ?? new List<string?>(PenSettingsDefaults.DefaultPaletteHexes);

                // 防御：如果外部变更导致列表变短，这里补齐到索引位置，避免越界。
                while (palette.Count <= index)
                {
                    palette.Add(null);
                }

                palette[index] = hex;
                s.Writing.Pen.PaletteHexes = palette;
            });
        }

        private void OnClearSelectedColorClicked(object sender, RoutedEventArgs e)
        {
            if (_selectedPaletteIndex is not int index)
            {
                return;
            }

            AppSettingsService.Instance.Update(s =>
            {
                List<string?> palette = s.Writing.Pen.PaletteHexes ?? new List<string?>(PenSettingsDefaults.DefaultPaletteHexes);
                if (index < 0 || index >= palette.Count)
                {
                    return;
                }

                palette[index] = null;
                s.Writing.Pen.PaletteHexes = palette;
            });
        }

        private void OnUseThicknessSliderToggled(object sender, RoutedEventArgs e)
        {
            if (_isSyncing)
            {
                return;
            }

            bool isOn = UseThicknessSliderToggleSwitch.IsOn;
            AppSettingsService.Instance.Update(s => s.Writing.Pen.UseThicknessSlider = isOn);
        }

        private void OnThicknessPresetValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_isSyncing)
            {
                return;
            }

            if (double.IsNaN(ThicknessPreset1NumberBox.Value)
                || double.IsNaN(ThicknessPreset2NumberBox.Value)
                || double.IsNaN(ThicknessPreset3NumberBox.Value))
            {
                return;
            }

            var presets =
            new List<float>
            {
                (float)ThicknessPreset1NumberBox.Value,
                (float)ThicknessPreset2NumberBox.Value,
                (float)ThicknessPreset3NumberBox.Value,
            };

            AppSettingsService.Instance.Update(s => s.Writing.Pen.ThicknessPresets = presets);
        }

        private static void SetExclusiveToggleChecked(Panel panel, ToggleButton checkedButton)
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is ToggleButton button)
                {
                    button.IsChecked = ReferenceEquals(button, checkedButton);
                }
            }
        }
    }
}
