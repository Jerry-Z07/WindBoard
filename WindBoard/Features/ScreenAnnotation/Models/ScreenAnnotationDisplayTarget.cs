using System;
using Windows.Graphics;

namespace WindBoard.Features.ScreenAnnotation.Models
{
    /// <summary>
    /// 屏幕批注目标显示器信息。
    /// </summary>
    internal readonly record struct ScreenAnnotationDisplayTarget(
        nint MonitorHandle,
        RectInt32 Bounds,
        RectInt32 WorkArea)
    {
        internal const int DefaultToolbarMargin = 8;

        /// <summary>
        /// 计算工具栏默认位置。
        /// </summary>
        /// <remarks>
        /// 约定：
        /// - 优先放在工作区左下角；
        /// - 若工具栏尺寸大于工作区，则钳制到工作区内，避免初始位置跑出屏幕。
        /// </remarks>
        internal RectInt32 GetInitialToolbarBounds(int width, int height, int margin = DefaultToolbarMargin)
        {
            RectInt32 area = GetVisibleArea();
            int safeMargin = Math.Max(0, margin);
            int toolbarWidth = Math.Clamp(width, 1, Math.Max(1, area.Width));
            int toolbarHeight = Math.Clamp(height, 1, Math.Max(1, area.Height));

            return ClampBoundsToVisibleArea(
                new RectInt32(
                    area.X + safeMargin,
                    area.Y + area.Height - toolbarHeight - safeMargin,
                    toolbarWidth,
                    toolbarHeight));
        }

        /// <summary>
        /// 把任意窗口矩形钳制到当前显示器可见工作区内。
        /// </summary>
        internal RectInt32 ClampBoundsToVisibleArea(RectInt32 bounds)
        {
            RectInt32 area = GetVisibleArea();
            int width = Math.Clamp(bounds.Width, 1, Math.Max(1, area.Width));
            int height = Math.Clamp(bounds.Height, 1, Math.Max(1, area.Height));

            int minX = area.X;
            int maxX = area.X + Math.Max(0, area.Width - width);
            int minY = area.Y;
            int maxY = area.Y + Math.Max(0, area.Height - height);

            return new RectInt32(
                Math.Clamp(bounds.X, minX, maxX),
                Math.Clamp(bounds.Y, minY, maxY),
                width,
                height);
        }

        private RectInt32 GetVisibleArea()
        {
            return WorkArea.Width > 0 && WorkArea.Height > 0 ? WorkArea : Bounds;
        }
    }
}
