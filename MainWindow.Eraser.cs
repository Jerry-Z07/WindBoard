using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Ink;

namespace WindBoard
{
    public partial class MainWindow
    {
        // 橡皮擦基准尺寸（屏幕上看到的尺寸，随缩放保持一致）
        private double _eraserBaseWidth = 40.0;
        private double _eraserBaseHeight = 80.0;
        // 橡皮擦浮标圆角（屏幕上看到的圆角，随缩放保持一致）
        private double _eraserBaseCornerRadius = 6.0;

        // 更新橡皮擦的可视化与命中测试形状（保持屏幕尺寸固定）
        private void UpdateEraserVisual(Point? center)
        {
            double wContent = _eraserBaseWidth / _zoom;
            double hContent = _eraserBaseHeight / _zoom;
            // 将屏幕偏移换算到内容坐标，确保缩放后仍是固定屏幕距离
            double offsetYContent = _eraserCursorOffsetY / _zoom; // 定义在另一个 partial 中

            // 圆角也要按 zoom 反算，保证屏幕圆角恒定
            double radiusContent = _eraserBaseCornerRadius / _zoom;

            // 更新可视游标大小与位置
            if (_eraserCursorRect != null)
            {
                _eraserCursorRect.Width = wContent;
                _eraserCursorRect.Height = hContent;

                // 更新圆角
                _eraserCursorRect.CornerRadius = new CornerRadius(radiusContent);

                if (center.HasValue)
                {
                    double left = center.Value.X - wContent / 2.0;
                    double topBase = center.Value.Y - hContent / 2.0;
                    double top = _isMouseErasing ? (topBase + offsetYContent) : topBase;
                    Canvas.SetLeft(_eraserCursorRect, left);
                    Canvas.SetTop(_eraserCursorRect, top);
                }
            }

            // 更新 InkCanvas 的橡皮擦形状
            if (RadioEraser != null && RadioEraser.IsChecked == true)
            {
                MyCanvas.EraserShape = new RectangleStylusShape(wContent, hContent);
            }

            // 覆盖层显隐（仅在按下时显示）
            if (_eraserOverlay != null)
            {
                _eraserOverlay.Visibility = (RadioEraser != null && RadioEraser.IsChecked == true && _isEraserPressed)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        // 统一输入订阅已接管橡皮擦游标与按压维护；旧的 HandleEraser* 方法已移除。
    }
}