using System;
using System.Numerics;
using WindBoard.Board;

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

