using System;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using WindBoard.Board.Elements;
using WindBoard.Features.Import;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.UI.Common;

namespace WindBoard
{
    /// <summary>
    /// 主窗口：导入入口（具体逻辑在 Features/Import）。
    /// </summary>
    public sealed partial class MainWindow
    {
        private async Task StartImportAsync()
        {
            XamlRoot? xamlRoot = TryGetDialogXamlRoot();
            if (xamlRoot is null)
            {
                return;
            }

            try
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (hwnd == IntPtr.Zero)
                {
                    await DialogHelpers.ShowMessageAsync(xamlRoot, L10n.Get("Import_Failed_Title"), L10n.Get("Common_WindowHandleFailed_Message"));
                    return;
                }

                var flow = new ImportFlow(
                    _workspace,
                    getViewportState: () =>
                    {
                        BoardCanvas.GetViewportState(out Vector2 cameraWorld, out float zoom);
                        return (cameraWorld, zoom);
                    },
                    selectElement: SelectImportedElement);

                await flow.StartAsync(xamlRoot, this, hwnd);
            }
            catch (Exception ex)
            {
                AppLog.Error("Import", "导入异常。", ex);
                await DialogHelpers.ShowMessageAsync(xamlRoot, L10n.Get("Import_Failed_Title"), ex.Message);
            }
        }

        private void SelectImportedElement(BoardElement element)
        {
            // 复刻旧版体验：导入后自动进入选择并选中新对象。
            ApplyToolSelection(Interaction.BoardTool.Select);
            BoardCanvas.SetSelectedElement(element);
        }
    }
}

