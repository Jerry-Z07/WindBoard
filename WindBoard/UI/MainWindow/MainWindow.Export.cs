using System;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using WindBoard.Features.Export;
using WindBoard.Localization;
using WindBoard.Logging;
using WindBoard.UI.Common;

namespace WindBoard
{
    /// <summary>
    /// 主窗口：导出入口（具体逻辑在 Features/Export）。
    /// </summary>
    public sealed partial class MainWindow
    {
        private async Task StartExportAsync()
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
                    await DialogHelpers.ShowMessageAsync(xamlRoot, L10n.Get("Export_Failed_Title"), L10n.Get("Common_WindowHandleFailed_Message"));
                    return;
                }

                var flow = new ExportFlow(
                    _workspace,
                    getFallbackViewportSizeDip: () =>
                    {
                        // 使用当前画布控件的实际尺寸作为“空页面导出尺寸”的兜底。
                        // 注意：ActualWidth/ActualHeight 的单位是 DIP。
                        float w = (float)Math.Max(1.0, BoardCanvas.ActualWidth);
                        float h = (float)Math.Max(1.0, BoardCanvas.ActualHeight);
                        return new Vector2(w, h);
                    },
                    getCanvasBackgroundColor: () => BoardCanvas.CanvasBackgroundColor);

                await flow.StartAsync(xamlRoot, hwnd);
            }
            catch (Exception ex)
            {
                AppLog.Error("Export", "导出异常。", ex);
                await DialogHelpers.ShowMessageAsync(xamlRoot, L10n.Get("Export_Failed_Title"), ex.Message);
            }
        }
    }
}

