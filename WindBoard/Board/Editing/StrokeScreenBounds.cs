using System;
using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;
using Vortice.Mathematics;

namespace WindBoard.Board.Editing
{
    /// <summary>
    /// 笔迹 Bounds 变换辅助：将若干笔迹的世界坐标 Bounds 变换到目标坐标系，并求并集矩形。
    /// </summary>
    internal static class StrokeScreenBounds
    {
        /// <summary>
        /// 计算笔迹集合在屏幕坐标（DIP）下的包围盒。
        /// </summary>
        /// <remarks>
        /// 注意：
        /// - 这里基于 Stroke 的 BoundsMin/BoundsMax 计算，为了性能不遍历所有点；
        /// - 某些情况下笔迹可能还未计算 Bounds（例如外部构造/导入），会在这里兜底重建。
        /// </remarks>
        internal static bool TryGetStrokesBoundsScreenDip(
            IReadOnlyList<Stroke> strokes,
            Matrix3x2 worldToScreen,
            out Rect boundsScreenDip)
        {
            boundsScreenDip = default;

            if (strokes is null || strokes.Count == 0)
            {
                return false;
            }

            float left = float.PositiveInfinity;
            float top = float.PositiveInfinity;
            float right = float.NegativeInfinity;
            float bottom = float.NegativeInfinity;

            bool hasAny = false;
            for (int i = 0; i < strokes.Count; i++)
            {
                Stroke stroke = strokes[i];
                if (stroke.Points.Count == 0)
                {
                    continue;
                }

                if (!stroke.HasBounds)
                {
                    stroke.RecalculateBoundsFromPoints();
                }

                if (!stroke.HasBounds)
                {
                    continue;
                }

                Vector2 minScreen = Vector2.Transform(stroke.BoundsMin, worldToScreen);
                Vector2 maxScreen = Vector2.Transform(stroke.BoundsMax, worldToScreen);

                float l = Math.Min(minScreen.X, maxScreen.X);
                float t = Math.Min(minScreen.Y, maxScreen.Y);
                float r = Math.Max(minScreen.X, maxScreen.X);
                float b = Math.Max(minScreen.Y, maxScreen.Y);

                left = Math.Min(left, l);
                top = Math.Min(top, t);
                right = Math.Max(right, r);
                bottom = Math.Max(bottom, b);
                hasAny = true;
            }

            if (!hasAny)
            {
                return false;
            }

            boundsScreenDip = Rect.FromLTRB(left, top, right, bottom);
            return true;
        }
    }
}

