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
            SetViewportBitmapCache(true);
            _zoomPanService.ZoomByWheel(e.GetPosition(Viewport), e.Delta);
            ScheduleViewportCacheDisable();
            ScheduleSelectionDockUpdate();
            e.Handled = true;
        }

        private static bool IsRealMouse(MouseEventArgs e) => e.StylusDevice == null;
        private static bool IsRealMouse(MouseButtonEventArgs e) => e.StylusDevice == null;

        private void MyCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsRealMouse(e)) return;

            if (IsSelectModeActive()
                && e.ChangedButton == MouseButton.Left
                && !Keyboard.IsKeyDown(Key.Space))
            {
                var canvasPoint = e.GetPosition(MyCanvas);
                var hit = HitTestAttachment(canvasPoint);
                if (hit != null)
                {
                    SelectAttachment(hit);
                    if (e.ClickCount >= 2)
                    {
                        if (hit.Type == BoardAttachmentType.Video && !string.IsNullOrWhiteSpace(hit.FilePath))
                        {
                            OpenExternal(hit.FilePath);
                        }
                        else if (hit.Type == BoardAttachmentType.Link && !string.IsNullOrWhiteSpace(hit.Url))
                        {
                            OpenExternal(hit.Url);
                        }
                    }

                    e.Handled = true;
                    return;
                }

                // 未命中附件：交给 InkCanvas 做笔迹选择，同时清除当前附件选择框
                SelectAttachment(null);
            }

            if (Keyboard.IsKeyDown(Key.Space) && e.ChangedButton == MouseButton.Left)
            {
                _modeBeforePan = _modeController.ActiveMode ?? _modeController.CurrentMode;
                _zoomPanService.BeginMousePan(e.GetPosition(Viewport));
                SetViewportBitmapCache(true);
                MyCanvas.EditingMode = InkCanvasEditingMode.None;
                MyCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            var args = BuildMouseArgs(e, isInAir: false);
            BeginUndoTransactionForCurrentMode();
            _inputManager.Dispatch(InputStage.Down, args);
        }

        private void MyCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_zoomPanService.UpdateMousePan(e.GetPosition(Viewport)))
            {
                ScheduleSelectionDockUpdate();
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
                ScheduleViewportCacheDisable();
                ScheduleSelectionDockUpdate();
                e.Handled = true;
            }

            if (!IsRealMouse(e)) return;

            var args = BuildMouseArgs(e, isInAir: false);
            _inputManager.Dispatch(InputStage.Up, args);
            EndUndoTransactionForCurrentMode();
        }

        private void MyCanvas_TouchDown(object sender, TouchEventArgs e)
        {
            MyCanvas.CaptureTouch(e.TouchDevice);
            var viewportPoint = e.GetTouchPoint(Viewport).Position;
            bool gesture = _zoomPanService.TouchDown(e.TouchDevice.Id, viewportPoint);
            if (_zoomPanService.IsGestureActive || gesture)
            {
                BeginGestureSuppression();
                HideTouchInkCursor();
                e.Handled = true;
                return;
            }

            var args = BuildTouchArgs(e, isInAir: false);
            BeginUndoTransactionForCurrentMode();
            _inputManager.Dispatch(InputStage.Down, args);
            UpdateTouchInkCursor(args.CanvasPoint);
            e.Handled = true;
        }

        private void MyCanvas_TouchMove(object sender, TouchEventArgs e)
        {
            var viewportPoint = e.GetTouchPoint(Viewport).Position;
            if (_zoomPanService.TouchMove(e.TouchDevice.Id, viewportPoint))
            {
                BeginGestureSuppression();
                HideTouchInkCursor();
                e.Handled = true;
                return;
            }

            if (_zoomPanService.IsGestureActive)
            {
                BeginGestureSuppression();
                HideTouchInkCursor();
                e.Handled = true;
                return;
            }

            var args = BuildTouchArgs(e, isInAir: false);
            _inputManager.Dispatch(InputStage.Move, args);
            UpdateTouchInkCursor(args.CanvasPoint);
            e.Handled = true;
        }

        private void MyCanvas_TouchUp(object sender, TouchEventArgs e)
        {
            _ = _zoomPanService.TouchUp(e.TouchDevice.Id);
            HideTouchInkCursor();
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
                e.Handled = true;
                MyCanvas.ReleaseTouchCapture(e.TouchDevice);
                return;
            }

            var args = BuildTouchArgs(e, isInAir: false);
            _inputManager.Dispatch(InputStage.Up, args);
            EndUndoTransactionForCurrentMode();
            e.Handled = true;
            MyCanvas.ReleaseTouchCapture(e.TouchDevice);
        }

        private void MyCanvas_StylusDown(object sender, StylusDownEventArgs e)
        {
            if (_inputSourceSelector?.ShouldHandleWpfStylus == false) return;
            if (!IsStylusPen(e)) return;

            var args = BuildStylusArgs(e, isInAir: false);
            BeginUndoTransactionForCurrentMode();
            _inputManager.Dispatch(InputStage.Down, args);
        }

        private void MyCanvas_StylusMove(object sender, StylusEventArgs e)
        {
            if (_inputSourceSelector?.ShouldHandleWpfStylus == false) return;
            if (!IsStylusPen(e)) return;

            var args = BuildStylusArgs(e, isInAir: false);
            _inputManager.Dispatch(InputStage.Move, args);
        }

        private void MyCanvas_StylusUp(object sender, StylusEventArgs e)
        {
            if (_inputSourceSelector?.ShouldHandleWpfStylus == false) return;
            if (!IsStylusPen(e)) return;

            var args = BuildStylusArgs(e, isInAir: false);
            _inputManager.Dispatch(InputStage.Up, args);
            EndUndoTransactionForCurrentMode();
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
            // 性能：StylusMove 频率很高；此项目未使用压力（InkMode/DefaultDrawingAttributes 已 IgnorePressure=true），
            // 避免在每次 Move 调用 GetStylusPoints 造成额外分配/跨层开销。
            double? pressure = null;

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

        private void DispatchStylusFromRealTimeStylus(InputStage stage, InputEventArgs args)
        {
            if (_inputManager == null) return;

            if (stage == InputStage.Down)
            {
                BeginUndoTransactionForCurrentMode();
            }

            _inputManager.Dispatch(stage, args);

            if (stage == InputStage.Up)
            {
                EndUndoTransactionForCurrentMode();
            }
        }
    }
}
