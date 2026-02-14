using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace WindBoard.Reminders
{
    /// <summary>
    /// 窗口显示模式判定（全屏/窗口化等）。
    /// 
    /// 说明：目前 WindBoard 还没有完整的“全屏模式”入口，这里先预留判断逻辑；
    /// 后续只需要改这里即可切换“全屏走弹条”策略。
    /// </summary>
    internal static class WindowDisplayModeHelper
    {
        internal static bool IsFullScreen(Window window)
        {
            if (window is null)
            {
                return false;
            }

            try
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
                return appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
            }
            catch
            {
                // 判定失败则按“非全屏”处理，避免影响提醒主流程。
                return false;
            }
        }
    }
}

