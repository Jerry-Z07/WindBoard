using System;
using System.Numerics;

namespace WindBoard.Board.Editing
{
    /// <summary>
    /// 与“笔迹/橡皮擦”相关的命中检测算法（纯计算逻辑，便于单元测试与后续扩展）。
    /// </summary>
    internal static class StrokeHitTest
    {
        /// <summary>
        /// 判断某条笔迹是否被“橡皮擦轨迹线段”命中。
        /// </summary>
        internal static bool IsStrokeHitByEraserSegment(Stroke stroke, Vector2 eraserFromWorld, Vector2 eraserToWorld, Vector2 eraserRadiusWorld)
        {
            if (stroke.Points.Count == 0)
            {
                return false;
            }

            // 快速过滤：若笔迹有 Bounds，则只需判断橡皮擦的 AABB（扩展半径）是否与笔迹 AABB 相交。
            if (stroke.HasBounds)
            {
                Vector2 eraserMin = new(
                    Math.Min(eraserFromWorld.X, eraserToWorld.X) - eraserRadiusWorld.X,
                    Math.Min(eraserFromWorld.Y, eraserToWorld.Y) - eraserRadiusWorld.Y);

                Vector2 eraserMax = new(
                    Math.Max(eraserFromWorld.X, eraserToWorld.X) + eraserRadiusWorld.X,
                    Math.Max(eraserFromWorld.Y, eraserToWorld.Y) + eraserRadiusWorld.Y);

                if (!IntersectsAabb(stroke.BoundsMin, stroke.BoundsMax, eraserMin, eraserMax))
                {
                    return false;
                }
            }

            // 单点笔迹：按“圆点”处理（半径为笔宽的一半）。
            if (stroke.Points.Count == 1)
            {
                StrokePoint p = stroke.Points[0];
                float halfWidth = GetHalfStrokeWidthWorld(stroke, p.Pressure, p.Pressure);
                Vector2 r = new(
                    Math.Max(0.0f, eraserRadiusWorld.X) + halfWidth,
                    Math.Max(0.0f, eraserRadiusWorld.Y) + halfWidth);

                Vector2 inv = new(
                    1.0f / Math.Max(0.0000001f, r.X),
                    1.0f / Math.Max(0.0000001f, r.Y));

                return SegmentMath2D.DistanceSquaredPointToSegment(
                    new Vector2(p.Position.X * inv.X, p.Position.Y * inv.Y),
                    new Vector2(eraserFromWorld.X * inv.X, eraserFromWorld.Y * inv.Y),
                    new Vector2(eraserToWorld.X * inv.X, eraserToWorld.Y * inv.Y)) <= 1.0f;
            }

            // 多点笔迹：把笔迹视为折线段集合，与橡皮擦轨迹线段做“线段-线段最短距离”检测。
            for (int i = 1; i < stroke.Points.Count; i++)
            {
                StrokePoint p0 = stroke.Points[i - 1];
                StrokePoint p1 = stroke.Points[i];

                float halfWidth = GetHalfStrokeWidthWorld(stroke, p0.Pressure, p1.Pressure);
                Vector2 r = new(
                    Math.Max(0.0f, eraserRadiusWorld.X) + halfWidth,
                    Math.Max(0.0f, eraserRadiusWorld.Y) + halfWidth);

                Vector2 inv = new(
                    1.0f / Math.Max(0.0000001f, r.X),
                    1.0f / Math.Max(0.0000001f, r.Y));

                float d2 = SegmentMath2D.DistanceSquaredSegmentToSegment(
                    new Vector2(eraserFromWorld.X * inv.X, eraserFromWorld.Y * inv.Y),
                    new Vector2(eraserToWorld.X * inv.X, eraserToWorld.Y * inv.Y),
                    new Vector2(p0.Position.X * inv.X, p0.Position.Y * inv.Y),
                    new Vector2(p1.Position.X * inv.X, p1.Position.Y * inv.Y));

                if (d2 <= 1.0f)
                {
                    return true;
                }
            }

            return false;
        }

        private static float GetHalfStrokeWidthWorld(Stroke stroke, float pressure0, float pressure1)
        {
            float widthFactor = stroke.EnablePressure
                ? Math.Clamp((pressure0 + pressure1) / 2.0f, 0.1f, 1.0f)
                : 1.0f;

            // 与渲染/Bounds 逻辑保持一致：BaseSize 是直径，且最小半径不小于 0.25。
            return Math.Max(0.25f, stroke.BaseSize * widthFactor / 2.0f);
        }

        private static bool IntersectsAabb(Vector2 aMin, Vector2 aMax, Vector2 bMin, Vector2 bMax)
        {
            return aMin.X <= bMax.X
                && aMax.X >= bMin.X
                && aMin.Y <= bMax.Y
                && aMax.Y >= bMin.Y;
        }

    }
}
