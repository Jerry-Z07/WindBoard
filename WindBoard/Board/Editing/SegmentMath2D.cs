using System;
using System.Numerics;

namespace WindBoard.Board.Editing
{
    /// <summary>
    /// 2D 几何辅助：面向“线段/点”的基础运算。
    ///
    /// 说明：
    /// - 这里的实现主要服务于擦除与命中测试（纯计算逻辑）。
    /// - 输入为 <see cref="float"/>，因此用较小 epsilon 做数值容错。
    /// </summary>
    internal static class SegmentMath2D
    {
        internal static float DistanceSquaredPointToSegment(Vector2 p, Vector2 a, Vector2 b)
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

        internal static float DistanceSquaredSegmentToSegment(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
        {
            // 2D 线段-线段最短距离：
            // 1) 若相交（含共线重叠），距离为 0；
            // 2) 否则取四个“端点到对方线段”的最小值。
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

        internal static bool SegmentsIntersect(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
        {
            // 线段相交测试（包含共线重叠）。
            const float eps = 0.00001f;

            float o1 = Cross(a1 - a0, b0 - a0);
            float o2 = Cross(a1 - a0, b1 - a0);
            float o3 = Cross(b1 - b0, a0 - b0);
            float o4 = Cross(b1 - b0, a1 - b0);

            // 一般情况：两端点分别位于对方线段两侧。
            bool intersect = o1 * o2 < 0.0f && o3 * o4 < 0.0f;

            if (!intersect)
            {
                // 共线/触碰：判断点是否在线段投影范围内。
                if (Math.Abs(o1) <= eps && OnSegment(a0, a1, b0, eps))
                {
                    intersect = true;
                }
                else if (Math.Abs(o2) <= eps && OnSegment(a0, a1, b1, eps))
                {
                    intersect = true;
                }
                else if (Math.Abs(o3) <= eps && OnSegment(b0, b1, a0, eps))
                {
                    intersect = true;
                }
                else if (Math.Abs(o4) <= eps && OnSegment(b0, b1, a1, eps))
                {
                    intersect = true;
                }
            }

            return intersect;
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
