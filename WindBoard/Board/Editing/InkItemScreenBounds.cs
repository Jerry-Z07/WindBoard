using System;
using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Items;
using Vortice.Mathematics;

namespace WindBoard.Board.Editing
{
    /// <summary>
    /// 绘制条目 Bounds 变换辅助：将若干条目的世界坐标 Bounds 变换到目标坐标系，并求并集矩形。
    /// </summary>
    internal static class InkItemScreenBounds
    {
        /// <summary>
        /// 计算条目集合在屏幕坐标（DIP）下的包围盒（按条目类型分发取世界包围盒）。
        /// </summary>
        /// <remarks>
        /// 注意：
        /// - 这里基于条目的世界 Bounds 计算，为了性能不遍历所有点；
        /// - 折线笔迹某些情况下可能还未计算 Bounds（例如外部构造/导入），会在这里兜底重建；
        /// - 无有效包围盒的条目（空矩形）跳过。
        /// </remarks>
        internal static bool TryGetInkItemsBoundsScreenDip(
            IReadOnlyList<IBoardInkItem> items,
            Matrix3x2 worldToScreen,
            out Rect boundsScreenDip)
        {
            boundsScreenDip = default;

            if (items is null || items.Count == 0)
            {
                return false;
            }

            float left = float.PositiveInfinity;
            float top = float.PositiveInfinity;
            float right = float.NegativeInfinity;
            float bottom = float.NegativeInfinity;

            bool hasAny = false;
            for (int i = 0; i < items.Count; i++)
            {
                IBoardInkItem item = items[i];

                // 按条目类型取世界包围盒：折线笔迹走 BoundsMin/BoundsMax（含兜底重建），其余走接口契约。
                if (!TryGetInkItemBoundsWorld(item, out Vector2 minWorld, out Vector2 maxWorld))
                {
                    continue;
                }

                Vector2 minScreen = Vector2.Transform(minWorld, worldToScreen);
                Vector2 maxScreen = Vector2.Transform(maxWorld, worldToScreen);

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

        /// <summary>
        /// 获取条目的世界坐标包围盒（按条目类型分发）；返回 false 表示无有效包围盒。
        /// </summary>
        private static bool TryGetInkItemBoundsWorld(IBoardInkItem item, out Vector2 minWorld, out Vector2 maxWorld)
        {
            switch (item)
            {
                case Stroke stroke:
                {
                    if (stroke.Points.Count == 0)
                    {
                        minWorld = default;
                        maxWorld = default;
                        return false;
                    }

                    if (!stroke.HasBounds)
                    {
                        stroke.RecalculateBoundsFromPoints();
                    }

                    if (!stroke.HasBounds)
                    {
                        minWorld = default;
                        maxWorld = default;
                        return false;
                    }

                    minWorld = stroke.BoundsMin;
                    maxWorld = stroke.BoundsMax;
                    return true;
                }

                default:
                {
                    // 通用路径：直接读取条目世界包围盒契约；空矩形视为无有效包围盒。
                    Rect bounds = item.BoundsWorld;
                    if (bounds.Width <= 0.0f && bounds.Height <= 0.0f)
                    {
                        minWorld = default;
                        maxWorld = default;
                        return false;
                    }

                    minWorld = new Vector2(bounds.Left, bounds.Top);
                    maxWorld = new Vector2(bounds.Right, bounds.Bottom);
                    return true;
                }
            }
        }
    }
}
