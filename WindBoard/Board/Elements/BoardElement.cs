using System;
using System.Numerics;
using Vortice.Mathematics;

namespace WindBoard.Board.Elements
{
    /// <summary>
    /// 白板页面元素（非笔迹对象）的基类。
    /// </summary>
    internal abstract class BoardElement
    {
        protected BoardElement()
        {
            Id = Guid.NewGuid();
        }

        /// <summary>
        /// 元素唯一标识。
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// 元素左上角世界坐标。
        /// </summary>
        public Vector2 PositionWorld { get; set; } = Vector2.Zero;

        /// <summary>
        /// 元素尺寸（世界坐标，宽高应为正数）。
        /// </summary>
        public Vector2 SizeWorld { get; set; } = new(1.0f, 1.0f);

        /// <summary>
        /// 获取世界坐标包围盒（AABB）。
        /// </summary>
        public Rect GetBoundsWorld()
        {
            float left = PositionWorld.X;
            float top = PositionWorld.Y;
            float right = left + SizeWorld.X;
            float bottom = top + SizeWorld.Y;

            // 允许 SizeWorld 为负的异常输入，兜底归一化为有效矩形。
            float l = Math.Min(left, right);
            float t = Math.Min(top, bottom);
            float r = Math.Max(left, right);
            float b = Math.Max(top, bottom);
            return Rect.FromLTRB(l, t, r, b);
        }
    }
}

