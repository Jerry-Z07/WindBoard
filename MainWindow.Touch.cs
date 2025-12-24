using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WindBoard
{
    public partial class MainWindow
    {
        #region Touch Handlers (Pinch Zoom & Pan - Snapshot Based)

        private void MyCanvas_TouchDown(object sender, TouchEventArgs e)
        {
            MyCanvas.CaptureTouch(e.TouchDevice);

            var p = e.GetTouchPoint(Viewport).Position;
            _activeTouches[e.TouchDevice.Id] = p;

            // 统一派发：触摸按下（仅单指进入统一事件流）
            if (_activeTouches.Count == 1 && !_gestureActive)
            {
                Point pCanvas = e.GetTouchPoint(MyCanvas).Position;
                Point pViewport = e.GetTouchPoint(Viewport).Position;

                long ticks = (long)e.Timestamp * System.TimeSpan.TicksPerMillisecond;
                var mods = Keyboard.Modifiers;

                var args = new DeviceInputEventArgs
                {
                    DeviceType = InputDeviceType.Touch,
                    CanvasPoint = pCanvas,
                    ViewportPoint = pViewport,
                    TouchId = e.TouchDevice.Id,
                    Pressure = null,
                    IsInAir = false,
                    LeftButton = false,
                    RightButton = false,
                    MiddleButton = false,
                    Ctrl = (mods & ModifierKeys.Control) != 0,
                    Shift = (mods & ModifierKeys.Shift) != 0,
                    Alt = (mods & ModifierKeys.Alt) != 0,
                    TimestampTicks = ticks
                };

                RaiseDeviceDown(args);
            }

            if (_activeTouches.Count >= 2)
            {
                // 进入多指手势：暂停书写
                _lastEditingMode = MyCanvas.EditingMode;
                MyCanvas.EditingMode = InkCanvasEditingMode.None;

                // 以所有触点的质心+平均半径作为手势快照
                double sumX = 0, sumY = 0; int n = _activeTouches.Count;
                foreach (var pt in _activeTouches.Values) { sumX += pt.X; sumY += pt.Y; }
                Point center = new Point(sumX / n, sumY / n);
                double spread = 0;
                foreach (var pt in _activeTouches.Values)
                {
                    double dx = pt.X - center.X, dy = pt.Y - center.Y;
                    spread += Math.Sqrt(dx * dx + dy * dy);
                }
                spread = n > 0 ? spread / n : 0;

                _lastGestureCenter = center;
                _lastGestureSpread = spread;
                _gestureActive = true;

                // 多指开始，隐藏橡皮擦游标
                _isEraserPressed = false;
                UpdateEraserVisual(null);

                // 关键：多指时必须 Handled，阻止触摸被“提升”为鼠标/滚动等副作用
                e.Handled = true;
            }
            // 单指不 Handled，让 InkCanvas 正常收集墨迹
        }

        private void MyCanvas_TouchMove(object sender, TouchEventArgs e)
        {
            if (!_activeTouches.ContainsKey(e.TouchDevice.Id)) return;

            // 更新当前触点（Viewport 坐标）
            var p = e.GetTouchPoint(Viewport).Position;
            _activeTouches[e.TouchDevice.Id] = p;

            // 统一派发：仅在单指非手势时执行（AutoExpand 由统一订阅处理）
            var pCanvasMove = e.GetTouchPoint(MyCanvas).Position;
            if (!_gestureActive && _activeTouches.Count == 1)
            {
                long ticks = (long)e.Timestamp * System.TimeSpan.TicksPerMillisecond;
                var mods = Keyboard.Modifiers;

                var args = new DeviceInputEventArgs
                {
                    DeviceType = InputDeviceType.Touch,
                    CanvasPoint = pCanvasMove,
                    ViewportPoint = p,
                    TouchId = e.TouchDevice.Id,
                    Pressure = null,
                    IsInAir = false,
                    LeftButton = false,
                    RightButton = false,
                    MiddleButton = false,
                    Ctrl = (mods & ModifierKeys.Control) != 0,
                    Shift = (mods & ModifierKeys.Shift) != 0,
                    Alt = (mods & ModifierKeys.Alt) != 0,
                    TimestampTicks = ticks
                };

                RaiseDeviceMove(args);
            }

            if (!(_gestureActive && _activeTouches.Count >= 2))
            {
                return;
            }

            // 当前多指质心与平均半径（Viewport 坐标）
            double sumX = 0, sumY = 0; int n = _activeTouches.Count;
            foreach (var pt in _activeTouches.Values) { sumX += pt.X; sumY += pt.Y; }
            Point newCenter = new Point(sumX / n, sumY / n);
            double newSpread = 0;
            foreach (var pt in _activeTouches.Values)
            {
                double dx = pt.X - newCenter.X, dy = pt.Y - newCenter.Y;
                newSpread += Math.Sqrt(dx * dx + dy * dy);
            }
            newSpread = n > 0 ? newSpread / n : 0;

            // 计算新 zoom（允许纯平移：spread 太小时 scale = 1）
            double oldZoom = _zoom;
            double scale = 1.0;
            if (_lastGestureSpread > 10 && newSpread > 0)
                scale = newSpread / _lastGestureSpread;
            double newZoom = Clamp(oldZoom * scale, MinZoom, MaxZoom);

            // 把上帧质心对应的内容点锁定到新质心（一步到位消漂移）
            double contentX = (Viewport.HorizontalOffset + _lastGestureCenter.X) / oldZoom;
            double contentY = (Viewport.VerticalOffset + _lastGestureCenter.Y) / oldZoom;

            // 应用缩放
            _zoom = newZoom;
            ZoomTransform.ScaleX = _zoom;
            ZoomTransform.ScaleY = _zoom;

            // 更新布局，保证 Extent/Viewport 尺寸已刷新
            Viewport.UpdateLayout();

            // 设置新的 offset：让内容点落在新质心
            Viewport.ScrollToHorizontalOffset(contentX * _zoom - newCenter.X);
            Viewport.ScrollToVerticalOffset(contentY * _zoom - newCenter.Y);

            UpdatePenThickness(_zoom);
            UpdateEraserVisual(null);

            // 更新快照
            _lastGestureCenter = newCenter;
            _lastGestureSpread = newSpread;

            // 多指手势必须吃掉事件，避免产生乱线/提升为鼠标
            e.Handled = true;
        }

        private void MyCanvas_TouchUp(object sender, TouchEventArgs e)
        {
            MyCanvas.ReleaseTouchCapture(e.TouchDevice);

            // 统一派发：触摸抬起
            Point pCanvas = e.GetTouchPoint(MyCanvas).Position;
            Point pViewport = e.GetTouchPoint(Viewport).Position;

            long ticks = (long)e.Timestamp * System.TimeSpan.TicksPerMillisecond;
            var mods = Keyboard.Modifiers;

            var args = new DeviceInputEventArgs
            {
                DeviceType = InputDeviceType.Touch,
                CanvasPoint = pCanvas,
                ViewportPoint = pViewport,
                TouchId = e.TouchDevice.Id,
                Pressure = null,
                IsInAir = false,
                LeftButton = false,
                RightButton = false,
                MiddleButton = false,
                Ctrl = (mods & ModifierKeys.Control) != 0,
                Shift = (mods & ModifierKeys.Shift) != 0,
                Alt = (mods & ModifierKeys.Alt) != 0,
                TimestampTicks = ticks
            };

            RaiseDeviceUp(args);

            _activeTouches.Remove(e.TouchDevice.Id);

            if (_activeTouches.Count < 2)
            {
                // 退出手势
                _gestureActive = false;

                // 恢复之前的编辑模式
                if (MyCanvas.EditingMode == InkCanvasEditingMode.None)
                    MyCanvas.EditingMode = _lastEditingMode;

                // 多指结束也 Handled 一下，减少“提升为鼠标事件”引发的杂音
                e.Handled = true;
            }
            else if (_activeTouches.Count >= 2)
            {
                // 仍有多指（比如第三指抬起/换指），重建手势快照，避免跳变
                double sumX = 0, sumY = 0; int n = _activeTouches.Count;
                foreach (var pt in _activeTouches.Values) { sumX += pt.X; sumY += pt.Y; }
                Point center = new Point(sumX / n, sumY / n);
                double spread = 0;
                foreach (var pt in _activeTouches.Values)
                {
                    double dx = pt.X - center.X, dy = pt.Y - center.Y;
                    spread += Math.Sqrt(dx * dx + dy * dy);
                }
                spread = n > 0 ? spread / n : 0;

                _lastGestureCenter = center;
                _lastGestureSpread = spread;
                _gestureActive = true;

                e.Handled = true;
            }
        }

        #endregion
    }
}
