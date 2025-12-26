using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Ink;
using WindBoard.Core.Input;
using InputEventArgs = WindBoard.Core.Input.InputEventArgs;

namespace WindBoard
{
    public partial class MainWindow
    {
        private void MyCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _zoomPanService.ZoomByWheel(e.GetPosition(Viewport), e.Delta);
            e.Handled = true;
        }

        private static bool IsRealMouse(MouseEventArgs e) => e.StylusDevice == null;
        private static bool IsRealMouse(MouseButtonEventArgs e) => e.StylusDevice == null;

        private void MyCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsRealMouse(e)) return;

            if (Keyboard.IsKeyDown(Key.Space) && e.ChangedButton == MouseButton.Left)
            {
                _modeBeforePan = _modeController.ActiveMode ?? _modeController.CurrentMode;
                _zoomPanService.BeginMousePan(e.GetPosition(Viewport));
                MyCanvas.EditingMode = InkCanvasEditingMode.None;
                MyCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            var args = BuildMouseArgs(e, isInAir: false);
            _inputManager.Dispatch(InputStage.Down, args);
        }

        private void MyCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_zoomPanService.UpdateMousePan(e.GetPosition(Viewport)))
            {
                e.Handled = true;
                return;
            }

            if (!IsRealMouse(e)) return;

            bool anyPressed = e.LeftButton == MouseButtonState.Pressed
                              || e.RightButton == MouseButtonState.Pressed
                              || e.MiddleButton == MouseButtonState.Pressed;

            var args = BuildMouseArgs(e, isInAir: !anyPressed);
            _inputManager.Dispatch(anyPressed ? InputStage.Move : InputStage.Hover, args);
        }

        private void MyCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_zoomPanService.IsMousePanning && e.ChangedButton == MouseButton.Left)
            {
                _zoomPanService.EndMousePan();
                MyCanvas.ReleaseMouseCapture();
                _modeBeforePan?.SwitchOn();
                _modeBeforePan = null;
                e.Handled = true;
            }

            if (!IsRealMouse(e)) return;

            var args = BuildMouseArgs(e, isInAir: false);
            _inputManager.Dispatch(InputStage.Up, args);
        }

        private void MyCanvas_TouchDown(object sender, TouchEventArgs e)
        {
            MyCanvas.CaptureTouch(e.TouchDevice);
            var viewportPoint = e.GetTouchPoint(Viewport).Position;
            bool gesture = _zoomPanService.TouchDown(e.TouchDevice.Id, viewportPoint);
            if (_zoomPanService.IsGestureActive || gesture)
            {
                BeginGestureSuppression();
                e.Handled = true;
                return;
            }

            var args = BuildTouchArgs(e, isInAir: false);
            _inputManager.Dispatch(InputStage.Down, args);
        }

        private void MyCanvas_TouchMove(object sender, TouchEventArgs e)
        {
            var viewportPoint = e.GetTouchPoint(Viewport).Position;
            if (_zoomPanService.TouchMove(e.TouchDevice.Id, viewportPoint))
            {
                BeginGestureSuppression();
                e.Handled = true;
                return;
            }

            if (_zoomPanService.IsGestureActive)
            {
                BeginGestureSuppression();
                e.Handled = true;
                return;
            }

            var args = BuildTouchArgs(e, isInAir: false);
            _inputManager.Dispatch(InputStage.Move, args);
        }

        private void MyCanvas_TouchUp(object sender, TouchEventArgs e)
        {
            bool gestureHandled = _zoomPanService.TouchUp(e.TouchDevice.Id);
            if (_zoomPanService.IsGestureActive)
            {
                BeginGestureSuppression();
                e.Handled = true;
                MyCanvas.ReleaseTouchCapture(e.TouchDevice);
                return;
            }

            if (_gestureInputSuppressed)
            {
                EndGestureSuppression();
                e.Handled = gestureHandled;
                MyCanvas.ReleaseTouchCapture(e.TouchDevice);
                return;
            }

            var args = BuildTouchArgs(e, isInAir: false);
            _inputManager.Dispatch(InputStage.Up, args);
            e.Handled = gestureHandled;
            MyCanvas.ReleaseTouchCapture(e.TouchDevice);
        }

        private void MyCanvas_StylusDown(object sender, StylusDownEventArgs e)
        {
            if (!IsStylusPen(e)) return;

            var args = BuildStylusArgs(e, isInAir: false);
            _inputManager.Dispatch(InputStage.Down, args);
        }

        private void MyCanvas_StylusMove(object sender, StylusEventArgs e)
        {
            if (!IsStylusPen(e)) return;

            var args = BuildStylusArgs(e, isInAir: false);
            _inputManager.Dispatch(InputStage.Move, args);
        }

        private void MyCanvas_StylusUp(object sender, StylusEventArgs e)
        {
            if (!IsStylusPen(e)) return;

            var args = BuildStylusArgs(e, isInAir: false);
            _inputManager.Dispatch(InputStage.Up, args);
        }

        private void MyCanvas_StylusInAirMove(object sender, StylusEventArgs e)
        {
            if (!IsStylusPen(e)) return;

            var args = BuildStylusArgs(e, isInAir: true);
            _inputManager.Dispatch(InputStage.Hover, args);
        }

        private static bool IsStylusPen(StylusEventArgs e)
        {
            var tablet = e.StylusDevice?.TabletDevice;
            return tablet != null && tablet.Type == TabletDeviceType.Stylus;
        }

        private InputEventArgs BuildMouseArgs(MouseEventArgs e, bool isInAir)
        {
            var mods = Keyboard.Modifiers;
            long ticks = DateTime.UtcNow.Ticks;

            return new InputEventArgs
            {
                DeviceType = InputDeviceType.Mouse,
                CanvasPoint = e.GetPosition(MyCanvas),
                ViewportPoint = e.GetPosition(Viewport),
                PointerId = null,
                Pressure = null,
                IsInAir = isInAir,
                LeftButton = e.LeftButton == MouseButtonState.Pressed,
                RightButton = e.RightButton == MouseButtonState.Pressed,
                MiddleButton = e.MiddleButton == MouseButtonState.Pressed,
                Ctrl = (mods & ModifierKeys.Control) != 0,
                Shift = (mods & ModifierKeys.Shift) != 0,
                Alt = (mods & ModifierKeys.Alt) != 0,
                TimestampTicks = ticks
            };
        }

        private InputEventArgs BuildStylusArgs(StylusEventArgs e, bool isInAir)
        {
            var mods = Keyboard.Modifiers;
            double? pressure = null;
            try
            {
                var pts = e.GetStylusPoints(MyCanvas);
                if (pts != null && pts.Count > 0)
                {
                    pressure = pts[^1].PressureFactor;
                }
            }
            catch
            {
            }

            long ticks = (long)e.Timestamp * TimeSpan.TicksPerMillisecond;

            return new InputEventArgs
            {
                DeviceType = InputDeviceType.Stylus,
                CanvasPoint = e.GetPosition(MyCanvas),
                ViewportPoint = e.GetPosition(Viewport),
                PointerId = e.StylusDevice?.Id,
                Pressure = pressure,
                IsInAir = isInAir,
                LeftButton = false,
                RightButton = false,
                MiddleButton = false,
                Ctrl = (mods & ModifierKeys.Control) != 0,
                Shift = (mods & ModifierKeys.Shift) != 0,
                Alt = (mods & ModifierKeys.Alt) != 0,
                TimestampTicks = ticks
            };
        }

        private InputEventArgs BuildTouchArgs(TouchEventArgs e, bool isInAir)
        {
            var mods = Keyboard.Modifiers;
            var tpCanvas = e.GetTouchPoint(MyCanvas);
            var tpViewport = e.GetTouchPoint(Viewport);
            long ticks = (long)e.Timestamp * TimeSpan.TicksPerMillisecond;
            var size = tpCanvas.Bounds.Size;

            return new InputEventArgs
            {
                DeviceType = InputDeviceType.Touch,
                CanvasPoint = tpCanvas.Position,
                ViewportPoint = tpViewport.Position,
                PointerId = e.TouchDevice.Id,
                Pressure = null,
                IsInAir = isInAir,
                LeftButton = false,
                RightButton = false,
                MiddleButton = false,
                Ctrl = (mods & ModifierKeys.Control) != 0,
                Shift = (mods & ModifierKeys.Shift) != 0,
                Alt = (mods & ModifierKeys.Alt) != 0,
                TimestampTicks = ticks,
                ContactSize = size
            };
        }
    }
}
