using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace WindBoard.Features.ScreenAnnotation.Interop
{
    /// <summary>
    /// 屏幕批注窗口相关的原生互操作封装。
    /// </summary>
    internal static class ScreenAnnotationWindowInterop
    {
        private const int GwlStyle = -16;
        private const int GwlExStyle = -20;
        private const uint WsCaption = 0x00C00000;
        private const uint WsThickFrame = 0x00040000;
        private const uint WsMinimizeBox = 0x00020000;
        private const uint WsMaximizeBox = 0x00010000;
        private const uint WsSysMenu = 0x00080000;
        private const uint WsExTransparent = 0x00000020;
        private const uint WsExToolWindow = 0x00000080;
        private const uint WsExLayered = 0x00080000;

        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpShowWindow = 0x0040;

        private const int SwMinimize = 6;
        private const int SwRestore = 9;
        private const int SwShow = 5;
        private const int EInvalidArg = unchecked((int)0x80070057);

        private const uint LwaAlpha = 0x00000002;
        private const uint MonitorDefaultToNearest = 2;
        internal const uint DwmWindowCornerPreferenceAttribute = 33;
        internal const uint DwmBorderColorAttribute = 34;
        internal const uint DwmWindowCornerPreferenceDoNotRound = 1;
        internal const uint DwmColorNone = 0xFFFFFFFE;

        private static readonly IntPtr HwndTopMost = new(-1);
        private const uint BorderlessWindowStyleMask = WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsSysMenu;

        internal static IntPtr GetWindowHandle(Window window)
        {
            if (window is null)
            {
                return IntPtr.Zero;
            }

            try
            {
                return WinRT.Interop.WindowNative.GetWindowHandle(window);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        internal static AppWindow? TryGetAppWindow(Window window)
        {
            return TryGetAppWindow(GetWindowHandle(window));
        }

        internal static AppWindow? TryGetAppWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                return AppWindow.GetFromWindowId(windowId);
            }
            catch
            {
                return null;
            }
        }

        internal static bool TryConfigureBorderlessWindow(AppWindow appWindow, RectInt32 bounds, out string? error)
        {
            if (appWindow is null)
            {
                error = "AppWindow is null.";
                return false;
            }

            try
            {
                if (appWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsResizable = false;
                    presenter.IsMaximizable = false;
                    presenter.IsMinimizable = false;
                    presenter.SetBorderAndTitleBar(false, false);
                }

                appWindow.IsShownInSwitchers = false;
                appWindow.MoveAndResize(bounds);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static bool TryPrepareAnnotationWindow(IntPtr hwnd, out string? error)
        {
            if (!TryUpdateStandardStyle(hwnd, BuildBorderlessWindowStyle, out error))
            {
                return false;
            }

            if (!TryUpdateExtendedStyle(hwnd, BuildAnnotationWindowExtendedStyle, out error))
            {
                return false;
            }

            if (!SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha))
            {
                error = $"SetLayeredWindowAttributes failed: lastError={Marshal.GetLastWin32Error()}";
                return false;
            }

            if (!TrySuppressWindowBorder(hwnd, out error))
            {
                return false;
            }

            return TrySetTopMost(hwnd, out error);
        }

        internal static bool TryPrepareToolbarWindow(IntPtr hwnd, out string? error)
        {
            if (!TryUpdateStandardStyle(hwnd, BuildBorderlessWindowStyle, out error))
            {
                return false;
            }

            if (!TryUpdateExtendedStyle(hwnd, BuildToolbarWindowExtendedStyle, out error))
            {
                return false;
            }

            if (!SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha))
            {
                error = $"SetLayeredWindowAttributes failed: lastError={Marshal.GetLastWin32Error()}";
                return false;
            }

            if (!TrySuppressWindowBorder(hwnd, out error))
            {
                return false;
            }

            return TrySetTopMost(hwnd, out error);
        }

        internal static bool TryPromoteWindowToTopMost(IntPtr hwnd, out string? error)
        {
            return TrySetTopMost(hwnd, out error);
        }

        internal static bool TryPlaceWindowBehind(IntPtr hwnd, IntPtr behindWindowHwnd, out string? error)
        {
            if (hwnd == IntPtr.Zero)
            {
                error = "Window handle is zero.";
                return false;
            }

            if (behindWindowHwnd == IntPtr.Zero)
            {
                error = "Behind window handle is zero.";
                return false;
            }

            if (!SetWindowPos(
                hwnd,
                behindWindowHwnd,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow))
            {
                error = $"SetWindowPos(relative z-order) failed: lastError={Marshal.GetLastWin32Error()}";
                return false;
            }

            error = null;
            return true;
        }

        internal static bool TrySetPassThrough(IntPtr hwnd, bool enabled, out string? error)
        {
            return TryUpdateExtendedStyle(
                hwnd,
                addFlags: enabled ? WsExTransparent : 0,
                removeFlags: enabled ? 0 : WsExTransparent,
                out error);
        }

        /// <summary>
        /// 仅依赖 <c>OverlappedPresenter.SetBorderAndTitleBar(false, false)</c> 在部分 WinUI 3 场景下
        /// 仍会保留非客户区边框，这里额外移除 Win32 标准窗口样式中的边框相关位。
        /// </summary>
        internal static uint BuildBorderlessWindowStyle(uint currentStyle)
        {
            return currentStyle & ~BorderlessWindowStyleMask;
        }

        internal static uint BuildAnnotationWindowExtendedStyle(uint currentStyle)
        {
            return BuildUpdatedStyle(currentStyle, WsExToolWindow | WsExLayered, removeFlags: 0);
        }

        internal static uint BuildToolbarWindowExtendedStyle(uint currentStyle)
        {
            return BuildUpdatedStyle(currentStyle, WsExToolWindow | WsExLayered, removeFlags: 0);
        }

        internal static bool TryMinimizeWindow(IntPtr hwnd, out string? error)
        {
            if (hwnd == IntPtr.Zero)
            {
                error = "Window handle is zero.";
                return false;
            }

            ShowWindow(hwnd, SwMinimize);
            error = null;
            return true;
        }

        internal static bool TryRestoreWindow(IntPtr hwnd, out string? error)
        {
            if (hwnd == IntPtr.Zero)
            {
                error = "Window handle is zero.";
                return false;
            }

            ShowWindow(hwnd, SwRestore);
            error = null;
            return true;
        }

        internal static bool TryActivateWindow(IntPtr hwnd, out string? error)
        {
            if (hwnd == IntPtr.Zero)
            {
                error = "Window handle is zero.";
                return false;
            }

            ShowWindow(hwnd, SwShow);
            SetForegroundWindow(hwnd);
            error = null;
            return true;
        }

        internal static bool TryGetCursorScreenPosition(out PointInt32 point)
        {
            if (!GetCursorPos(out Point nativePoint))
            {
                point = default;
                return false;
            }

            point = new PointInt32(nativePoint.X, nativePoint.Y);
            return true;
        }

        internal static bool TryGetWindowRect(IntPtr hwnd, out RectInt32 rect)
        {
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out Rect nativeRect))
            {
                rect = default;
                return false;
            }

            rect = new RectInt32(
                nativeRect.Left,
                nativeRect.Top,
                nativeRect.Right - nativeRect.Left,
                nativeRect.Bottom - nativeRect.Top);
            return true;
        }

        internal static bool TryGetMonitorBounds(IntPtr ownerHwnd, out RectInt32 bounds, out RectInt32 workArea, out nint monitorHandle, out string? error)
        {
            bounds = default;
            workArea = default;
            monitorHandle = IntPtr.Zero;

            if (ownerHwnd == IntPtr.Zero)
            {
                error = "Owner window handle is zero.";
                return false;
            }

            monitorHandle = MonitorFromWindow(ownerHwnd, MonitorDefaultToNearest);
            if (monitorHandle == IntPtr.Zero)
            {
                error = $"MonitorFromWindow failed: lastError={Marshal.GetLastWin32Error()}";
                return false;
            }

            var monitorInfo = new MonitorInfo
            {
                Size = Marshal.SizeOf<MonitorInfo>(),
            };

            if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
            {
                error = $"GetMonitorInfo failed: lastError={Marshal.GetLastWin32Error()}";
                return false;
            }

            bounds = ToRectInt32(monitorInfo.MonitorRect);
            workArea = ToRectInt32(monitorInfo.WorkRect);
            error = null;
            return true;
        }

        internal static bool TryReadWindowPos(nint lParam, out IntPtr insertAfterHwnd, out uint flags)
        {
            insertAfterHwnd = IntPtr.Zero;
            flags = 0;

            if (lParam == 0)
            {
                return false;
            }

            WindowPos windowPos = Marshal.PtrToStructure<WindowPos>((IntPtr)lParam);
            insertAfterHwnd = windowPos.InsertAfter;
            flags = windowPos.Flags;
            return true;
        }

        private static bool TrySetTopMost(IntPtr hwnd, out string? error)
        {
            if (!SetWindowPos(
                hwnd,
                HwndTopMost,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged | SwpShowWindow))
            {
                error = $"SetWindowPos(HWND_TOPMOST) failed: lastError={Marshal.GetLastWin32Error()}";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TrySuppressWindowBorder(IntPtr hwnd, out string? error)
        {
            if (hwnd == IntPtr.Zero)
            {
                error = "Window handle is zero.";
                return false;
            }

            // 在 Win11 上，单纯移除 Win32 样式位仍可能残留系统圆角轮廓与描边；
            // 这里尽力关闭圆角偏好并把边框颜色设为 NONE。
            uint cornerPreference = DwmWindowCornerPreferenceDoNotRound;
            int cornerResult = DwmSetWindowAttribute(
                hwnd,
                DwmWindowCornerPreferenceAttribute,
                ref cornerPreference,
                (uint)Marshal.SizeOf<uint>());
            if (cornerResult != 0 && cornerResult != EInvalidArg)
            {
                error = $"DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE) failed: hr=0x{cornerResult:X8}";
                return false;
            }

            uint borderColor = DwmColorNone;
            int result = DwmSetWindowAttribute(hwnd, DwmBorderColorAttribute, ref borderColor, (uint)Marshal.SizeOf<uint>());
            if (result == 0 || result == EInvalidArg)
            {
                // Win11 22000+ 可通过 DWMWA_BORDER_COLOR=DWMWA_COLOR_NONE 隐藏系统描边；
                // 对不支持该属性的系统（如部分 Win10 环境）退化为忽略，避免影响窗口初始化。
                error = null;
                return true;
            }

            error = $"DwmSetWindowAttribute(DWMWA_BORDER_COLOR) failed: hr=0x{result:X8}";
            return false;
        }

        private static bool TryUpdateStandardStyle(IntPtr hwnd, Func<uint, uint> updateStyle, out string? error)
        {
            return TryUpdateWindowStyle(hwnd, GwlStyle, updateStyle, styleName: "style", out error);
        }

        private static bool TryUpdateExtendedStyle(IntPtr hwnd, Func<uint, uint> updateStyle, out string? error)
        {
            return TryUpdateWindowStyle(hwnd, GwlExStyle, updateStyle, styleName: "exStyle", out error);
        }

        private static bool TryUpdateExtendedStyle(IntPtr hwnd, uint addFlags, uint removeFlags, out string? error)
        {
            return TryUpdateExtendedStyle(
                hwnd,
                currentStyle => BuildUpdatedStyle(currentStyle, addFlags, removeFlags),
                out error);
        }

        private static uint BuildUpdatedStyle(uint currentStyle, uint addFlags, uint removeFlags)
        {
            return (currentStyle | addFlags) & ~removeFlags;
        }

        private static bool TryUpdateWindowStyle(
            IntPtr hwnd,
            int index,
            Func<uint, uint> updateStyle,
            string styleName,
            out string? error)
        {
            if (hwnd == IntPtr.Zero)
            {
                error = "Window handle is zero.";
                return false;
            }

            SetLastError(0);
            nint currentStyleValue = GetWindowLongPtr(hwnd, index);
            int currentError = Marshal.GetLastWin32Error();
            if (currentStyleValue == 0 && currentError != 0)
            {
                error = $"GetWindowLongPtr({styleName}) failed: lastError={currentError}";
                return false;
            }

            uint currentStyle = unchecked((uint)currentStyleValue.ToInt64());
            uint newStyle = updateStyle(currentStyle);
            if (newStyle == currentStyle)
            {
                error = null;
                return true;
            }

            SetLastError(0);
            _ = SetWindowLongPtr(hwnd, index, (nint)newStyle);
            int setError = Marshal.GetLastWin32Error();
            if (setError != 0)
            {
                error = $"SetWindowLongPtr({styleName}) failed: lastError={setError}";
                return false;
            }

            if (!SetWindowPos(
                hwnd,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpShowWindow))
            {
                error = $"SetWindowPos(style refresh) failed: lastError={Marshal.GetLastWin32Error()}";
                return false;
            }

            error = null;
            return true;
        }

        private static RectInt32 ToRectInt32(Rect rect)
        {
            return new RectInt32(
                rect.Left,
                rect.Top,
                rect.Right - rect.Left,
                rect.Bottom - rect.Top);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(
            IntPtr hwnd,
            uint colorKey,
            byte alpha,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            uint dwAttribute,
            ref uint pvAttribute,
            uint cbAttribute);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo monitorInfo);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern nint GetWindowLongPtr(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern nint SetWindowLongPtr(IntPtr hwnd, int index, nint newLong);

        [DllImport("kernel32.dll")]
        private static extern void SetLastError(int dwErrCode);

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect MonitorRect;
            public Rect WorkRect;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPos
        {
            public IntPtr Hwnd;
            public IntPtr InsertAfter;
            public int X;
            public int Y;
            public int Cx;
            public int Cy;
            public uint Flags;
        }
    }
}
