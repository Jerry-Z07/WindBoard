using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Input;
using System;
using System.Linq;
using System.Collections.Generic;

namespace WindBoard
{
    public partial class MainWindow : Window
    {
        // 记录操作前的编辑模式，用于手势结束后恢复
        private InkCanvasEditingMode _lastEditingMode = InkCanvasEditingMode.Ink;
        // 记录当前选中的笔触粗细（基准值）
        private double _baseThickness = 3.0;

        // 鼠标平移相关变量
        private bool _isPanning = false;
        private Point _lastMousePosition;

        public MainWindow()
        {
            InitializeComponent();
        }

        #region Touch Handlers (Zoom & Pan)

        // 触摸点集合：DeviceId -> Point (Window坐标)
        private Dictionary<int, Point> _activeTouches = new Dictionary<int, Point>();

        private void MyCanvas_TouchDown(object sender, TouchEventArgs e)
        {
            // 捕获触摸以确保我们在移动到元素外部时也能收到事件
            MyCanvas.CaptureTouch(e.TouchDevice);

            var point = e.GetTouchPoint(this).Position;
            if (!_activeTouches.ContainsKey(e.TouchDevice.Id))
            {
                _activeTouches.Add(e.TouchDevice.Id, point);
            }

            if (_activeTouches.Count == 2)
            {
                // 进入手势模式
                _lastEditingMode = MyCanvas.EditingMode;
                MyCanvas.EditingMode = InkCanvasEditingMode.None;
            }
        }

        private void MyCanvas_TouchMove(object sender, TouchEventArgs e)
        {
            if (!_activeTouches.ContainsKey(e.TouchDevice.Id)) return;

            var newPoint = e.GetTouchPoint(this).Position;

            // 如果是双指操作
            if (_activeTouches.Count == 2)
            {
                // 获取另一个触摸点
                var otherId = _activeTouches.Keys.First(id => id != e.TouchDevice.Id);
                var otherPoint = _activeTouches[otherId];
                var oldPoint = _activeTouches[e.TouchDevice.Id];

                // 计算变换
                var transform = MyCanvas.RenderTransform as MatrixTransform;
                if (transform != null)
                {
                    var matrix = transform.Matrix;

                    // 1. 计算缩放
                    double oldDist = (oldPoint - otherPoint).Length;
                    double newDist = (newPoint - otherPoint).Length;

                    if (oldDist > 10) // 避免距离过近导致计算不稳定
                    {
                        double scale = newDist / oldDist;

                        // 限制缩放
                        double currentScale = matrix.M11;
                        double targetScale = currentScale * scale;

                        if (targetScale < 0.5) scale = 0.5 / currentScale;
                        if (targetScale > 5.0) scale = 5.0 / currentScale;

                        // 缩放中心为两点中点（旧位置）
                        Point center = new Point((oldPoint.X + otherPoint.X) / 2, (oldPoint.Y + otherPoint.Y) / 2);
                        matrix.ScaleAt(scale, scale, center.X, center.Y);
                    }

                    // 2. 计算平移
                    // 移动量 = 新中点 - 旧中点
                    Point newCenter = new Point((newPoint.X + otherPoint.X) / 2, (newPoint.Y + otherPoint.Y) / 2);
                    Point oldCenter = new Point((oldPoint.X + otherPoint.X) / 2, (oldPoint.Y + otherPoint.Y) / 2);
                    Vector translation = newCenter - oldCenter;
                    
                    matrix.Translate(translation.X, translation.Y);

                    transform.Matrix = matrix;
                    UpdatePenThickness(matrix.M11);
                }
            }

            // 更新当前触摸点位置
            _activeTouches[e.TouchDevice.Id] = newPoint;
        }

        private void MyCanvas_TouchUp(object sender, TouchEventArgs e)
        {
            MyCanvas.ReleaseTouchCapture(e.TouchDevice);

            if (_activeTouches.ContainsKey(e.TouchDevice.Id))
            {
                _activeTouches.Remove(e.TouchDevice.Id);
            }

            // 如果手指少于2个，恢复编辑模式
            if (_activeTouches.Count < 2 && MyCanvas.EditingMode == InkCanvasEditingMode.None)
            {
                MyCanvas.EditingMode = _lastEditingMode;
            }
        }

        private void UpdatePenThickness(double currentScale)
        {
            // 避免除以0
            if (currentScale <= 0) currentScale = 1;

            // 计算新的笔触粗细：基准粗细 / 当前缩放倍率
            // 这样当画布放大时，笔触变细（世界坐标），从而在屏幕上看起来粗细不变
            double newThickness = _baseThickness / currentScale;

            var da = MyCanvas.DefaultDrawingAttributes;
            da.Width = newThickness;
            da.Height = newThickness;
        }

        #endregion

        #region Mouse Interaction Handlers (Zoom & Pan)

        private void MyCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var transform = MyCanvas.RenderTransform as MatrixTransform;
            if (transform == null) return;

            var matrix = transform.Matrix;
            var scale = e.Delta > 0 ? 1.1 : 0.9; // 滚轮向上放大，向下缩小

            double currentScale = matrix.M11;
            double targetScale = currentScale * scale;

            // 限制缩放倍率：0.5x ~ 5.0x
            if (targetScale < 0.5)
            {
                scale = 0.5 / currentScale;
            }
            else if (targetScale > 5.0)
            {
                scale = 5.0 / currentScale;
            }

            // 以鼠标位置为中心进行缩放
            var position = e.GetPosition(MyCanvas);
            matrix.ScaleAt(scale, scale, position.X, position.Y);

            transform.Matrix = matrix;

            // 视觉优化：调整笔触粗细
            UpdatePenThickness(matrix.M11);

            e.Handled = true;
        }

        private void MyCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 按住空格键 + 鼠标左键 -> 开始平移
            if (Keyboard.IsKeyDown(Key.Space) && e.ChangedButton == MouseButton.Left)
            {
                _isPanning = true;
                _lastMousePosition = e.GetPosition(this);
                
                // 记录当前模式并暂停书写
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
                var currentPosition = e.GetPosition(this);
                var offset = currentPosition - _lastMousePosition;

                var transform = MyCanvas.RenderTransform as MatrixTransform;
                if (transform != null)
                {
                    var matrix = transform.Matrix;
                    matrix.Translate(offset.X, offset.Y);
                    transform.Matrix = matrix;
                }

                _lastMousePosition = currentPosition;
            }
        }

        private void MyCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning && e.ChangedButton == MouseButton.Left)
            {
                _isPanning = false;
                MyCanvas.ReleaseMouseCapture();
                
                // 恢复之前的编辑模式
                MyCanvas.EditingMode = _lastEditingMode;
                e.Handled = true;
            }
        }

        #endregion

        private void RadioPen_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (RadioPen.IsChecked == true)
            {
                e.Handled = true;
                // 使用 Dispatcher 延迟打开，防止 Popup 立即关闭
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    PopupPenSettings.IsOpen = true;
                }));
            }
        }

        private void RadioPen_Checked(object sender, RoutedEventArgs e)
        {
            // 切换到墨迹模式
            MyCanvas.EditingMode = InkCanvasEditingMode.Ink;
        }

        private void RadioEraser_Checked(object sender, RoutedEventArgs e)
        {
            // 切换到按点擦除模式
            MyCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
            
            // 设置擦除器形状为圆形，大小适中
            MyCanvas.EraserShape = new EllipseStylusShape(16, 16);
        }

        private void RadioSelect_Checked(object sender, RoutedEventArgs e)
        {
            // 切换到选择模式
            MyCanvas.EditingMode = InkCanvasEditingMode.Select;
        }

        private void Thickness_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && double.TryParse(rb.Tag?.ToString(), out double thickness))
            {
                _baseThickness = thickness;
                
                // 立即应用新的粗细（考虑当前缩放）
                var transform = MyCanvas.RenderTransform as MatrixTransform;
                double currentScale = transform?.Matrix.M11 ?? 1.0;
                UpdatePenThickness(currentScale);
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
                catch (System.FormatException)
                {
                    // 处理颜色转换失败的情况，例如记录日志或使用默认颜色
                    MyCanvas.DefaultDrawingAttributes.Color = Colors.White;
                }
                
                // 选择颜色后关闭弹窗
                PopupPenSettings.IsOpen = false;
            }
        }
    }
}