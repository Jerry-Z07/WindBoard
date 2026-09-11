using System;
using System.Numerics;
using WindBoard.Board.Items;

namespace WindBoard.Board.Commands
{
    /// <summary>
    /// 用“两点几何快照”更新某个形状（可撤销）。
    /// </summary>
    /// <remarks>
    /// 镜像 <see cref="UpdateStrokePointsCommand"/> 的“前后快照 + Apply”结构（design A）：
    /// 适用场景为属性面板精调（R8）、选择工具拖拽平移等形状几何编辑；
    /// <see cref="UpdateStrokePointsCommand"/> 仍保持 Stroke 专用（点集 diff），不合并。
    /// </remarks>
    internal sealed class UpdateShapeGeometryCommand : IBoardCommand
    {
        private readonly BoardShape _shape;
        private readonly (Vector2 Start, Vector2 End) _before;
        private readonly (Vector2 Start, Vector2 End) _after;

        public UpdateShapeGeometryCommand(BoardShape shape, (Vector2 Start, Vector2 End) before, (Vector2 Start, Vector2 End) after)
        {
            _shape = shape ?? throw new ArgumentNullException(nameof(shape));
            _before = before;
            _after = after;
        }

        public void Do(BoardDocument document)
        {
            Apply(_after);
        }

        public void Undo(BoardDocument document)
        {
            Apply(_before);
        }

        private void Apply((Vector2 Start, Vector2 End) geometry)
        {
            // SetGeometry 内部重算 Bounds，与点列命令的 RecalculateBoundsFromPoints 对称。
            _shape.SetGeometry(geometry.Start, geometry.End);
        }
    }
}
