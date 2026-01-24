using System;
using Vortice.Mathematics;

namespace WindBoard.Rendering
{
    internal static class DxDirtyRectCalculator
    {
        internal static RectI[] CreatePanDirtyRectsPixels(int width, int height, int dxPixels, int dyPixels)
        {
            // 平移时上一帧内容可以通过 ScrollRect 复用，仅需重绘“新暴露出来”的区域。
            // 这里返回需要重绘的像素脏矩形（用于 Present1 的 DirtyRectangles）。

            RectI? vertical = null;
            if (dxPixels > 0)
            {
                vertical = new RectI(0, 0, dxPixels, height);
            }
            else if (dxPixels < 0)
            {
                vertical = new RectI(width + dxPixels, 0, -dxPixels, height);
            }

            RectI? horizontal = null;
            if (dyPixels > 0)
            {
                horizontal = new RectI(0, 0, width, dyPixels);
            }
            else if (dyPixels < 0)
            {
                horizontal = new RectI(0, height + dyPixels, width, -dyPixels);
            }

            if (vertical is null && horizontal is null)
            {
                return Array.Empty<RectI>();
            }

            if (vertical is not null && horizontal is not null)
            {
                return new[] { vertical.Value, horizontal.Value };
            }

            return new[] { vertical ?? horizontal ?? default };
        }
    }
}

