using System;
using System.Runtime.InteropServices;

namespace WindBoard.Features.ScreenAnnotation.UI.Backdrop
{
    /// <summary>
    /// 为窗口挂接轻量级消息监听，用于透明背景场景下处理原生擦除消息。
    /// </summary>
    internal sealed class ScreenAnnotationWindowMessageMonitor : IDisposable
    {
        private readonly IntPtr _hwnd;
        private readonly nuint _subclassId;
        private readonly SubclassProc _subclassProc;
        private bool _isAttached;

        internal ScreenAnnotationWindowMessageMonitor(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                throw new ArgumentException("窗口句柄不能为空。", nameof(hwnd));
            }

            _hwnd = hwnd;
            _subclassId = unchecked((nuint)hwnd.GetHashCode());
            _subclassProc = OnWindowMessage;
        }

        internal event EventHandler<ScreenAnnotationWindowMessageEventArgs>? WindowMessageReceived;

        internal void EnsureAttached()
        {
            if (_isAttached)
            {
                return;
            }

            if (!SetWindowSubclass(_hwnd, _subclassProc, _subclassId, IntPtr.Zero))
            {
                throw new InvalidOperationException(
                    $"SetWindowSubclass 失败，lastError={Marshal.GetLastWin32Error()}");
            }

            _isAttached = true;
        }

        public void Dispose()
        {
            if (!_isAttached)
            {
                return;
            }

            _ = RemoveWindowSubclass(_hwnd, _subclassProc, _subclassId);
            _isAttached = false;
        }

        private IntPtr OnWindowMessage(
            IntPtr hwnd,
            uint message,
            nuint wParam,
            nint lParam,
            nuint subclassId,
            IntPtr referenceData)
        {
            var args = new ScreenAnnotationWindowMessageEventArgs(hwnd, message, wParam, lParam);
            WindowMessageReceived?.Invoke(this, args);
            if (args.Handled)
            {
                return args.Result;
            }

            return DefSubclassProc(hwnd, message, wParam, lParam);
        }

        private delegate IntPtr SubclassProc(
            IntPtr hWnd,
            uint uMsg,
            nuint wParam,
            nint lParam,
            nuint uIdSubclass,
            IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowSubclass(
            IntPtr hWnd,
            SubclassProc pfnSubclass,
            nuint uIdSubclass,
            IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveWindowSubclass(
            IntPtr hWnd,
            SubclassProc pfnSubclass,
            nuint uIdSubclass);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern IntPtr DefSubclassProc(
            IntPtr hWnd,
            uint uMsg,
            nuint wParam,
            nint lParam);
    }

    /// <summary>
    /// 承载透明背景所需的窗口消息参数。
    /// </summary>
    internal sealed class ScreenAnnotationWindowMessageEventArgs : EventArgs
    {
        internal ScreenAnnotationWindowMessageEventArgs(IntPtr hwnd, uint messageId, nuint wParam, nint lParam)
        {
            Hwnd = hwnd;
            MessageId = messageId;
            WParam = wParam;
            LParam = lParam;
        }

        internal IntPtr Hwnd { get; }

        internal uint MessageId { get; }

        internal nuint WParam { get; }

        internal nint LParam { get; }

        internal bool Handled { get; set; }

        internal IntPtr Result { get; set; }
    }
}
