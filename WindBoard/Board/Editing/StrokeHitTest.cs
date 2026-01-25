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
        internal static bool IsStrokeHitByEraserSegment(Stroke stroke, Vector2 eraserFromWorld, Vector2 eraserToWorld, float eraserRadiusWorld)
        {
            if (stroke.Points.Count == 0)
            {
                return false;
            }

            // 快速过滤：若笔迹有 Bounds，则只需判断橡皮擦的 AABB（扩展半径）是否与笔迹 AABB 相交。
            if (stroke.HasBounds)
            {
                Vector2 eraserMin = new(
                    Math.Min(eraserFromWorld.X, eraserToWorld.X) - eraserRadiusWorld,
                    Math.Min(eraserFromWorld.Y, eraserToWorld.Y) - eraserRadiusWorld);

                Vector2 eraserMax = new(
                    Math.Max(eraserFromWorld.X, eraserToWorld.X) + eraserRadiusWorld,
                    Math.Max(eraserFromWorld.Y, eraserToWorld.Y) + eraserRadiusWorld);

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
                float threshold = Math.Max(0.0f, eraserRadiusWorld) + halfWidth;
                return DistanceSquaredPointToSegment(p.Position, eraserFromWorld, eraserToWorld) <= threshold * threshold;
            }

            // 多点笔迹：把笔迹视为折线段集合，与橡皮擦轨迹线段做“线段-线段最短距离”检测。
            for (int i = 1; i < stroke.Points.Count; i++)
            {
                StrokePoint p0 = stroke.Points[i - 1];
                StrokePoint p1 = stroke.Points[i];

                float halfWidth = GetHalfStrokeWidthWorld(stroke, p0.Pressure, p1.Pressure);
                float threshold = Math.Max(0.0f, eraserRadiusWorld) + halfWidth;

                float d2 = DistanceSquaredSegmentToSegment(eraserFromWorld, eraserToWorld, p0.Position, p1.Position);
                if (d2 <= threshold * threshold)
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

        private static float DistanceSquaredPointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float abLenSq = ab.LengthSquared();
            if (abLenSq <= 0.0000001f)
            {
                return Vector2.DistanceSquared(p, a);
            }

            float t = Vector2.Dot(p - a, ab) / abLenSq;
            t = Math.Clamp(t, 0.0f, 1.0f);
            Vector2 proj = a + ab * t;
            return Vector2.DistanceSquared(p, proj);
        }

        private static float DistanceSquaredSegmentToSegment(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
        {
            // 这是 2D 版本的线段-线段最短距离计算：
            // 1) 若两线段相交（包含共线重叠），距离为 0；
            // 2) 否则最短距离来自“任一端点到对方线段”的最小值。
            // 这种写法易读且对擦除命中而言足够稳定。
            if (SegmentsIntersect(a0, a1, b0, b1))
            {
                return 0.0f;
            }

            float d0 = DistanceSquaredPointToSegment(a0, b0, b1);
            float d1 = DistanceSquaredPointToSegment(a1, b0, b1);
            float d2 = DistanceSquaredPointToSegment(b0, a0, a1);
            float d3 = DistanceSquaredPointToSegment(b1, a0, a1);

            return Math.Min(Math.Min(d0, d1), Math.Min(d2, d3));
        }

        private static bool SegmentsIntersect(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
        {
            // 线段相交测试（包含共线重叠）。由于输入为 float，使用一个较小 epsilon 做数值容错。
            const float eps = 0.00001f;

            float o1 = Cross(a1 - a0, b0 - a0);
            float o2 = Cross(a1 - a0, b1 - a0);
            float o3 = Cross(b1 - b0, a0 - b0);
            float o4 = Cross(b1 - b0, a1 - b0);

            // 一般情况：两端点分别位于对方线段两侧。
            if (o1 * o2 < 0.0f && o3 * o4 < 0.0f)
            {
                return true;
            }

            // 共线/触碰：判断点是否在线段投影范围内。
            if (Math.Abs(o1) <= eps && OnSegment(a0, a1, b0, eps))
            {
                return true;
            }

            if (Math.Abs(o2) <= eps && OnSegment(a0, a1, b1, eps))
            {
                return true;
            }

            if (Math.Abs(o3) <= eps && OnSegment(b0, b1, a0, eps))
            {
                return true;
            }

            if (Math.Abs(o4) <= eps && OnSegment(b0, b1, a1, eps))
            {
                return true;
            }

            return false;
        }

        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 p, float eps)
        {
            return p.X >= Math.Min(a.X, b.X) - eps
                && p.X <= Math.Max(a.X, b.X) + eps
                && p.Y >= Math.Min(a.Y, b.Y) - eps
                && p.Y <= Math.Max(a.Y, b.Y) + eps;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    }
}

