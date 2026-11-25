using System;
using System.Numerics;
using Vortice.Mathematics;

namespace WindBoard.Board.Items
{
    /// <summary>
    /// 绘制条目（笔迹层对象）的统一契约：折线笔迹（<see cref="WindBoard.Board.Stroke"/>）与几何形状共用一个笔迹层集合（<see cref="WindBoard.Board.BoardDocument.InkItems"/>），按创建顺序交错叠放。
    /// 只承载选择与变换所需的最小契约（标识、包围盒、平移），类型专属能力由具体实现提供，渲染/命中/擦除按类型单点分发。
    /// </summary>
    internal interface IBoardInkItem
    {
        /// <summary>
        /// 条目唯一标识（创建时生成，不参与持久化）。
        /// </summary>
        Guid Id { get; }

        /// <summary>
        /// 世界坐标包围盒（AABB）。
        /// </summary>
        /// <remarks>
        /// 条目尚无有效包围盒时返回空矩形（宽高均为 0）。
        /// </remarks>
        Rect BoundsWorld { get; }

        /// <summary>
        /// 按世界坐标增量平移条目。
        /// </summary>
        void Translate(Vector2 delta);
    }
}
