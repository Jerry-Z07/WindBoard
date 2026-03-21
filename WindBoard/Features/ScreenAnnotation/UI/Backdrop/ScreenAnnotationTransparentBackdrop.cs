using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.UI;
using Windows.UI.Composition;

namespace WindBoard.Features.ScreenAnnotation.UI.Backdrop
{
    /// <summary>
    /// 为屏幕批注窗口提供真正的透明系统背景，避免 WinUI 顶层窗体默认黑底。
    /// </summary>
    internal sealed class ScreenAnnotationTransparentBackdrop : ScreenAnnotationCompositionBrushBackdrop
    {
        private const uint DwmBbEnable = 0x00000001;
        private const uint DwmBbBlurRegion = 0x00000002;
        private const uint WmEraseBkgnd = 0x0014;
        private const uint WmDwmCompositionChanged = 0x031E;

        private readonly IntPtr _hwnd;
        private readonly Color _tintColor;
        private ScreenAnnotationWindowMessageMonitor? _messageMonitor;
        private CompositionColorBrush? _brush;
        private IntPtr _backgroundBrush;

        internal ScreenAnnotationTransparentBackdrop(IntPtr hwnd)
            : this(hwnd, Color.FromArgb(0x00, 0x00, 0x00, 0x00))
        {
        }

        internal ScreenAnnotationTransparentBackdrop(IntPtr hwnd, Color tintColor)
        {
            if (hwnd == IntPtr.Zero)
            {
                throw new ArgumentException("窗口句柄不能为空。", nameof(hwnd));
            }

            _hwnd = hwnd;
            _tintColor = tintColor;
        }

        internal event EventHandler<ScreenAnnotationWindowMessageEventArgs>? WindowMessageObserved;

        protected override CompositionBrush CreateBrush(Compositor compositor)
        {
            _brush = compositor.CreateColorBrush(_tintColor);
            return _brush;
        }

        protected override void OnTargetConnected(
            Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop connectedTarget,
            Microsoft.UI.Xaml.XamlRoot xamlRoot)
        {
            EnsureMessageMonitor();
            ConfigureDwm(_hwnd);

            base.OnTargetConnected(connectedTarget, xamlRoot);

            IntPtr hdc = GetDC(_hwnd);
            if (hdc == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDC 失败。");
            }

            try
            {
                _ = ClearBackground(_hwnd, hdc);
            }
            finally
            {
                _ = ReleaseDC(_hwnd, hdc);
            }
        }

        protected override void OnTargetDisconnected(
            Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop disconnectedTarget)
        {
            if (_messageMonitor is not null)
            {
                _messageMonitor.WindowMessageReceived -= OnWindowMessageReceived;
                _messageMonitor.Dispose();
                _messageMonitor = null;
            }

            _brush = null;

            if (_backgroundBrush != IntPtr.Zero)
            {
                _ = DeleteObject(_backgroundBrush);
                _backgroundBrush = IntPtr.Zero;
            }

            base.OnTargetDisconnected(disconnectedTarget);
        }

        private void EnsureMessageMonitor()
        {
            if (_messageMonitor is not null)
            {
                return;
            }

            _messageMonitor = new ScreenAnnotationWindowMessageMonitor(_hwnd);
            _messageMonitor.WindowMessageReceived += OnWindowMessageReceived;
            _messageMonitor.EnsureAttached();
        }

        private void OnWindowMessageReceived(object? sender, ScreenAnnotationWindowMessageEventArgs e)
        {
            if (e.MessageId == WmEraseBkgnd)
            {
                if (ClearBackground(e.Hwnd, (IntPtr)e.WParam))
                {
                    e.Result = new IntPtr(1);
                    e.Handled = true;
                }

                return;
            }

            if (e.MessageId == WmDwmCompositionChanged)
            {
                ConfigureDwm(e.Hwnd);
                e.Result = IntPtr.Zero;
                e.Handled = true;
            }

            WindowMessageObserved?.Invoke(this, e);
        }

        private void ConfigureDwm(IntPtr hwnd)
        {
            var margins = new Margins();
            int extendResult = DwmExtendFrameIntoClientArea(hwnd, ref margins);
            if (extendResult != 0)
            {
                throw new Win32Exception(extendResult, "DwmExtendFrameIntoClientArea 失败。");
            }

            IntPtr blurRegion = CreateRectRgn(-2, -2, -1, -1);
            if (blurRegion == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRectRgn 失败。");
            }

            try
            {
                var blurBehind = new DwmBlurBehind
                {
                    DwFlags = DwmBbEnable | DwmBbBlurRegion,
                    FEnable = true,
                    HRgnBlur = blurRegion,
                };

                int blurResult = DwmEnableBlurBehindWindow(hwnd, ref blurBehind);
                if (blurResult != 0)
                {
                    throw new Win32Exception(blurResult, "DwmEnableBlurBehindWindow 失败。");
                }
            }
            finally
            {
                _ = DeleteObject(blurRegion);
            }
        }

        private bool ClearBackground(IntPtr hwnd, IntPtr hdc)
        {
            if (hdc == IntPtr.Zero || !GetClientRect(hwnd, out Rect rect))
            {
                return false;
            }

            if (_backgroundBrush == IntPtr.Zero)
            {
                _backgroundBrush = CreateSolidBrush(0);
                if (_backgroundBrush == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateSolidBrush 失败。");
                }
            }

            return FillRect(hdc, ref rect, _backgroundBrush) != 0;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

        [DllImport("dwmapi.dll")]
        private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DwmBlurBehind blurBehind);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateSolidBrush(uint color);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int FillRect(IntPtr hdc, ref Rect rect, IntPtr brush);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DwmBlurBehind
        {
            public uint DwFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool FEnable;
            public IntPtr HRgnBlur;
            [MarshalAs(UnmanagedType.Bool)]
            public bool FTransitionOnMaximized;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
