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
            else if (MyCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint && e.ChangedButton == MouseButton.Left)
            {
                HandleEraserMouseDown(e);
            }
        }

        private void MyCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            // 仅在按下左键（书写/擦除/拖拽）时检测扩容
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                AutoExpandCanvas(e.GetPosition(MyCanvas));
            }

            if (_isPanning)
            {
                Point currentPosition = e.GetPosition(Viewport);
                Vector delta = currentPosition - _lastMousePosition;

                PanBy(delta);

                _lastMousePosition = currentPosition;
                e.Handled = true;
            }
            else if (MyCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint && _isEraserPressed)
            {
                HandleEraserMouseMove(e);
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
            else if (MyCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint && e.ChangedButton == MouseButton.Left)
            {
                HandleEraserMouseUp(e);
            }
        }
    }
}