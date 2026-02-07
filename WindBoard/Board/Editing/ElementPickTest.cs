using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board.Elements;
using Vortice.Mathematics;

namespace WindBoard.Board.Editing
{
    /// <summary>
    /// 元素“点选”命中测试（纯计算逻辑）。
    /// </summary>
    internal static class ElementPickTest
    {
        /// <summary>
        /// 在给定世界坐标位置命中“最上层”元素（按列表顺序：越靠后越靠上）。
        /// </summary>
        internal static BoardElement? HitTestTopMostElement(IReadOnlyList<BoardElement> elements, Vector2 pointWorld, float toleranceWorld)
        {
            if (elements is null || elements.Count == 0)
            {
                return null;
            }

            for (int i = elements.Count - 1; i >= 0; i--)
            {
                BoardElement e = elements[i];
                Rect bounds = e.GetBoundsWorld();

                float left = bounds.Left - toleranceWorld;
                float top = bounds.Top - toleranceWorld;
                float right = bounds.Right + toleranceWorld;
                float bottom = bounds.Bottom + toleranceWorld;

                if (pointWorld.X >= left && pointWorld.X <= right && pointWorld.Y >= top && pointWorld.Y <= bottom)
                {
                    return e;
                }
            }

            return null;
        }
    }
}

