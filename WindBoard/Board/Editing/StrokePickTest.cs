using System;
using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;

namespace WindBoard.Board.Editing
{
    /// <summary>
    /// 笔迹“点选”命中测试（纯计算逻辑）。
    ///
    /// 设计目标：
    /// - 选择工具需要在“点击/触摸”位置命中最上层对象；
    /// - 逻辑独立于 UI/渲染，便于单元测试；
    /// - 未来接入“导入内容/元素”时，可复用同样的模式扩展（例如新增 ElementPickTest）。
    /// </summary>
    internal static class StrokePickTest
    {
        /// <summary>
        /// 在给定世界坐标位置命中“最上层”笔迹（按列表顺序：越靠后越靠上）。
        /// </summary>
        internal static Stroke? HitTestTopMostStroke(IReadOnlyList<Stroke> strokes, Vector2 pointWorld, float toleranceWorld)
        {
            if (strokes is null)
            {
                throw new ArgumentNullException(nameof(strokes));
            }

            // 反向遍历：后绘制的笔迹在视觉上更靠上，应优先被选中。
            for (int i = strokes.Count - 1; i >= 0; i--)
            {
                Stroke stroke = strokes[i];
                if (IsStrokeHitByPoint(stroke, pointWorld, toleranceWorld))
                {
                    return stroke;
                }
            }

            return null;
        }

        /// <summary>
        /// 判断某条笔迹是否被“点选”命中。
        /// </summary>
        internal static bool IsStrokeHitByPoint(Stroke stroke, Vector2 pointWorld, float toleranceWorld)
        {
            if (stroke is null)
            {
                throw new ArgumentNullException(nameof(stroke));
            }

            if (stroke.Points.Count == 0)
            {
                return false;
            }

            // 快速过滤：Bounds + 最大笔宽 + 额外容差。
            if (stroke.HasBounds)
            {
                float maxHalfWidth = GetMaxHalfStrokeWidthWorld(stroke);
                float pad = Math.Max(0.0f, toleranceWorld) + maxHalfWidth;
                Vector2 min = stroke.BoundsMin - new Vector2(pad, pad);
                Vector2 max = stroke.BoundsMax + new Vector2(pad, pad);
                if (pointWorld.X < min.X || pointWorld.X > max.X || pointWorld.Y < min.Y || pointWorld.Y > max.Y)
                {
                    return false;
                }
            }

            // 单点笔迹：按“圆点”处理（半径 = 笔宽/2 + 额外容差）。
            if (stroke.Points.Count == 1)
            {
                StrokePoint p = stroke.Points[0];
                float halfWidth = GetHalfStrokeWidthWorld(stroke, p.Pressure, p.Pressure);
                float r = Math.Max(0.0f, toleranceWorld) + halfWidth;
                return Vector2.DistanceSquared(pointWorld, p.Position) <= r * r;
            }

            // 多点笔迹：把笔迹视为折线段集合，逐段做“点到线段”的最短距离测试。
            for (int i = 1; i < stroke.Points.Count; i++)
            {
                StrokePoint p0 = stroke.Points[i - 1];
                StrokePoint p1 = stroke.Points[i];

                float halfWidth = GetHalfStrokeWidthWorld(stroke, p0.Pressure, p1.Pressure);
                float r = Math.Max(0.0f, toleranceWorld) + halfWidth;
                float d2 = SegmentMath2D.DistanceSquaredPointToSegment(pointWorld, p0.Position, p1.Position);
                if (d2 <= r * r)
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

        private static float GetMaxHalfStrokeWidthWorld(Stroke stroke)
        {
            // pressure 经过 clamp 后最大为 1，因此最大半径不会超过 BaseSize/2（但不小于 0.25）。
            return Math.Max(0.25f, stroke.BaseSize / 2.0f);
        }
    }
}

