using System;
using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;

namespace WindBoard.Board.Editing
{
    /// <summary>
    /// 笔迹“框选”命中测试（纯计算逻辑）。
    ///
    /// 设计目标：
    /// - 选择工具使用矩形框选命中最上层对象；
    /// - 逻辑不依赖 UI/渲染，便于单元测试；
    /// - 与 Bounds 逻辑保持一致（包含笔宽 padding）。
    /// </summary>
    internal static class StrokeRectSelectTest
    {
        /// <summary>
        /// 在给定世界坐标矩形范围内命中“所有相交”的笔迹（按列表顺序：越靠后越靠上）。
        /// </summary>
        /// <remarks>
        /// 用途：框选多个笔迹并作为整体进行移动/缩放/旋转等操作。
        /// </remarks>
        internal static List<Stroke> HitTestStrokesInWorldRect(IReadOnlyList<Stroke> strokes, Vector2 minWorld, Vector2 maxWorld)
        {
            if (strokes is null)
            {
                throw new ArgumentNullException(nameof(strokes));
            }

            var hits = new List<Stroke>();
            for (int i = 0; i < strokes.Count; i++)
            {
                Stroke stroke = strokes[i];
                if (IsStrokeIntersectWorldRect(stroke, minWorld, maxWorld))
                {
                    hits.Add(stroke);
                }
            }

            return hits;
        }

        /// <summary>
        /// 在给定世界坐标矩形范围内命中“最上层”笔迹（按列表顺序：越靠后越靠上）。
        /// </summary>
        internal static Stroke? HitTestTopMostStrokeInWorldRect(IReadOnlyList<Stroke> strokes, Vector2 minWorld, Vector2 maxWorld)
        {
            if (strokes is null)
            {
                throw new ArgumentNullException(nameof(strokes));
            }

            // 反向遍历：后绘制的笔迹在视觉上更靠上，应优先被选中。
            for (int i = strokes.Count - 1; i >= 0; i--)
            {
                Stroke stroke = strokes[i];
                if (IsStrokeIntersectWorldRect(stroke, minWorld, maxWorld))
                {
                    return stroke;
                }
            }

            return null;
        }

        internal static bool IsStrokeIntersectWorldRect(Stroke stroke, Vector2 minWorld, Vector2 maxWorld)
        {
            if (stroke is null)
            {
                throw new ArgumentNullException(nameof(stroke));
            }

            if (stroke.Points.Count == 0)
            {
                return false;
            }

            // 为避免依赖 stroke.HasBounds（导入/外部构造可能未计算），这里按点列“计算等价 Bounds”做 AABB 相交判断。
            GetStrokeBoundsWorld(stroke, out Vector2 strokeMin, out Vector2 strokeMax);
            return IntersectsAabb(strokeMin, strokeMax, minWorld, maxWorld);
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

        private static bool IntersectsAabb(Vector2 aMin, Vector2 aMax, Vector2 bMin, Vector2 bMax)
        {
            return aMin.X <= bMax.X
                && aMax.X >= bMin.X
                && aMin.Y <= bMax.Y
                && aMax.Y >= bMin.Y;
        }
    }
}
