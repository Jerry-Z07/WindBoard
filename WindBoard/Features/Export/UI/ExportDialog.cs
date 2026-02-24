using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindBoard.Features.Export.Models;
using WindBoard.Localization;

namespace WindBoard.Features.Export.UI
{
    /// <summary>
    /// 导出设置对话框（纯 C# 构建 ContentDialog，避免引入额外 XAML 文件）。
    /// </summary>
    internal static class ExportDialog
    {
        public static async Task<ExportDialogSelection?> ShowAsync(XamlRoot xamlRoot)
        {
            if (xamlRoot is null)
            {
                throw new ArgumentNullException(nameof(xamlRoot));
            }

            var formatCombo = new ComboBox
            {
                SelectedIndex = 0,
                Items =
                {
                    L10n.Get("Export_FileType_Png"),
                    L10n.Get("Export_FileType_Pdf"),
                    L10n.Get("Export_Format_Wbix_WithExt"),
                }
            };

            var scopeCombo = new ComboBox
            {
                SelectedIndex = 0,
                Items =
                {
                    L10n.Get("Export_PageScope_Current"),
                    L10n.Get("Export_PageScope_All"),
                    L10n.Get("Export_PageScope_Range"),
                }
            };

            var rangeBox = new TextBox
            {
                PlaceholderText = L10n.Get("Export_PageRange_Placeholder"),
                IsEnabled = false,
            };

            var pngAspectCombo = new ComboBox
            {
                SelectedIndex = 0,
                Items =
                {
                    L10n.Get("Export_PngAspect_Square"),
                    L10n.Get("Export_PngAspect_4_3"),
                    L10n.Get("Export_PngAspect_16_9"),
                }
            };

            var pngSizeCombo = new ComboBox
            {
                SelectedIndex = 1,
            };

            var dpiCombo = new ComboBox
            {
                SelectedIndex = 1,
                Items =
                {
                    L10n.Get("Export_PdfDpi_Standard"),
                    L10n.Get("Export_PdfDpi_High"),
                    L10n.Get("Export_PdfDpi_Ultra"),
                }
            };

            var paddingBox = new TextBox
            {
                Text = "24",
                PlaceholderText = L10n.Get("Export_Padding_Placeholder"),
            };

            var pngAspectLabel = new TextBlock { Text = L10n.Get("Export_PngAspect_Label") };
            var pngSizeLabel = new TextBlock { Text = L10n.Get("Export_PngSize_Label") };
            var dpiLabel = new TextBlock { Text = L10n.Get("Export_PdfDpi_Label") };
            var paddingLabel = new TextBlock { Text = L10n.Get("Export_Padding_Label") };

            var hintText = new TextBlock
            {
                Text = L10n.Get("Export_Hint_Text"),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.85,
            };

            void UpdateRangeEnabled()
            {
                rangeBox.IsEnabled = scopeCombo.SelectedIndex == (int)ExportPageScope.Range;
            }

            void UpdatePngSizeItems()
            {
                int oldIndex = Math.Clamp(pngSizeCombo.SelectedIndex, 0, 2);
                pngSizeCombo.Items.Clear();

                bool square = pngAspectCombo.SelectedIndex == 0;
                bool fourThree = pngAspectCombo.SelectedIndex == 1;

                (int w, int h) size720 = ComputePngSize(square, fourThree, baseHeight: 720);
                (int w, int h) size1080 = ComputePngSize(square, fourThree, baseHeight: 1080);
                (int w, int h) size4k = ComputePngSize(square, fourThree, baseHeight: 2160);

                pngSizeCombo.Items.Add(L10n.Format("Export_PngSizeItem_Fmt", 720, size720.w, size720.h));
                pngSizeCombo.Items.Add(L10n.Format("Export_PngSizeItem_Fmt", 1080, size1080.w, size1080.h));
                pngSizeCombo.Items.Add(L10n.Format("Export_PngSize4kItem_Fmt", size4k.w, size4k.h));

                pngSizeCombo.SelectedIndex = oldIndex;
            }

            void UpdateRasterOptionsVisibility()
            {
                bool isPng = formatCombo.SelectedIndex == (int)ExportFormat.Png;
                bool isPdf = formatCombo.SelectedIndex == (int)ExportFormat.Pdf;
                bool showRasterOptions = isPng || isPdf;

                pngAspectLabel.Visibility = isPng ? Visibility.Visible : Visibility.Collapsed;
                pngAspectCombo.Visibility = isPng ? Visibility.Visible : Visibility.Collapsed;
                pngSizeLabel.Visibility = isPng ? Visibility.Visible : Visibility.Collapsed;
                pngSizeCombo.Visibility = isPng ? Visibility.Visible : Visibility.Collapsed;

                dpiLabel.Visibility = isPdf ? Visibility.Visible : Visibility.Collapsed;
                dpiCombo.Visibility = isPdf ? Visibility.Visible : Visibility.Collapsed;

                paddingLabel.Visibility = showRasterOptions ? Visibility.Visible : Visibility.Collapsed;
                paddingBox.Visibility = showRasterOptions ? Visibility.Visible : Visibility.Collapsed;
            }

            scopeCombo.SelectionChanged += (_, _) => UpdateRangeEnabled();
            formatCombo.SelectionChanged += (_, _) => UpdateRasterOptionsVisibility();
            pngAspectCombo.SelectionChanged += (_, _) => UpdatePngSizeItems();

            UpdateRangeEnabled();
            UpdatePngSizeItems();
            UpdateRasterOptionsVisibility();

            var panel = new StackPanel { Spacing = 10 };

            panel.Children.Add(new TextBlock { Text = L10n.Get("Export_Format_Label") });
            panel.Children.Add(formatCombo);
            panel.Children.Add(new TextBlock { Text = L10n.Get("Export_PageScope_Label") });
            panel.Children.Add(scopeCombo);
            panel.Children.Add(rangeBox);
            panel.Children.Add(pngAspectLabel);
            panel.Children.Add(pngAspectCombo);
            panel.Children.Add(pngSizeLabel);
            panel.Children.Add(pngSizeCombo);
            panel.Children.Add(dpiLabel);
            panel.Children.Add(dpiCombo);
            panel.Children.Add(paddingLabel);
            panel.Children.Add(paddingBox);
            panel.Children.Add(hintText);

            var dialog = new ContentDialog
            {
                Title = L10n.Get("Export_Dialog_Title"),
                Content = panel,
                PrimaryButtonText = L10n.Get("Common_Next"),
                CloseButtonText = L10n.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            var format = (ExportFormat)Math.Clamp(formatCombo.SelectedIndex, 0, 2);
            var scope = (ExportPageScope)Math.Clamp(scopeCombo.SelectedIndex, 0, 2);
            string range = rangeBox.Text ?? string.Empty;
            int dpi = format == ExportFormat.Pdf
                ? dpiCombo.SelectedIndex switch
                {
                    0 => 96,
                    2 => 288,
                    _ => 192,
                }
                : 96;

            float paddingDip = 24.0f;
            if (float.TryParse(paddingBox.Text, out float parsedPadding))
            {
                paddingDip = Math.Max(0.0f, parsedPadding);
            }

            BoardRasterFixedFrame? pngFrame = null;
            if (format == ExportFormat.Png)
            {
                bool square = pngAspectCombo.SelectedIndex == 0;
                bool fourThree = pngAspectCombo.SelectedIndex == 1;

                int baseHeight = pngSizeCombo.SelectedIndex switch
                {
                    0 => 720,
                    2 => 2160,
                    _ => 1080,
                };

                (int w, int h) size = ComputePngSize(square, fourThree, baseHeight);
                pngFrame = new BoardRasterFixedFrame(size.w, size.h);
            }

            return new ExportDialogSelection(format, scope, range, dpi, paddingDip, pngFrame);
        }

        private static (int w, int h) ComputePngSize(bool square, bool fourThree, int baseHeight)
        {
            int h = Math.Max(1, baseHeight);

            if (square)
            {
                return (h, h);
            }

            if (fourThree)
            {
                int w43 = (int)Math.Round(h * 4.0 / 3.0);
                return (Math.Max(1, w43), h);
            }

            // 16:9
            int w169 = (int)Math.Round(h * 16.0 / 9.0);
            return (Math.Max(1, w169), h);
        }
    }
}
