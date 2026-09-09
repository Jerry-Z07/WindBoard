using System;
using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Items;
using Vortice.Mathematics;

namespace WindBoard.Board.Editing
{
    /// <summary>
    /// 绘制条目“框选”命中测试（按条目类型单点分发，纯计算逻辑）。
    ///
    /// 设计目标：
    /// - 选择工具使用矩形框选命中最上层对象；
    /// - 逻辑不依赖 UI/渲染，便于单元测试；
    /// - 折线笔迹的“线段/端点与矩形真实距离”算法保留为 Stroke 分支，
    ///   其它条目类型走“世界包围盒 AABB 相交”通用路径。
    /// </summary>
    internal static class InkItemRectSelectTest
    {
        /// <summary>
        /// 在给定世界坐标矩形范围内命中“所有相交”的条目（按列表顺序：越靠后越靠上）。
        /// </summary>
        /// <remarks>
        /// 用途：框选多个条目并作为整体进行移动/缩放/旋转等操作。
        /// </remarks>
        internal static List<IBoardInkItem> HitTestInkItemsInWorldRect(IReadOnlyList<IBoardInkItem> items, Vector2 minWorld, Vector2 maxWorld)
        {
            ArgumentNullException.ThrowIfNull(items);

            var hits = new List<IBoardInkItem>();
            for (int i = 0; i < items.Count; i++)
            {
                IBoardInkItem item = items[i];
                if (IsInkItemIntersectWorldRect(item, minWorld, maxWorld))
                {
                    hits.Add(item);
                }
            }

            return hits;
        }

        /// <summary>
        /// 在给定世界坐标矩形范围内命中“最上层”条目（按列表顺序：越靠后越靠上）。
        /// </summary>
        internal static IBoardInkItem? HitTestTopMostInkItemInWorldRect(IReadOnlyList<IBoardInkItem> items, Vector2 minWorld, Vector2 maxWorld)
        {
            ArgumentNullException.ThrowIfNull(items);

            // 反向遍历：后绘制的条目在视觉上更靠上，应优先被选中。
            for (int i = items.Count - 1; i >= 0; i--)
            {
                IBoardInkItem item = items[i];
                if (IsInkItemIntersectWorldRect(item, minWorld, maxWorld))
                {
                    return item;
                }
            }

            return null;
        }

        /// <summary>
        /// 判断某个绘制条目是否与给定世界坐标矩形相交（按条目类型分发）。
        /// </summary>
        internal static bool IsInkItemIntersectWorldRect(IBoardInkItem item, Vector2 minWorld, Vector2 maxWorld)
        {
            ArgumentNullException.ThrowIfNull(item);

            switch (item)
            {
                case Stroke stroke:
                    return IsStrokeIntersectWorldRect(stroke, minWorld, maxWorld);

                default:
                {
                    // 通用路径：条目世界包围盒（AABB）与选择矩形相交；无有效包围盒时不命中。
                    Rect bounds = item.BoundsWorld;
                    if (bounds.Width <= 0.0f && bounds.Height <= 0.0f)
                    {
                        return false;
                    }

                    return IntersectsAabb(
                        new Vector2(bounds.Left, bounds.Top),
                        new Vector2(bounds.Right, bounds.Bottom),
                        minWorld,
                        maxWorld);
                }
            }
        }

        /// <summary>
        /// 判断某条折线笔迹是否与给定世界坐标矩形相交（Stroke 分支，折线算法原样保留）。
        /// </summary>
        internal static bool IsStrokeIntersectWorldRect(Stroke stroke, Vector2 minWorld, Vector2 maxWorld)
        {
            ArgumentNullException.ThrowIfNull(stroke);

            if (stroke.Points.Count == 0)
            {
                return false;
            }

            // 先用等价 Bounds 做快速剔除，再按线段/端点与矩形的真实距离判断，避免仅 AABB 重叠时误命中。
            GetStrokeBoundsWorld(stroke, out Vector2 strokeMin, out Vector2 strokeMax);
            if (!IntersectsAabb(strokeMin, strokeMax, minWorld, maxWorld))
            {
                return false;
            }

            return IntersectsStrokeGeometry(stroke, minWorld, maxWorld);
        }

        private static bool IntersectsStrokeGeometry(Stroke stroke, Vector2 minWorld, Vector2 maxWorld)
        {
            Vector2 rectMin = new(
                Math.Min(minWorld.X, maxWorld.X),
                Math.Min(minWorld.Y, maxWorld.Y));
            Vector2 rectMax = new(
                Math.Max(minWorld.X, maxWorld.X),
                Math.Max(minWorld.Y, maxWorld.Y));

            for (int i = 0; i < stroke.Points.Count; i++)
            {
                StrokePoint point = stroke.Points[i];
                float pointRadius = GetHalfStrokeWidthWorld(stroke, point.Pressure);
                if (DistanceSquaredPointToRect(point.Position, rectMin, rectMax) <= pointRadius * pointRadius)
                {
                    return true;
                }
            }

            for (int i = 1; i < stroke.Points.Count; i++)
            {
                StrokePoint from = stroke.Points[i - 1];
                StrokePoint to = stroke.Points[i];
                float segmentRadius = GetHalfStrokeWidthWorld(stroke, from.Pressure, to.Pressure);

                if (IsSegmentIntersectWorldRect(from.Position, to.Position, segmentRadius, rectMin, rectMax))
                {
                    return true;
                }
            }

            return false;
        }

        private static void GetStrokeBoundsWorld(Stroke stroke, out Vector2 minWorld, out Vector2 maxWorld)
        {
            Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);

            for (int i = 0; i < stroke.Points.Count; i++)
            {
                StrokePoint p = stroke.Points[i];
                float halfWidth = GetHalfStrokeWidthWorld(stroke, p.Pressure);
                Vector2 pMin = p.Position - new Vector2(halfWidth, halfWidth);
                Vector2 pMax = p.Position + new Vector2(halfWidth, halfWidth);
                min = new Vector2(Math.Min(min.X, pMin.X), Math.Min(min.Y, pMin.Y));
                max = new Vector2(Math.Max(max.X, pMax.X), Math.Max(max.Y, pMax.Y));
            }

            minWorld = min;
            maxWorld = max;
        }

        private static float GetHalfStrokeWidthWorld(Stroke stroke, float pressure)
        {
            float widthFactor = stroke.EnablePressure
                ? Math.Clamp(pressure, 0.1f, 1.0f)
                : 1.0f;

            // 与渲染/Bounds 逻辑保持一致：BaseSize 是直径，且最小半径不小于 0.25。
            return Math.Max(0.25f, stroke.BaseSize * widthFactor / 2.0f);
        }

        private static float GetHalfStrokeWidthWorld(Stroke stroke, float pressure0, float pressure1)
        {
            float widthFactor = stroke.EnablePressure
                ? Math.Clamp((pressure0 + pressure1) / 2.0f, 0.1f, 1.0f)
                : 1.0f;

            return Math.Max(0.25f, stroke.BaseSize * widthFactor / 2.0f);
        }

        private static bool IntersectsAabb(Vector2 aMin, Vector2 aMax, Vector2 bMin, Vector2 bMax)
        {
            return aMin.X <= bMax.X
                && aMax.X >= bMin.X
                && aMin.Y <= bMax.Y
                && aMax.Y >= bMin.Y;
        }

        private static bool IsSegmentIntersectWorldRect(Vector2 from, Vector2 to, float radiusWorld, Vector2 rectMin, Vector2 rectMax)
        {
            if (IsPointInsideRect(from, rectMin, rectMax) || IsPointInsideRect(to, rectMin, rectMax))
            {
                return true;
            }

            Vector2 topLeft = rectMin;
            Vector2 topRight = new(rectMax.X, rectMin.Y);
            Vector2 bottomLeft = new(rectMin.X, rectMax.Y);
            Vector2 bottomRight = rectMax;

            if (SegmentMath2D.SegmentsIntersect(from, to, topLeft, topRight)
                || SegmentMath2D.SegmentsIntersect(from, to, topRight, bottomRight)
                || SegmentMath2D.SegmentsIntersect(from, to, bottomRight, bottomLeft)
                || SegmentMath2D.SegmentsIntersect(from, to, bottomLeft, topLeft))
            {
                return true;
            }

            float radiusSquared = radiusWorld * radiusWorld;
            if (DistanceSquaredPointToRect(from, rectMin, rectMax) <= radiusSquared
                || DistanceSquaredPointToRect(to, rectMin, rectMax) <= radiusSquared)
            {
                return true;
            }

            return SegmentMath2D.DistanceSquaredPointToSegment(topLeft, from, to) <= radiusSquared
                || SegmentMath2D.DistanceSquaredPointToSegment(topRight, from, to) <= radiusSquared
                || SegmentMath2D.DistanceSquaredPointToSegment(bottomRight, from, to) <= radiusSquared
                || SegmentMath2D.DistanceSquaredPointToSegment(bottomLeft, from, to) <= radiusSquared;
        }

        private static bool IsPointInsideRect(Vector2 point, Vector2 rectMin, Vector2 rectMax)
        {
            return point.X >= rectMin.X
                && point.X <= rectMax.X
                && point.Y >= rectMin.Y
                && point.Y <= rectMax.Y;
        }

        private static float DistanceSquaredPointToRect(Vector2 point, Vector2 rectMin, Vector2 rectMax)
        {
            float dx = 0.0f;
            if (point.X < rectMin.X)
            {
                dx = rectMin.X - point.X;
            }
            else if (point.X > rectMax.X)
            {
                dx = point.X - rectMax.X;
            }

            float dy = 0.0f;
            if (point.Y < rectMin.Y)
            {
                dy = rectMin.Y - point.Y;
            }
            else if (point.Y > rectMax.Y)
            {
                dy = point.Y - rectMax.Y;
            }

            return dx * dx + dy * dy;
        }
    }
}
