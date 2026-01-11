using System;
using System.Windows.Input;

namespace WindBoard
{
    public partial class MainWindow
    {
        private void PrepareForPageTransition()
        {
            try
            {
                _zoomPanService?.EndMousePan();
                _zoomPanService?.CancelGesture();
            }
            catch
            {
            }

            try
            {
                if (MyCanvas != null)
                {
                    if (MyCanvas.IsMouseCaptured)
                    {
                        MyCanvas.ReleaseMouseCapture();
                    }
                    MyCanvas.ReleaseAllTouchCaptures();
                }
            }
            catch
            {
            }

            try
            {
                Mouse.Capture(null);
            }
            catch
            {
            }

            try
            {
                if (_gestureInputSuppressed)
                {
                    EndGestureSuppression();
                }
            }
            catch
            {
                try { _gestureInputSuppressed = false; } catch { }
                try { _inputManager.InputSuppressed = false; } catch { }
            }

            try
            {
                _inkMode?.CancelAllStrokes();
            }
            catch
            {
            }

            try
            {
                InkSurface?.ResetSurface();
            }
            catch
            {
            }

            try
            {
                _autoExpandService?.DiscardPendingShift();
            }
            catch
            {
            }

            try
            {
                _modeBeforePan = null;
                _modeBeforeGesture = null;
            }
            catch
            {
            }
        }

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            PrepareForPageTransition();
            UpdateInkSurfaceViewportTransform();
            InvalidateInkSurface();
        }

        private void MyCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_zoomPanService?.IsMousePanning != true)
            {
                return;
            }

            _zoomPanService.EndMousePan();
            _modeBeforePan?.SwitchOn();
            _modeBeforePan = null;
            UpdateInkSurfaceViewportTransform();
            InvalidateInkSurface();
            ScheduleViewportCacheDisable();
            ScheduleSelectionDockUpdate();
        }

        private void MyCanvas_LostTouchCapture(object sender, TouchEventArgs e)
        {
            _zoomPanService?.CancelGesture();

            if (!_gestureInputSuppressed)
            {
                return;
            }

            EndGestureSuppression();
            UpdateInkSurfaceViewportTransform();
            InvalidateInkSurface();
        }
    }
}
