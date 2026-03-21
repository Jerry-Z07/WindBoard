using System;
using Windows.Graphics;
using WindBoard.Features.ScreenAnnotation.Models;

namespace WindBoard.Features.ScreenAnnotation.UI
{
    /// <summary>
    /// 屏幕批注工具栏的纯逻辑辅助方法。
    /// </summary>
    internal static class ScreenAnnotationToolbarBehavior
    {
        internal static bool IsSecondaryClick(ScreenAnnotationMode currentMode, ScreenAnnotationMode requestedMode)
        {
            return currentMode == requestedMode;
        }

        internal static RectInt32 BuildFlyoutHostBounds(
            ScreenAnnotationDisplayTarget displayTarget,
            RectInt32 currentBounds,
            int flyoutWidth,
            int flyoutHeight,
            int toolbarHeight)
        {
            RectInt32 area = displayTarget.WorkArea.Width > 0 && displayTarget.WorkArea.Height > 0
                ? displayTarget.WorkArea
                : displayTarget.Bounds;

            int width = Math.Clamp(Math.Max(currentBounds.Width, flyoutWidth), 1, Math.Max(1, area.Width));
            int height = Math.Clamp(toolbarHeight + Math.Max(0, flyoutHeight), 1, Math.Max(1, area.Height));
            int bottom = currentBounds.Y + currentBounds.Height;

            int minX = area.X;
            int maxX = area.X + Math.Max(0, area.Width - width);
            int minY = area.Y;
            int maxY = area.Y + Math.Max(0, area.Height - height);

            return new RectInt32(
                Math.Clamp(currentBounds.X, minX, maxX),
                Math.Clamp(bottom - height, minY, maxY),
                width,
                height);
        }

        internal static RectInt32 BuildCompactToolbarBounds(
            ScreenAnnotationDisplayTarget displayTarget,
            RectInt32 currentBounds,
            int compactWidth,
            int compactHeight)
        {
            RectInt32 area = displayTarget.WorkArea.Width > 0 && displayTarget.WorkArea.Height > 0
                ? displayTarget.WorkArea
                : displayTarget.Bounds;

            int width = Math.Clamp(compactWidth, 1, Math.Max(1, area.Width));
            int height = Math.Clamp(compactHeight, 1, Math.Max(1, area.Height));
            int bottom = currentBounds.Y + currentBounds.Height;

            int minX = area.X;
            int maxX = area.X + Math.Max(0, area.Width - width);
            int minY = area.Y;
            int maxY = area.Y + Math.Max(0, area.Height - height);

            return new RectInt32(
                Math.Clamp(currentBounds.X, minX, maxX),
                Math.Clamp(bottom - height, minY, maxY),
                width,
                height);
        }
    }
}

