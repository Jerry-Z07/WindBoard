using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.Storage;
using WindBoard.Board.Editing;
using WindBoard.Board.Persistence;
using WindBoard.Features.Export.Models;
using WindBoard.Features.Export.Services;
using WindBoard.Features.Export.UI;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.UI.Common;

namespace WindBoard.Features.Export
{
    /// <summary>
    /// 导出流程编排：负责展示导出 UI，并将用户选择转换为具体的导出操作（PNG / PDF / WBIX）。
    /// </summary>
    internal sealed class ExportFlow
    {
        private readonly BoardWorkspace _workspace;
        private readonly Func<(Vector2 cameraWorld, float zoom)> _getViewportState;
        private readonly Func<Vector2> _getFallbackViewportSizeDip;
        private readonly Func<Windows.UI.Color> _getCanvasBackgroundColor;

        private readonly IBoardExportService _exportService = new BoardExportService();

        public ExportFlow(
            BoardWorkspace workspace,
            Func<(Vector2 cameraWorld, float zoom)> getViewportState,
            Func<Vector2> getFallbackViewportSizeDip,
            Func<Windows.UI.Color> getCanvasBackgroundColor)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _getViewportState = getViewportState ?? throw new ArgumentNullException(nameof(getViewportState));
            _getFallbackViewportSizeDip = getFallbackViewportSizeDip ?? throw new ArgumentNullException(nameof(getFallbackViewportSizeDip));
            _getCanvasBackgroundColor = getCanvasBackgroundColor ?? throw new ArgumentNullException(nameof(getCanvasBackgroundColor));
        }

        public async Task StartAsync(XamlRoot xamlRoot, IntPtr hwnd)
        {
            if (xamlRoot is null)
            {
                throw new ArgumentNullException(nameof(xamlRoot));
            }

            if (hwnd == IntPtr.Zero)
            {
                await DialogHelpers.ShowMessageAsync(xamlRoot, L10n.Get("Export_Failed_Title"), L10n.Get("Common_WindowHandleFailed_Message"));
                return;
            }

            ExportDialogSelection? selection = null;
            try
            {
                selection = await ExportDialog.ShowAsync(xamlRoot);
                if (selection is null)
                {
                    return;
                }

                AppLog.Info("Export", $"开始导出：format={selection.Format}, scope={selection.PageScope}, range='{selection.PageRangeText}', dpi={selection.Dpi}, paddingDip={selection.PaddingDip}");

                // 导出建议基于快照进行：避免导出耗时期间用户继续编辑导致数据竞争或导出内容不一致。
                Vector2 fallbackViewportSizeDip = _getFallbackViewportSizeDip();
                (Vector2 cameraWorld, float zoom) = _getViewportState();

                BoardWorkspaceSnapshot snapshot = BoardWorkspaceSnapshotConverter.CreateSnapshot(
                    _workspace,
                    viewportCameraWorld: cameraWorld,
                    viewportZoom: zoom,
                    viewportSizeDip: fallbackViewportSizeDip);

                var rasterOptions = new BoardRasterExportOptions(
                    Dpi: selection.Dpi,
                    PaddingDip: selection.PaddingDip,
                    BackgroundColor: _getCanvasBackgroundColor(),
                    FallbackViewportSizeDip: fallbackViewportSizeDip,
                    FixedFrame: selection.Format == ExportFormat.Png ? selection.PngFixedFrame : null);

                // WBIX 固定导出整个工作区：忽略页范围选择。
                if (selection.Format == ExportFormat.Wbix)
                {
                    await ExportWbixAsync(xamlRoot, hwnd, snapshot);
                    return;
                }

                List<int> pageIndices;
                if (!TryResolvePageIndices(selection, pageCount: snapshot.Pages.Count, currentIndex: _workspace.CurrentIndex, out pageIndices, out string pageError))
                {
                    await DialogHelpers.ShowMessageAsync(xamlRoot, L10n.Get("Export_PageRangeError_Title"), pageError);
                    return;
                }

                switch (selection.Format)
                {
                    case ExportFormat.Pdf:
                        await ExportPdfAsync(xamlRoot, hwnd, snapshot, pageIndices, rasterOptions);
                        return;

                    case ExportFormat.Png:
                        await ExportPngAsync(xamlRoot, hwnd, snapshot, pageIndices, rasterOptions);
                        return;

                    default:
                        await DialogHelpers.ShowMessageAsync(xamlRoot, L10n.Get("Export_Failed_Title"), L10n.Get("Export_UnknownFormat_Message"));
                        return;
                }
            }
            catch (Exception ex)
            {
                string message = selection is null
                    ? "导出异常。"
                    : $"导出异常：format={selection.Format}, scope={selection.PageScope}, range='{selection.PageRangeText}'";

                AppLog.Error("Export", message, ex);
                await DialogHelpers.ShowMessageAsync(xamlRoot, L10n.Get("Export_Failed_Title"), ex.Message);
            }
        }

        private static bool TryResolvePageIndices(ExportDialogSelection selection, int pageCount, int currentIndex, out List<int> pageIndices, out string errorMessage)
        {
            pageIndices = new List<int>();
            errorMessage = string.Empty;

            if (pageCount <= 0)
            {
                errorMessage = L10n.Get("Export_NoPages_Message");
                return false;
            }

            switch (selection.PageScope)
            {
                case ExportPageScope.Current:
                    pageIndices.Add(currentIndex);
                    return true;

                case ExportPageScope.All:
                    for (int i = 0; i < pageCount; i++)
                    {
                        pageIndices.Add(i);
                    }

                    return true;

                case ExportPageScope.Range:
                    return PageRangeParser.TryParse(selection.PageRangeText, pageCount, out pageIndices, out errorMessage);

                default:
                    errorMessage = L10n.Get("Export_UnknownPageScope_Message");
                    return false;
            }
        }

        private async Task ExportWbixAsync(XamlRoot xamlRoot, IntPtr hwnd, BoardWorkspaceSnapshot snapshot)
        {
            StorageFile? file = await ExportPickers.PickSaveFileWithOverwriteConfirmAsync(xamlRoot, hwnd, ExportFormat.Wbix);
            if (file is null)
            {
                return;
            }

            await DialogHelpers.RunBusyAsync(xamlRoot, L10n.Get("Export_Busy_Wbix_Title"), L10n.Get("Export_Busy_Default_Message"), async () =>
            {
                AppLog.Info("Export", $"导出 WBIX：path='{file.Path}'");
                await _exportService.ExportWbixAsync(snapshot, file.Path);
            }, logTag: "Export");

            AppLog.Info("Export", $"导出 WBIX 完成：path='{file.Path}'");
            await DialogHelpers.ShowMessageAsync(xamlRoot, L10n.Get("Export_Completed_Title"), L10n.Format("Export_Completed_File_Fmt", file.Path));
        }

        private async Task ExportPdfAsync(XamlRoot xamlRoot, IntPtr hwnd, BoardWorkspaceSnapshot snapshot, List<int> pageIndices, BoardRasterExportOptions rasterOptions)
        {
            StorageFile? file = await ExportPickers.PickSaveFileWithOverwriteConfirmAsync(xamlRoot, hwnd, ExportFormat.Pdf);
            if (file is null)
            {
                return;
            }

            var options = new BoardPdfExportOptions(rasterOptions);

            await DialogHelpers.RunBusyAsync(xamlRoot, L10n.Get("Export_Busy_Pdf_Title"), L10n.Get("Export_Busy_Default_Message"), async () =>
            {
                AppLog.Info("Export", $"导出 PDF：path='{file.Path}', pages={pageIndices.Count}");
                await _exportService.ExportPdfAsync(snapshot, pageIndices, file.Path, options);
            }, logTag: "Export");

            AppLog.Info("Export", $"导出 PDF 完成：path='{file.Path}'");
            await DialogHelpers.ShowMessageAsync(xamlRoot, L10n.Get("Export_Completed_Title"), L10n.Format("Export_Completed_File_Fmt", file.Path));
        }

        private async Task ExportPngAsync(XamlRoot xamlRoot, IntPtr hwnd, BoardWorkspaceSnapshot snapshot, List<int> pageIndices, BoardRasterExportOptions rasterOptions)
        {
            if (pageIndices.Count == 1)
            {
                StorageFile? file = await ExportPickers.PickSaveFileWithOverwriteConfirmAsync(xamlRoot, hwnd, ExportFormat.Png);
                if (file is null)
                {
                    return;
                }

                await DialogHelpers.RunBusyAsync(xamlRoot, L10n.Get("Export_Busy_Png_Title"), L10n.Get("Export_Busy_Default_Message"), async () =>
                {
                    AppLog.Info("Export", $"导出 PNG：path='{file.Path}', page={pageIndices[0]}");
                    await _exportService.ExportPngAsync(snapshot, pageIndices[0], file.Path, rasterOptions);
                }, logTag: "Export");

                AppLog.Info("Export", $"导出 PNG 完成：path='{file.Path}'");
                await DialogHelpers.ShowMessageAsync(xamlRoot, L10n.Get("Export_Completed_Title"), L10n.Format("Export_Completed_File_Fmt", file.Path));
                return;
            }

            StorageFolder? folder = await ExportPickers.PickFolderAsync(xamlRoot, hwnd);
            if (folder is null)
            {
                return;
            }

            // 多页 PNG：在用户选择的目录下创建“WindBoard-年-月-日”文件夹，内部文件名为“年-月-日-页码”。
            DateTimeOffset now = DateTimeOffset.Now;
            string date = ExportPickers.FormatDate(now);
            string exportFolderPath = Path.Combine(folder.Path, $"WindBoard-{date}");

            if (TryGetPngBatchConflicts(snapshot, pageIndices, exportFolderPath, date, out List<string> conflicts)
                && conflicts.Count > 0)
            {
                bool overwrite = await ExportPickers.ConfirmOverwriteFilesAsync(xamlRoot, exportFolderPath, conflicts);
                if (!overwrite)
                {
                    return;
                }
            }

            await DialogHelpers.RunBusyAsync(xamlRoot, L10n.Get("Export_Busy_Png_Title"), L10n.Get("Export_Busy_Default_Message"), async () =>
            {
                AppLog.Info("Export", $"批量导出 PNG：folder='{exportFolderPath}', pages={pageIndices.Count}");
                await _exportService.ExportPngPagesToFolderAsync(snapshot, pageIndices, exportFolderPath, date, rasterOptions);
            }, logTag: "Export");

            AppLog.Info("Export", $"批量导出 PNG 完成：folder='{exportFolderPath}'");
            await DialogHelpers.ShowMessageAsync(xamlRoot, L10n.Get("Export_Completed_Title"), L10n.Format("Export_Completed_Folder_Fmt", exportFolderPath));
        }

        private static bool TryGetPngBatchConflicts(
            BoardWorkspaceSnapshot snapshot,
            List<int> pageIndices,
            string folderPath,
            string datePrefix,
            out List<string> conflictPaths)
        {
            conflictPaths = new List<string>();

            if (snapshot is null)
            {
                return false;
            }

            if (pageIndices is null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(datePrefix))
            {
                return false;
            }

            foreach (int index in pageIndices)
            {
                if ((uint)index >= (uint)snapshot.Pages.Count)
                {
                    continue;
                }

                string fileName = $"{datePrefix}-{index + 1:000}.png";
                string path = Path.Combine(folderPath, fileName);
                if (File.Exists(path))
                {
                    conflictPaths.Add(path);
                }
            }

            return true;
        }
    }
}
