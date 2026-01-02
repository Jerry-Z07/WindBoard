using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using WindBoard.Models.Export;
using WindBoard.Services.Export;
using WindBoard.Services;

namespace WindBoard.Views.Dialogs
{
    /// <summary>
    /// 导出对话框
    /// </summary>
    public partial class ExportDialog : UserControl
    {
        private readonly IList<BoardPage> _pages;
        private readonly int _currentPageIndex;
        private readonly System.Windows.Media.Color _backgroundColor;
        private readonly ExportService _exportService = new();

        /// <summary>
        /// 导出请求结果
        /// </summary>
        public ExportRequest? Result { get; private set; }

        public ExportDialog(IList<BoardPage> pages, int currentPageIndex, System.Windows.Media.Color backgroundColor)
        {
            _pages = pages;
            _currentPageIndex = currentPageIndex;
            _backgroundColor = backgroundColor;

            InitializeComponent();
            Initialize();
        }

        private void Initialize()
        {
            // 设置页面数量
            TxtPageCount.Text = LocalizationService.Instance.Format("ExportDialog_PageCount_Format", _pages.Count);

            // 如果只有一页，禁用全部页面选项
            if (_pages.Count <= 1)
            {
                RbAllPages.IsEnabled = false;
                TxtPageCount.Visibility = Visibility.Collapsed;
            }

            UpdateEstimatedSize();
        }

        private void Scope_Changed(object sender, RoutedEventArgs e)
        {
            UpdateEstimatedSize();
        }

        private void FormatTab_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateEstimatedSize();
        }

        private void ImageFormat_Changed(object sender, RoutedEventArgs e)
        {
            UpdateEstimatedSize();
        }

        private void Resolution_Changed(object sender, TextChangedEventArgs e)
        {
            UpdateEstimatedSize();
        }

        private void WbiOptions_Changed(object sender, RoutedEventArgs e)
        {
            UpdateEstimatedSize();
        }

        private void Preset1080p_Click(object sender, RoutedEventArgs e)
        {
            TxtWidth.Text = "1920";
            TxtHeight.Text = "1080";
        }

        private void Preset2K_Click(object sender, RoutedEventArgs e)
        {
            TxtWidth.Text = "2560";
            TxtHeight.Text = "1440";
        }

        private void Preset4K_Click(object sender, RoutedEventArgs e)
        {
            TxtWidth.Text = "3840";
            TxtHeight.Text = "2160";
        }

        private void UpdateEstimatedSize()
        {
            if (TxtEstimatedSize == null || FormatTabControl == null) return;

            try
            {
                var pages = GetPagesToExport();
                var format = GetSelectedFormat();
                object options = GetExportOptions();

                long size = _exportService.EstimateExportSize(pages, format, options);
                TxtEstimatedSize.Text = "~" + ExportService.FormatFileSize(size);
            }
            catch
            {
                TxtEstimatedSize.Text = LocalizationService.Instance.GetString("ExportDialog_EstimatedSize_Calculating");
            }
        }

        private IList<BoardPage> GetPagesToExport()
        {
            if (RbCurrentPage.IsChecked == true)
            {
                return new[] { _pages[_currentPageIndex] };
            }
            return _pages;
        }

        private ExportFormat GetSelectedFormat()
        {
            return FormatTabControl.SelectedIndex switch
            {
                0 => RbPng.IsChecked == true ? ExportFormat.Png : ExportFormat.Jpg,
                1 => ExportFormat.Pdf,
                2 => ExportFormat.Wbi,
                _ => ExportFormat.Png
            };
        }

        private object GetExportOptions()
        {
            return FormatTabControl.SelectedIndex switch
            {
                0 => GetImageOptions(),
                1 => GetPdfOptions(),
                2 => GetWbiOptions(),
                _ => GetImageOptions()
            };
        }

        private ImageExportOptions GetImageOptions()
        {
            int.TryParse(TxtWidth.Text, out int width);
            int.TryParse(TxtHeight.Text, out int height);
            width = Math.Clamp(width, 100, 8000);
            height = Math.Clamp(height, 100, 8000);

            return new ImageExportOptions
            {
                Format = RbPng.IsChecked == true ? ExportFormat.Png : ExportFormat.Jpg,
                Width = width,
                Height = height,
                KeepAspectRatio = ChkKeepAspectRatio.IsChecked == true,
                BackgroundColor = _backgroundColor
            };
        }

        private PdfExportOptions GetPdfOptions()
        {
            var orientation = PdfOrientation.Auto;
            if (RbOrientLandscape.IsChecked == true) orientation = PdfOrientation.Landscape;
            else if (RbOrientPortrait.IsChecked == true) orientation = PdfOrientation.Portrait;

            int dpi = CboPdfDpi.SelectedIndex switch
            {
                0 => 96,
                1 => 150,
                2 => 300,
                _ => 150
            };

            return new PdfExportOptions
            {
                Orientation = orientation,
                ScaleMode = RbFitPage.IsChecked == true ? PdfScaleMode.FitPage : PdfScaleMode.FillPage,
                Dpi = dpi
            };
        }

        private WbiExportOptions GetWbiOptions()
        {
            var level = (WbiCompressionLevel)CboCompression.SelectedIndex;

            return new WbiExportOptions
            {
                CompressionLevel = level,
                IncludeImageAssets = ChkIncludeImages.IsChecked == true
            };
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var format = GetSelectedFormat();
            string? filePath = ShowSaveDialog(format);

            if (string.IsNullOrEmpty(filePath))
                return;

            Result = new ExportRequest
            {
                Pages = GetPagesToExport().ToList(),
                Format = format,
                FilePath = filePath,
                ImageOptions = format is ExportFormat.Png or ExportFormat.Jpg ? GetImageOptions() : null,
                PdfOptions = format == ExportFormat.Pdf ? GetPdfOptions() : null,
                WbiOptions = format == ExportFormat.Wbi ? GetWbiOptions() : null,
                BackgroundColor = _backgroundColor
            };

            DialogHost.CloseDialogCommand.Execute(Result, this);
        }

        private string? ShowSaveDialog(ExportFormat format)
        {
            var dialog = new SaveFileDialog();

            switch (format)
            {
                case ExportFormat.Png:
                    dialog.Filter = LocalizationService.Instance.GetString("ExportDialog_SaveFilter_Png");
                    dialog.DefaultExt = ".png";
                    break;
                case ExportFormat.Jpg:
                    dialog.Filter = LocalizationService.Instance.GetString("ExportDialog_SaveFilter_Jpg");
                    dialog.DefaultExt = ".jpg";
                    break;
                case ExportFormat.Pdf:
                    dialog.Filter = LocalizationService.Instance.GetString("ExportDialog_SaveFilter_Pdf");
                    dialog.DefaultExt = ".pdf";
                    break;
                case ExportFormat.Wbi:
                    dialog.Filter = LocalizationService.Instance.Format(
                        "ExportDialog_SaveFilter_Wbi",
                        AppDisplayNames.GetAppNameFromSettings());
                    dialog.DefaultExt = ".wbi";
                    break;
            }

            dialog.FileName = $"{AppDisplayNames.GetAppNameFromSettings()}_Export_{DateTime.Now:yyyyMMdd_HHmmss}";

            if (dialog.ShowDialog() == true)
            {
                return dialog.FileName;
            }

            return null;
        }
    }

    /// <summary>
    /// 导出请求
    /// </summary>
    public sealed class ExportRequest
    {
        public List<BoardPage> Pages { get; set; } = new();
        public ExportFormat Format { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public ImageExportOptions? ImageOptions { get; set; }
        public PdfExportOptions? PdfOptions { get; set; }
        public WbiExportOptions? WbiOptions { get; set; }
        public System.Windows.Media.Color BackgroundColor { get; set; }
    }
}
