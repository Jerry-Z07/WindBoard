using System;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Viewport;
using Vortice.Mathematics;

namespace WindBoard.Interaction
{
    internal static class BoardInputDirtyRectCalculator
    {
        internal static Rect? UpdatePendingStrokeDirtyRect(
            Rect? pendingStrokeDirtyRectDip,
            Stroke stroke,
            BoardViewport viewport,
            Vector2 latestScreenDip,
            float extraPaddingDip)
        {
            // 目标：在“增量书写”时只重绘必要区域，提高交互帧率。
            // 规则：基于“最新点 + 上一个点（若存在）”形成的线段包围盒，再按笔宽与额外 padding 扩展。
            // 注意：这里的 Rect 使用 DIP（屏幕坐标），由调用方统一用于局部重绘与 Present dirty rect 计算。

            if (stroke.Points.Count == 0)
            {
                return pendingStrokeDirtyRectDip;
            }

            float zoom = viewport.Zoom;
            if (zoom <= 0.0001f)
            {
                return pendingStrokeDirtyRectDip;
            }

            int pointCount = stroke.Points.Count;

            float padding = extraPaddingDip;
            float x0 = latestScreenDip.X;
            float y0 = latestScreenDip.Y;
            float x1 = x0;
            float y1 = y0;

            float pressure0 = stroke.Points[^1].Pressure;
            float pressure1 = pressure0;

            if (pointCount >= 2)
            {
                StrokePoint prev = stroke.Points[^2];
                Vector2 prevScreen = Vector2.Transform(prev.Position, viewport.GetWorldToScreenTransform());
                x0 = prevScreen.X;
                y0 = prevScreen.Y;
                x1 = latestScreenDip.X;
                y1 = latestScreenDip.Y;

                pressure0 = prev.Pressure;
                pressure1 = stroke.Points[^1].Pressure;
            }

            float widthFactor = stroke.EnablePressure
                ? Math.Clamp((pressure0 + pressure1) / 2.0f, 0.1f, 1.0f)
                : 1.0f;

            // stroke.BaseSize 是世界坐标下的“笔迹直径”，换算到屏幕需要乘以 zoom。
            float halfWidthWorld = Math.Max(0.25f, stroke.BaseSize * widthFactor / 2.0f);
            float halfWidthScreen = halfWidthWorld * zoom;
            padding += halfWidthScreen;

            float left = Math.Min(x0, x1) - padding;
            float top = Math.Min(y0, y1) - padding;
            float right = Math.Max(x0, x1) + padding;
            float bottom = Math.Max(y0, y1) + padding;

            Rect rectDip = Rect.FromLTRB(left, top, right, bottom);
            if (pendingStrokeDirtyRectDip is Rect existing)
            {
                return Rect.FromLTRB(
                    Math.Min(existing.Left, rectDip.Left),
                    Math.Min(existing.Top, rectDip.Top),
                    Math.Max(existing.Right, rectDip.Right),
                    Math.Max(existing.Bottom, rectDip.Bottom));
            }

            return rectDip;
        }
    }
}

