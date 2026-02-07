using System;
using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board.Elements;
using Vortice.Mathematics;

namespace WindBoard.Board.Editing
{
    /// <summary>
    /// 元素“框选”命中测试（纯计算逻辑）。
    /// </summary>
    internal static class ElementRectSelectTest
    {
        /// <summary>
        /// 在给定世界坐标矩形范围内命中“最上层”元素（按列表顺序：越靠后越靠上）。
        /// </summary>
        internal static BoardElement? HitTestTopMostElementInWorldRect(IReadOnlyList<BoardElement> elements, Vector2 minWorld, Vector2 maxWorld)
        {
            if (elements is null || elements.Count == 0)
            {
                return null;
            }

            float left = Math.Min(minWorld.X, maxWorld.X);
            float top = Math.Min(minWorld.Y, maxWorld.Y);
            float right = Math.Max(minWorld.X, maxWorld.X);
            float bottom = Math.Max(minWorld.Y, maxWorld.Y);

            Rect rect = Rect.FromLTRB(left, top, right, bottom);

            for (int i = elements.Count - 1; i >= 0; i--)
            {
                BoardElement e = elements[i];
                Rect bounds = e.GetBoundsWorld();

                // AABB 相交判断
                if (bounds.Right < rect.Left || bounds.Left > rect.Right || bounds.Bottom < rect.Top || bounds.Top > rect.Bottom)
                {
                    continue;
                }

                return e;
            }

            return null;
        }
    }
}

