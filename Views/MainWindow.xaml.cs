using System;
using WindBoard.Services;

namespace WindBoard
{
    public partial class MainWindow
    {
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                var windowHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                _touchGestureService?.RestoreSystemGestures(windowHandle);
                _touchGestureService?.Dispose();
                _realTimeStylusManager?.Dispose();
            }
            catch
            {
                // ignore disposal errors on shutdown
            }

            base.OnClosed(e);
        }
    }
}
