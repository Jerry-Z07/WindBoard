using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Ink;
using System.Windows.Media;

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

            // 单指擦除：按下时显示游标
            if (RadioEraser.IsChecked == true && _activeTouches.Count == 1)
            {
                HandleEraserTouchDown(e);
            }

            if (_activeTouches.Count == 2)
            {
                // 进入双指手势：暂停书写
                _lastEditingMode = MyCanvas.EditingMode;
                MyCanvas.EditingMode = InkCanvasEditingMode.None;

                // 固定手势的两根手指 ID（排序保证稳定）
                var ids = _activeTouches.Keys.OrderBy(id => id).ToArray();
                _gestureId1 = ids[0];
                _gestureId2 = ids[1];

                _lastGestureP1 = _activeTouches[_gestureId1];
                _lastGestureP2 = _activeTouches[_gestureId2];
                _gestureActive = true;

                // 双指开始，隐藏橡皮擦游标
                _isEraserPressed = false;
                UpdateEraserVisual(null);

                // 关键：双指时必须 Handled，阻止触摸被“提升”为鼠标/滚动等副作用
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

            // 自动扩容检测
            AutoExpandCanvas(e.GetTouchPoint(MyCanvas).Position);

            // 橡皮擦模式下的单指移动：仅在按下时显示并跟随（不拦截事件）
            if (!_gestureActive && RadioEraser.IsChecked == true && _activeTouches.Count == 1 && _isEraserPressed)
            {
                HandleEraserTouchMove(e);
            }

            if (!(_gestureActive && _activeTouches.Count == 2
                  && _activeTouches.ContainsKey(_gestureId1)
                  && _activeTouches.ContainsKey(_gestureId2)))
            {
                return;
            }

            // 当前两指位置
            Point p1New = _activeTouches[_gestureId1];
            Point p2New = _activeTouches[_gestureId2];

            // 上一帧两指位置（快照）
            Point p1Old = _lastGestureP1;
            Point p2Old = _lastGestureP2;

            // 中心点（Viewport 坐标）
            Point oldCenter = new Point((p1Old.X + p2Old.X) / 2.0, (p1Old.Y + p2Old.Y) / 2.0);
            Point newCenter = new Point((p1New.X + p2New.X) / 2.0, (p1New.Y + p2New.Y) / 2.0);

            // 距离（Viewport 坐标）
            double oldDist = (p1Old - p2Old).Length;
            double newDist = (p1New - p2New).Length;

            // 计算新 zoom（允许纯平移：dist 太小时 scale = 1）
            double oldZoom = _zoom;
            double scale = 1.0;

            if (oldDist > 10 && newDist > 0)
                scale = newDist / oldDist;

            double newZoom = Clamp(oldZoom * scale, MinZoom, MaxZoom);

            // 关键：把 oldCenter 对应的内容点锁定到 newCenter（一步到位消漂移）
            // oldCenter 指向的内容坐标（content space）
            double contentX = (Viewport.HorizontalOffset + oldCenter.X) / oldZoom;
            double contentY = (Viewport.VerticalOffset + oldCenter.Y) / oldZoom;

            // 应用缩放
            _zoom = newZoom;
            ZoomTransform.ScaleX = _zoom;
            ZoomTransform.ScaleY = _zoom;

            // 更新布局，保证 Extent/Viewport 尺寸已刷新
            Viewport.UpdateLayout();

            // 设置新的 offset：让 content 点落在 newCenter
            Viewport.ScrollToHorizontalOffset(contentX * _zoom - newCenter.X);
            Viewport.ScrollToVerticalOffset(contentY * _zoom - newCenter.Y);

            UpdatePenThickness(_zoom);
            UpdateEraserVisual(null);

            // 更新快照
            _lastGestureP1 = p1New;
            _lastGestureP2 = p2New;

            // 双指手势必须吃掉事件，避免产生乱线/提升为鼠标
            e.Handled = true;
        }

        private void MyCanvas_TouchUp(object sender, TouchEventArgs e)
        {
            MyCanvas.ReleaseTouchCapture(e.TouchDevice);

            _activeTouches.Remove(e.TouchDevice.Id);

            if (_activeTouches.Count < 2)
            {
                // 退出手势
                _gestureActive = false;
                _gestureId1 = _gestureId2 = -1;

                // 恢复之前的编辑模式
                if (MyCanvas.EditingMode == InkCanvasEditingMode.None)
                    MyCanvas.EditingMode = _lastEditingMode;

                // 松开触点：隐藏橡皮擦游标
                HandleEraserTouchUp(e);

                // 双指结束也 Handled 一下，减少“提升为鼠标事件”引发的杂音
                e.Handled = true;
            }
            else if (_activeTouches.Count == 2)
            {
                // 仍有两指（比如第三指抬起/换指），重建手势快照，避免跳变
                var ids = _activeTouches.Keys.OrderBy(id => id).ToArray();
                _gestureId1 = ids[0];
                _gestureId2 = ids[1];
                _lastGestureP1 = _activeTouches[_gestureId1];
                _lastGestureP2 = _activeTouches[_gestureId2];
                _gestureActive = true;

                e.Handled = true;
            }
        }

        #endregion
    }
}
