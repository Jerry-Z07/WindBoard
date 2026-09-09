using System;
using System.Numerics;
using Vortice.Mathematics;
using WindBoard.Board;
using WindBoard.Board.Items;

namespace WindBoard.Rendering.Board
{
    internal static class BoardSceneMath
    {
        internal static float GetAdaptiveGridStepWorld(float zoom)
        {
            // 基准：zoom=1 时每 40 DIP 一格。
            // 通过调整世界坐标步长，让屏幕上的网格密度保持在一个稳定范围内（避免缩放后过密或过稀）。
            float step = 40.0f;
            float stepScreen = step * zoom;

            while (stepScreen < 20.0f)
            {
                step *= 2.0f;
                stepScreen = step * zoom;
            }

            while (stepScreen > 80.0f)
            {
                step /= 2.0f;
                stepScreen = step * zoom;
            }

            return step;
        }

        internal static float GetStrokeWidthFactor(float normalizedPressure)
        {
            return Math.Clamp(normalizedPressure, 0.1f, 1.0f);
        }

        /// <summary>
        /// 判断笔迹层条目是否与可见区域相交（按条目类型单点分发的可见性入口）。
        /// </summary>
        /// <remarks>
        /// 折线笔迹（Stroke）沿用既有折线可见性算法；其它条目类型走通用 Bounds 路径，
        /// 后续形状类型接入时无需改动调用方。
        /// </remarks>
        internal static bool IsInkItemVisible(IBoardInkItem item, Vector2 visibleMinWorld, Vector2 visibleMaxWorld)
        {
            switch (item)
            {
                case Stroke stroke:
                    return IsStrokeVisible(stroke, visibleMinWorld, visibleMaxWorld);

                default:
                {
                    // 通用路径：按条目世界包围盒（AABB）判断。
                    // 无有效包围盒（空矩形，宽高均为 0）时默认可见，避免误删绘制。
                    Rect bounds = item.BoundsWorld;
                    if (bounds.Width <= 0.0f && bounds.Height <= 0.0f)
                    {
                        return true;
                    }

                    return IntersectsAabb(
                        new Vector2(bounds.Left, bounds.Top),
                        new Vector2(bounds.Right, bounds.Bottom),
                        visibleMinWorld,
                        visibleMaxWorld);
                }
            }
        }

        internal static bool IsStrokeVisible(Stroke stroke, Vector2 visibleMinWorld, Vector2 visibleMaxWorld)
        {
            if (stroke.Points.Count == 0)
            {
                return false;
            }

            // 某些情况下笔迹可能还未计算 Bounds（例如外部构造/导入），此时默认视为可见以避免误删绘制。
            if (!stroke.HasBounds)
            {
                return true;
            }

            return IntersectsAabb(stroke.BoundsMin, stroke.BoundsMax, visibleMinWorld, visibleMaxWorld);
        }

        internal static bool IntersectsAabb(Vector2 aMin, Vector2 aMax, Vector2 bMin, Vector2 bMax)
        {
            return aMin.X <= bMax.X
                && aMax.X >= bMin.X
                && aMin.Y <= bMax.Y
                && aMax.Y >= bMin.Y;
        }
    }
}

