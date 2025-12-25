using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WindBoard
{
    public partial class MainWindow
    {
        // 鼠标交互（滚轮缩放 & 空格平移），从 MainWindow.xaml.cs 拆分
        private void MyCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_gestureActive) return;
            double factor = e.Delta > 0 ? 1.1 : 0.9;
            double newZoom = _zoom * factor;
            newZoom = Clamp(newZoom, MinZoom, MaxZoom);

            // 以鼠标在 Viewport 内的位置为缩放中心
            Point p = e.GetPosition(Viewport);
            SetZoomAt(p, newZoom);

            e.Handled = true;
        }

        // 集中化“真实鼠标”判定，避免分散使用 e.StylusDevice == null
        private static bool IsRealMouse(MouseEventArgs e)
        {
            return e.StylusDevice == null;
        }

        private static bool IsRealMouse(MouseButtonEventArgs e)
        {
            return e.StylusDevice == null;
        }

        private void MyCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 按住空格 + 左键拖拽平移
            if (Keyboard.IsKeyDown(Key.Space) && e.ChangedButton == MouseButton.Left)
            {
                _isPanning = true;
                _lastMousePosition = e.GetPosition(Viewport);

                _lastEditingMode = MyCanvas.EditingMode;
                MyCanvas.EditingMode = InkCanvasEditingMode.None;

                MyCanvas.CaptureMouse();
                e.Handled = true;
            }

            // 统一事件分发：仅当未由触摸/触笔提升时（即 StylusDevice == null）才按“鼠标”派发，避免重复
            if (IsRealMouse(e))
            {
                Point pCanvas = e.GetPosition(MyCanvas);
                Point pViewport = e.GetPosition(Viewport);

                long ticks = (long)e.Timestamp * TimeSpan.TicksPerMillisecond;
                var mods = Keyboard.Modifiers;

                var args = new DeviceInputEventArgs
                {
                    DeviceType = InputDeviceType.Mouse,
                    CanvasPoint = pCanvas,
                    ViewportPoint = pViewport,
                    TouchId = null,
                    Pressure = null,
                    IsInAir = false,
                    LeftButton = e.LeftButton == MouseButtonState.Pressed || e.ChangedButton == MouseButton.Left,
                    RightButton = e.RightButton == MouseButtonState.Pressed || e.ChangedButton == MouseButton.Right,
                    MiddleButton = e.MiddleButton == MouseButtonState.Pressed || e.ChangedButton == MouseButton.Middle,
                    Ctrl = (mods & ModifierKeys.Control) != 0,
                    Shift = (mods & ModifierKeys.Shift) != 0,
                    Alt = (mods & ModifierKeys.Alt) != 0,
                    TimestampTicks = ticks
                };

                RaiseDeviceDown(args);
            }
        }

        private void MyCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                Point currentPosition = e.GetPosition(Viewport);
                Vector delta = currentPosition - _lastMousePosition;

                PanBy(delta);

                _lastMousePosition = currentPosition;
                e.Handled = true;
            }

            // 统一事件分发：MouseMove 仅对真实鼠标派发
            if (IsRealMouse(e))
            {
                Point pCanvas = e.GetPosition(MyCanvas);
                Point pViewport = e.GetPosition(Viewport);

                long ticks = (long)e.Timestamp * TimeSpan.TicksPerMillisecond;
                var mods = Keyboard.Modifiers;

                bool anyPressed = e.LeftButton == MouseButtonState.Pressed
                    || e.RightButton == MouseButtonState.Pressed
                    || e.MiddleButton == MouseButtonState.Pressed;

                var args = new DeviceInputEventArgs
                {
                    DeviceType = InputDeviceType.Mouse,
                    CanvasPoint = pCanvas,
                    ViewportPoint = pViewport,
                    TouchId = null,
                    Pressure = null,
                    IsInAir = !anyPressed,
                    LeftButton = e.LeftButton == MouseButtonState.Pressed,
                    RightButton = e.RightButton == MouseButtonState.Pressed,
                    MiddleButton = e.MiddleButton == MouseButtonState.Pressed,
                    Ctrl = (mods & ModifierKeys.Control) != 0,
                    Shift = (mods & ModifierKeys.Shift) != 0,
                    Alt = (mods & ModifierKeys.Alt) != 0,
                    TimestampTicks = ticks
                };

                if (!anyPressed)
                {
                    RaiseDeviceHover(args);
                }
                else
                {
                    RaiseDeviceMove(args);
                }
            }
        }

        private void MyCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning && e.ChangedButton == MouseButton.Left)
            {
                _isPanning = false;
                MyCanvas.ReleaseMouseCapture();

                MyCanvas.EditingMode = _lastEditingMode;
                e.Handled = true;
            }

            // 统一事件分发：MouseUp 仅对真实鼠标派发
            if (IsRealMouse(e))
            {
                Point pCanvas = e.GetPosition(MyCanvas);
                Point pViewport = e.GetPosition(Viewport);

                long ticks = (long)e.Timestamp * TimeSpan.TicksPerMillisecond;
                var mods = Keyboard.Modifiers;

                var args = new DeviceInputEventArgs
                {
                    DeviceType = InputDeviceType.Mouse,
                    CanvasPoint = pCanvas,
                    ViewportPoint = pViewport,
                    TouchId = null,
                    Pressure = null,
                    IsInAir = false,
                    LeftButton = e.LeftButton == MouseButtonState.Pressed,
                    RightButton = e.RightButton == MouseButtonState.Pressed,
                    MiddleButton = e.MiddleButton == MouseButtonState.Pressed,
                    Ctrl = (mods & ModifierKeys.Control) != 0,
                    Shift = (mods & ModifierKeys.Shift) != 0,
                    Alt = (mods & ModifierKeys.Alt) != 0,
                    TimestampTicks = ticks
                };

                RaiseDeviceUp(args);
            }
        }
    }
}