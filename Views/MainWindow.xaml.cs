using System;

namespace WindBoard
{
    public partial class MainWindow
    {
        protected override void OnClosed(EventArgs e)
        {
            try
            {
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
