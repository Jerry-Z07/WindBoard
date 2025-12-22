using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WindBoard
{
    public partial class MainWindow
    {
        #region Auto Expansion

        /// 自动扩容补丁函数：当操作点接近画布边缘时，自动增加画布尺寸
        /// <param name="currentContentPosition">当前操作点在 MyCanvas 内部的坐标</param>
        private void AutoExpandCanvas(Point currentContentPosition)
        {
            // 阈值/步长（内容坐标）
            const double ExpansionThreshold = 1000.0;
            const double ExpansionStep = 2000.0;

            double expandLeft = 0, expandTop = 0, expandRight = 0, expandBottom = 0;

            if (currentContentPosition.X < ExpansionThreshold) expandLeft = ExpansionStep;
            if (currentContentPosition.Y < ExpansionThreshold) expandTop = ExpansionStep;

            if (currentContentPosition.X > MyCanvas.Width - ExpansionThreshold) expandRight = ExpansionStep;
            if (currentContentPosition.Y > MyCanvas.Height - ExpansionThreshold) expandBottom = ExpansionStep;

            if (expandLeft == 0 && expandTop == 0 && expandRight == 0 && expandBottom == 0)
                return;

            // 先扩尺寸：左/上扩容也需要先把 Width/Height 变大，否则平移后内容可能被推到边界外
            double newW = MyCanvas.Width + expandLeft + expandRight;
            double newH = MyCanvas.Height + expandTop + expandBottom;
            double newSize = Math.Max(newW, newH); // 保持正方形

            if (newSize > MyCanvas.Width || newSize > MyCanvas.Height)
            {
                MyCanvas.Width = newSize;
                MyCanvas.Height = newSize;

                // 同步更新当前页的画布尺寸，防止切页回来尺寸回滚
                if (Pages.Count > 0)
                {
                    Pages[_currentPageIndex].CanvasWidth = MyCanvas.Width;
                    Pages[_currentPageIndex].CanvasHeight = MyCanvas.Height;
                }
            }

            // 左/上扩容：需要把已有内容整体往右/下挪，才能“腾出”左/上空间
            if (expandLeft > 0 || expandTop > 0)
            {
                // 正在书写(Ink)时，立刻 Transform 可能导致“动态笔迹”和最终笔迹错位
                bool inkingActive = (MyCanvas.EditingMode == InkCanvasEditingMode.Ink) &&
                                    (Mouse.LeftButton == MouseButtonState.Pressed || _activeTouches.Count > 0);

                if (inkingActive)
                {
                    _pendingShiftX += expandLeft;
                    _pendingShiftY += expandTop;
                }
                else
                {
                    ShiftCanvasContent(expandLeft, expandTop);
                }
            }

        }

        // 平移内容（笔迹 + 子元素 + 视口补偿）
        private void ShiftCanvasContent(double dx, double dy)
        {
            if (dx == 0 && dy == 0) return;

            // 平移笔迹
            var m = Matrix.Identity;
            m.Translate(dx, dy);
            MyCanvas.Strokes.Transform(m, false);

            // 平移 InkCanvas.Children（如果你未来往 InkCanvas 里加 UIElement，也能一起挪）
            foreach (UIElement child in MyCanvas.Children)
            {
                double left = InkCanvas.GetLeft(child);
                double top = InkCanvas.GetTop(child);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;

                InkCanvas.SetLeft(child, left + dx);
                InkCanvas.SetTop(child, top + dy);
            }

            // 关键：内容整体向右/下挪了，为了让用户“视野不跳”，ScrollViewer 的 offset 也要补偿
            Viewport.UpdateLayout();
            Viewport.ScrollToHorizontalOffset(Viewport.HorizontalOffset + dx * _zoom);
            Viewport.ScrollToVerticalOffset(Viewport.VerticalOffset + dy * _zoom);
        }

        private void MyCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
        {
            if (_pendingShiftX == 0 && _pendingShiftY == 0) return;

            double dx = _pendingShiftX;
            double dy = _pendingShiftY;
            _pendingShiftX = _pendingShiftY = 0;

            ShiftCanvasContent(dx, dy);
        }

        #endregion
    }
}