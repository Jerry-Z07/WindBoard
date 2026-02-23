using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using WindBoard.Board.Elements;
using WindBoard.Board.Editing;
using WindBoard.Board.Persistence;
using WindBoard.Board.Persistence.Wbix;
using WindBoard.Features.Import.Models;
using WindBoard.Features.Import.Services;
using WindBoard.Features.Import.UI;
using WindBoard.Features.Import.Wbi;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.UI.Common;

namespace WindBoard.Features.Import
{
    /// <summary>
    /// 导入流程编排：负责展示导入 UI，并将用户选择转换为具体的导入操作（元素导入 / WBIX / WBI）。
    /// </summary>
    internal sealed class ImportFlow
    {
        private readonly BoardWorkspace _workspace;
        private readonly Func<(Vector2 cameraWorld, float zoom)> _getViewportState;
        private readonly Action<BoardElement>? _selectElement;

        public ImportFlow(
            BoardWorkspace workspace,
            Func<(Vector2 cameraWorld, float zoom)> getViewportState,
            Action<BoardElement>? selectElement)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _getViewportState = getViewportState ?? throw new ArgumentNullException(nameof(getViewportState));
            _selectElement = selectElement;
        }

        public async Task StartAsync(XamlRoot xamlRoot, IntPtr hwnd)
        {
            if (xamlRoot is null)
            {
                throw new ArgumentNullException(nameof(xamlRoot));
            }

            if (hwnd == IntPtr.Zero)
            {
                await DialogHelpers.ShowMessageAsync(xamlRoot, L10n.Get("Import_Failed_Title"), L10n.Get("Common_WindowHandleFailed_Message"));
                return;
            }

            var dialog = new ImportDialog(hwnd)
            {
                XamlRoot = xamlRoot,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            if (dialog.WbixRequest is ImportWbixRequest wbix)
            {
                AppLog.Info("Import", $"开始导入 WBIX：path='{wbix.File.Path}', mode={wbix.Mode}");
                await ImportWbixAsync(xamlRoot, wbix.File, wbix.Mode);
                return;
            }

            if (dialog.WbiRequest is ImportWbiRequest wbi)
            {
                AppLog.Info("Import", $"开始导入 WBI：path='{wbi.File.Path}', mode={wbi.Mode}");
                await ImportWbiAsync(xamlRoot, wbi.File, wbi.Mode);
                return;
            }

            if (dialog.ElementsRequest is not ImportElementsRequest request)
            {
                return;
            }

            (Vector2 cameraWorld, float zoom) = _getViewportState();
            IReadOnlyList<BoardElement> created = await BoardImportService.ImportElementsAsync(_workspace, cameraWorld, zoom, request);

            if (created.Count > 0)
            {
                // 复刻旧版体验：导入后自动进入选择并选中新对象。
                _selectElement?.Invoke(created[^1]);
            }
        }

        private async Task ImportWbixAsync(XamlRoot xamlRoot, StorageFile file, ImportWbixMode mode)
        {
            if (mode == ImportWbixMode.ReplaceCurrentPage)
            {
                bool confirmed = await ConfirmReplaceCurrentPageRiskAsync(xamlRoot);
                if (!confirmed)
                {
                    return;
                }
            }

            var serializer = new WbixWorkspaceSerializer();

            try
            {
                await DialogHelpers.RunBusyAsync(xamlRoot, L10n.Get("Import_Wbix_Busy_Title"), L10n.Get("Import_Wbix_Busy_Message"), async () =>
                {
                    BoardWorkspaceSnapshot snapshot = await Task.Run(async () =>
                    {
                        await using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        return await serializer.LoadAsync(stream);
                    });

                    List<BoardPage> pages = BoardWorkspaceSnapshotApplier.CreatePages(snapshot);

                    if (mode == ImportWbixMode.ReplaceCurrentPage)
                    {
                        AppLog.Info("WBIX", $"替换工作区：pages={pages.Count}, currentIndex={snapshot.CurrentIndex}");
                        if (pages.Count == 0)
                        {
                            AppLog.Warn("WBIX", "pages=0，忽略导入。");
                            return;
                        }

                        int insertIndex = _workspace.CurrentIndex;
                        int replaceImportCurrent = Math.Clamp(snapshot.CurrentIndex, 0, Math.Max(0, pages.Count - 1));

                        AppLog.Info("WBIX", $"覆盖当前页并插入：workspaceCurrent={insertIndex}, importPages={pages.Count}, importCurrent={replaceImportCurrent}");

                        // 覆盖当前页：用导入文件的第 1 页替换当前页，然后把剩余页插入到其后。
                        _workspace.ReplacePageAt(insertIndex, pages[0]);

                        if (pages.Count > 1)
                        {
                            _workspace.InsertPages(insertIndex + 1, pages.GetRange(1, pages.Count - 1), switchToFirstInsertedPage: false);
                        }

                        int replaceTargetIndex = Math.Clamp(insertIndex + replaceImportCurrent, 0, Math.Max(0, _workspace.Pages.Count - 1));
                        AppLog.Info("WBIX", $"覆盖导入完成：switchTo={replaceTargetIndex}, pagesAfter={_workspace.Pages.Count}");
                        _workspace.SetCurrentIndex(replaceTargetIndex);
                        return;
                    }

                    int startIndex = _workspace.AppendPages(pages, switchToFirstAppendedPage: false);
                    int importCurrent = Math.Clamp(snapshot.CurrentIndex, 0, Math.Max(0, pages.Count - 1));
                    int targetIndex = Math.Clamp(startIndex + importCurrent, 0, Math.Max(0, _workspace.Pages.Count - 1));
                    AppLog.Info("WBIX", $"追加页面：startIndex={startIndex}, pages={pages.Count}, switchTo={targetIndex}");
                    _workspace.SetCurrentIndex(targetIndex);
                }, logTag: "Import");
            }
            catch (Exception ex)
            {
                AppLog.Error("WBIX", $"导入失败：'{file.Path}'", ex);
                await DialogHelpers.ShowMessageAsync(xamlRoot, L10n.Get("Import_Failed_Title"), L10n.Get("Import_Wbix_ParseFailed_Message"));
            }
        }

        private async Task ImportWbiAsync(XamlRoot xamlRoot, StorageFile file, ImportWbixMode mode)
        {
            if (mode == ImportWbixMode.ReplaceCurrentPage)
            {
                bool confirmed = await ConfirmReplaceCurrentPageRiskAsync(xamlRoot);
                if (!confirmed)
                {
                    return;
                }
            }

            var importer = new WbiWorkspaceImporter();
            List<string>? missingResources = null;

            try
            {
                await DialogHelpers.RunBusyAsync(xamlRoot, L10n.Get("Import_Wbix_Busy_Title"), L10n.Get("Import_Wbix_Busy_Message"), async () =>
                {
                    WbiWorkspaceImportResult importResult = await Task.Run(() => importer.ImportAsync(file.Path));
                    if (!importResult.Success)
                    {
                        throw new InvalidDataException(importResult.ErrorMessage ?? L10n.Get("Import_Wbix_ParseFailed_Message"));
                    }

                    missingResources = importResult.MissingResources;
                    List<BoardPage> pages = importResult.Pages;

                    // WBI 不记录 currentIndex：按旧版行为默认视为 0。
                    const int importCurrent = 0;

                    if (mode == ImportWbixMode.ReplaceCurrentPage)
                    {
                        AppLog.Info("WBI", $"替换工作区：pages={pages.Count}");
                        if (pages.Count == 0)
                        {
                            AppLog.Warn("WBI", "pages=0，忽略导入。");
                            return;
                        }

                        int insertIndex = _workspace.CurrentIndex;
                        AppLog.Info("WBI", $"覆盖当前页并插入：workspaceCurrent={insertIndex}, importPages={pages.Count}");

                        // 覆盖当前页：用导入文件的第 1 页替换当前页，然后把剩余页插入到其后。
                        _workspace.ReplacePageAt(insertIndex, pages[0]);

                        if (pages.Count > 1)
                        {
                            _workspace.InsertPages(insertIndex + 1, pages.GetRange(1, pages.Count - 1), switchToFirstInsertedPage: false);
                        }

                        int replaceTargetIndex = Math.Clamp(insertIndex + importCurrent, 0, Math.Max(0, _workspace.Pages.Count - 1));
                        AppLog.Info("WBI", $"覆盖导入完成：switchTo={replaceTargetIndex}, pagesAfter={_workspace.Pages.Count}");
                        _workspace.SetCurrentIndex(replaceTargetIndex);
                        return;
                    }

                    int startIndex = _workspace.AppendPages(pages, switchToFirstAppendedPage: false);
                    int targetIndex = Math.Clamp(startIndex + importCurrent, 0, Math.Max(0, _workspace.Pages.Count - 1));
                    AppLog.Info("WBI", $"追加页面：startIndex={startIndex}, pages={pages.Count}, switchTo={targetIndex}");
                    _workspace.SetCurrentIndex(targetIndex);
                }, logTag: "Import");
            }
            catch (Exception ex)
            {
                AppLog.Error("WBI", $"导入失败：'{file.Path}'", ex);
                await DialogHelpers.ShowMessageAsync(xamlRoot, L10n.Get("Import_Failed_Title"), L10n.Get("Import_Wbix_ParseFailed_Message"));
                return;
            }

            if (missingResources is { Count: > 0 })
            {
                await NotifyMissingResourcesAsync(xamlRoot, missingResources);
            }
        }

        private static async Task NotifyMissingResourcesAsync(XamlRoot xamlRoot, List<string> missingResources)
        {
            AppLog.Warn("WBI", $"导入存在缺失资源：count={missingResources.Count}");

            int take = Math.Min(8, missingResources.Count);
            string detail = string.Join(Environment.NewLine, missingResources.GetRange(0, take));
            string more = missingResources.Count > take ? $"{Environment.NewLine}…（还有 {missingResources.Count - take} 项，详见日志）" : string.Empty;

            await DialogHelpers.ShowMessageAsync(
                xamlRoot,
                L10n.Get("Import_Tip_Title"),
                $"导入完成，但有部分资源未找到或无法读取：{Environment.NewLine}{detail}{more}");
        }

        private static async Task<bool> ConfirmReplaceCurrentPageRiskAsync(XamlRoot xamlRoot)
        {
            var dialog = new ContentDialog
            {
                Title = L10n.Get("Import_Risk_Title"),
                Content = L10n.Get("Import_Risk_ReplaceCurrentPage_Content"),
                PrimaryButtonText = L10n.Get("Import_Risk_ContinueOverwrite"),
                CloseButtonText = L10n.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot,
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
    }
}
