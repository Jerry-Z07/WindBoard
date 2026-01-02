using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using WindBoard.Models.Export;
using WindBoard.Services;
using WindBoard.Services.Export;
using WindBoard.Views.Dialogs;

namespace WindBoard
{
    public partial class MainWindow
    {
        private readonly ExportService _exportService = new();

        private async void MenuExport_Click(object sender, RoutedEventArgs e)
        {
            // 关闭更多菜单
            if (_popupMoreMenu != null)
                _popupMoreMenu.IsOpen = false;

            // 保存当前页面状态
            _pageService?.SaveCurrentPage();

            // 获取背景色
            var backgroundColor = GetCanvasBackgroundColor();

            // 显示导出对话框
            if (_pageService == null)
            {
                ShowMessage("没有可导出的内容");
                return;
            }

            var pages = _pageService.Pages;
            if (pages.Count == 0)
            {
                ShowMessage("没有可导出的内容");
                return;
            }

            var dialog = new ExportDialog(pages, _pageService.CurrentPageIndex, backgroundColor);
            var result = await DialogHost.Show(dialog, "MainDialogHost");

            if (result is ExportRequest request)
            {
                await ExecuteExportAsync(request);
            }
        }

        private async Task ExecuteExportAsync(ExportRequest request)
        {
            try
            {
                var progress = new Progress<ExportProgress>(p =>
                {
                    // 可以在这里更新进度 UI
                });

                switch (request.Format)
                {
                    case ExportFormat.Png:
                    case ExportFormat.Jpg:
                        await ExportToImageAsync(request, progress);
                        break;
                    case ExportFormat.Pdf:
                        await ExportToPdfAsync(request, progress);
                        break;
                    case ExportFormat.Wbi:
                        await ExportToWbiAsync(request, progress);
                        break;
                }

                ShowExportSuccessMessage(request.FilePath);
            }
            catch (OperationCanceledException)
            {
                ShowMessage("导出已取消");
            }
            catch (Exception ex)
            {
                ShowMessage($"导出失败: {ex.Message}");
            }
        }

        private async Task ExportToImageAsync(ExportRequest request, IProgress<ExportProgress> progress)
        {
            if (request.ImageOptions == null) return;

            if (request.Pages.Count == 1)
            {
                // 单页直接导出到指定文件
                var data = request.Format == ExportFormat.Png
                    ? new ExportRenderer().RenderPageToPng(request.Pages[0], request.ImageOptions)
                    : new ExportRenderer().RenderPageToJpeg(request.Pages[0], request.ImageOptions);

                await Task.Run(() => File.WriteAllBytes(request.FilePath, data));
            }
            else
            {
                // 多页导出到文件夹
                string folder = Path.GetDirectoryName(request.FilePath) ?? "";
                string prefix = Path.GetFileNameWithoutExtension(request.FilePath);

                await _exportService.ExportToImageAsync(
                    request.Pages,
                    folder,
                    request.ImageOptions,
                    prefix,
                    progress);
            }
        }

        private async Task ExportToPdfAsync(ExportRequest request, IProgress<ExportProgress> progress)
        {
            if (request.PdfOptions == null) return;

            await _exportService.ExportToPdfAsync(
                request.Pages,
                request.FilePath,
                request.PdfOptions,
                request.BackgroundColor,
                progress);
        }

        private async Task ExportToWbiAsync(ExportRequest request, IProgress<ExportProgress> progress)
        {
            if (request.WbiOptions == null) return;

            await _exportService.ExportToWbiAsync(
                request.Pages,
                request.FilePath,
                request.WbiOptions,
                progress);
        }

        private Color GetCanvasBackgroundColor()
        {
            // 尝试从 CanvasHost 获取背景色
            if (CanvasHost.Background is SolidColorBrush brush)
            {
                return brush.Color;
            }

            // 默认背景色
            return Color.FromRgb(0x0F, 0x12, 0x16);
        }

        private void ShowExportSuccessMessage(string filePath)
        {
            string message = $"导出成功！\n\n文件位置: {filePath}";

            // 询问是否打开文件位置
            var result = MessageBox.Show(
                message + "\n\n是否打开文件所在位置？",
                "导出完成",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    string? folder = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                    }
                }
                catch
                {
                    // 忽略打开文件夹失败
                }
            }
        }

        private void ShowMessage(string message)
        {
            string caption = string.IsNullOrWhiteSpace(WindowTitle) ? AppDisplayNames.GetAppNameFromSettings() : WindowTitle;
            MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
