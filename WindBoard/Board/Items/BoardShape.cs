using System;
using System.Numerics;
using Vortice.Mathematics;

namespace WindBoard.Board.Items
{
    /// <summary>
    /// 两点式几何形状的种类（阶段二 R5：直线/矩形/椭圆/箭头）。
    /// </summary>
    /// <remarks>
    /// 序列化与 Codec 的字符串映射集中在 <see cref="WindBoard.Board.Persistence.BoardInkItemCodec"/>，
    /// 枚举本身不承载文件格式语义。
    /// </remarks>
    internal enum BoardShapeKind
    {
        /// <summary>直线段（Start → End）。</summary>
        Line,

        /// <summary>轴对齐矩形（Start/End 为对角点；渲染/属性面板按 Min/Max 规范化读取）。</summary>
        Rectangle,

        /// <summary>轴对齐椭圆（Start/End 为外接矩形对角点；宽高相等即圆）。</summary>
        Ellipse,

        /// <summary>箭头（直线 + End 端点箭头头部，头部尺寸 = 常量系数 × Width）。</summary>
        Arrow,
    }

    /// <summary>
    /// 两点式几何形状（阶段二形状域模型，design A）。
    /// </summary>
    /// <remarks>
    /// 设计决策：本期形状全部为两点式（状态完全同构：Start/End/Color/Width），差异只在
    /// "渲染与命中的几何解释"，因此用单个类 + <see cref="BoardShapeKind"/> 枚举承载，不拆子类
    /// （避免命令/选择快照/序列化/属性面板全部引入类型分支）。
    /// - 颜色/线宽取创建时的画笔参数（R6）；不支持压感；
    /// - <see cref="BoundsWorld"/> = AABB(Start, End) 外扩半宽（与 Stroke 的半宽语义一致），
    ///   任何几何/线宽变更路径都会重算，保证包围盒始终一致；
    /// - 矩形/椭圆域内不强制归一化存储（保留 Start/End 原始方向，读侧按 Min/Max 规范化）。
    /// </remarks>
    internal sealed class BoardShape : IBoardInkItem
    {
        private float _width = 3.0f;

        public BoardShape(BoardShapeKind kind)
        {
            Kind = kind;
        }

        /// <summary>形状唯一标识（创建时生成，仅用于内存定位，不参与持久化）。</summary>
        public Guid Id { get; } = Guid.NewGuid();

        /// <summary>形状种类（创建后不变）。</summary>
        public BoardShapeKind Kind { get; }

        /// <summary>起点（世界坐标）。</summary>
        public Vector2 Start { get; private set; }

        /// <summary>终点（世界坐标）。</summary>
        public Vector2 End { get; private set; }

        /// <summary>描边颜色（取当前画笔颜色，R6）。</summary>
        public Color4 Color { get; set; } = new(0, 0, 0, 1);

        /// <summary>线宽（世界坐标下的描边直径，取当前画笔粗细，R6；不支持压感）。</summary>
        /// <remarks>线宽影响包围盒外扩量，写入时同步重算包围盒。</remarks>
        public float Width
        {
            get => _width;
            set
            {
                _width = value;
                RecalculateBounds();
            }
        }

        /// <summary>世界坐标包围盒（<see cref="IBoardInkItem"/> 契约实现）。</summary>
        public Rect BoundsWorld { get; private set; }

        /// <summary>按世界坐标增量平移形状（同步平移两端点并重算包围盒）。</summary>
        public void Translate(Vector2 deltaWorld)
        {
            if (deltaWorld.LengthSquared() <= 0.0000001f)
            {
                return;
            }

            SetGeometry(Start + deltaWorld, End + deltaWorld);
        }

        /// <summary>
        /// 重设两点几何并重算包围盒。
        /// </summary>
        /// <remarks>
        /// 统一入口：工具预览推进（ShapeTool）、命令 Apply（UpdateShapeGeometryCommand）、
        /// 快照重建（BoardInkItemCodec.BuildShape）均经此更新，保证 Bounds 单点重算。
        /// </remarks>
        internal void SetGeometry(Vector2 start, Vector2 end)
        {
            Start = start;
            End = end;
            RecalculateBounds();
        }

        private void RecalculateBounds()
        {
            // 与 Stroke 的半宽语义一致：包围盒按线宽一半外扩（保留 0.25 最小半径，避免零宽线命中困难）。
            float halfWidth = Math.Max(0.25f, Width / 2.0f);
            float left = Math.Min(Start.X, End.X) - halfWidth;
            float top = Math.Min(Start.Y, End.Y) - halfWidth;
            float right = Math.Max(Start.X, End.X) + halfWidth;
            float bottom = Math.Max(Start.Y, End.Y) + halfWidth;
            BoundsWorld = Rect.FromLTRB(left, top, right, bottom);
        }
    }
}
