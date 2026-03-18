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
        private const int DefaultToolbarMargin = 8;

        /// <summary>
        /// 计算工具栏默认位置。
        /// </summary>
        /// <remarks>
        /// 约定：
        /// - 优先放在工作区右上角；
        /// - 若工具栏尺寸大于工作区，则钳制到工作区内，避免初始位置跑出屏幕。
        /// </remarks>
        internal RectInt32 GetInitialToolbarBounds(int width, int height)
        {
            RectInt32 area = WorkArea.Width > 0 && WorkArea.Height > 0 ? WorkArea : Bounds;
            int toolbarWidth = Math.Clamp(width, 1, Math.Max(1, area.Width));
            int toolbarHeight = Math.Clamp(height, 1, Math.Max(1, area.Height));

            int minX = area.X;
            int maxX = area.X + Math.Max(0, area.Width - toolbarWidth);
            int minY = area.Y;
            int maxY = area.Y + Math.Max(0, area.Height - toolbarHeight);

            int preferredX = area.X + area.Width - toolbarWidth - DefaultToolbarMargin;
            int preferredY = area.Y + DefaultToolbarMargin;

            return new RectInt32(
                Math.Clamp(preferredX, minX, maxX),
                Math.Clamp(preferredY, minY, maxY),
                toolbarWidth,
                toolbarHeight);
        }
    }
}
