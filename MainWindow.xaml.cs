using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace WindBoard
{
    public partial class MainWindow : Window
    {
        // 工具状态
        private InkCanvasEditingMode _lastEditingMode = InkCanvasEditingMode.Ink;
        private double _baseThickness = 3.0;

        // Zoom（视口缩放）
        private double _zoom = 1.0;
        private const double MinZoom = 0.5;
        private const double MaxZoom = 5.0;

        // 鼠标平移相关
        private bool _isPanning = false;
        private Point _lastMousePosition;

        // 触摸点集合：DeviceId -> Point (Viewport坐标)
        private readonly Dictionary<int, Point> _activeTouches = new Dictionary<int, Point>();

        private bool _gestureActive = false;
        private int _gestureId1 = -1;
        private int _gestureId2 = -1;
        private Point _lastGestureP1;
        private Point _lastGestureP2;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 启动后把视口移动到大画布中心
            Dispatcher.InvokeAsync(() =>
            {
                // 先确保布局完成，否则 Viewport.ViewportWidth/Height 可能为 0
                Viewport.UpdateLayout();

                var extentW = MyCanvas.Width * _zoom;
                var extentH = MyCanvas.Height * _zoom;

                Viewport.ScrollToHorizontalOffset((extentW - Viewport.ViewportWidth) / 2.0);
                Viewport.ScrollToVerticalOffset((extentH - Viewport.ViewportHeight) / 2.0);

                UpdatePenThickness(_zoom);
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        #region Core Helpers (Zoom & Pan)

        private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

        private void SetZoomAt(Point viewportPoint, double newZoom)
        {
            double oldZoom = _zoom;
            newZoom = Clamp(newZoom, MinZoom, MaxZoom);
            if (Math.Abs(newZoom - oldZoom) < 0.00001) return;

            // 将“鼠标/触点指向的屏幕点”映射到缩放前的内容坐标
            double contentX = (Viewport.HorizontalOffset + viewportPoint.X) / oldZoom;
            double contentY = (Viewport.VerticalOffset + viewportPoint.Y) / oldZoom;

            // 应用缩放
            _zoom = newZoom;
            ZoomTransform.ScaleX = _zoom;
            ZoomTransform.ScaleY = _zoom;

            // 让 ScrollViewer 更新 Extent，然后再设置 Offset（更稳）
            Viewport.UpdateLayout();

            // 调整 Offset：保证缩放后仍指向同一内容点
            Viewport.ScrollToHorizontalOffset(contentX * _zoom - viewportPoint.X);
            Viewport.ScrollToVerticalOffset(contentY * _zoom - viewportPoint.Y);

            UpdatePenThickness(_zoom);
        }

        private void PanBy(Vector deltaViewport)
        {
            // 手往右拖，内容跟着往右 => ScrollOffset 减小
            Viewport.ScrollToHorizontalOffset(Viewport.HorizontalOffset - deltaViewport.X);
            Viewport.ScrollToVerticalOffset(Viewport.VerticalOffset - deltaViewport.Y);
        }

        private void UpdatePenThickness(double currentZoom)
        {
            if (currentZoom <= 0) currentZoom = 1;

            // “屏幕看起来粗细不变”：世界坐标粗细随 zoom 反比变化
            double newThickness = _baseThickness / currentZoom;

            var da = MyCanvas.DefaultDrawingAttributes;
            da.Width = newThickness;
            da.Height = newThickness;
        }

        #endregion

#region Touch Handlers (Pinch Zoom & Pan - Snapshot Based)

private void MyCanvas_TouchDown(object sender, TouchEventArgs e)
{
    MyCanvas.CaptureTouch(e.TouchDevice);

    var p = e.GetTouchPoint(Viewport).Position;
    _activeTouches[e.TouchDevice.Id] = p;

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

        #region Mouse Interaction Handlers (Wheel Zoom & Space Pan)

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
        }

        #endregion

        #region Tool UI

        private void RadioPen_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (RadioPen.IsChecked == true)
            {
                e.Handled = true;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    PopupPenSettings.IsOpen = true;
                }));
            }
        }

        private void RadioPen_Checked(object sender, RoutedEventArgs e)
        {
            MyCanvas.EditingMode = InkCanvasEditingMode.Ink;
        }

        private void RadioEraser_Checked(object sender, RoutedEventArgs e)
        {
            MyCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
            MyCanvas.EraserShape = new EllipseStylusShape(16, 16);
        }

        private void RadioSelect_Checked(object sender, RoutedEventArgs e)
        {
            MyCanvas.EditingMode = InkCanvasEditingMode.Select;
        }

        private void Thickness_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && double.TryParse(rb.Tag?.ToString(), out double thickness))
            {
                _baseThickness = thickness;
                UpdatePenThickness(_zoom);
            }
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorCode)
            {
                try
                {
                    Color color = (Color)ColorConverter.ConvertFromString(colorCode);
                    MyCanvas.DefaultDrawingAttributes.Color = color;
                }
                catch (FormatException)
                {
                    MyCanvas.DefaultDrawingAttributes.Color = Colors.White;
                }

                PopupPenSettings.IsOpen = false;
            }
        }

        #endregion
    }
}