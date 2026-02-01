using System;
using System.Collections.Generic;
using WindBoard.Board;

namespace WindBoard.Board.Commands
{
    /// <summary>
    /// 用“点列快照”更新某条笔迹。
    ///
    /// 适用场景：
    /// - 选择工具对笔迹做平移/缩放/旋转；
    /// - 未来导入/编辑对笔迹做几何变换（保持撤销/重做一致）。
    /// </summary>
    internal sealed class UpdateStrokePointsCommand : IBoardCommand
    {
        private readonly Stroke _stroke;
        private readonly List<StrokePoint> _before;
        private readonly List<StrokePoint> _after;

        public UpdateStrokePointsCommand(Stroke stroke, List<StrokePoint> before, List<StrokePoint> after)
        {
            _stroke = stroke ?? throw new ArgumentNullException(nameof(stroke));
            _before = before is null ? throw new ArgumentNullException(nameof(before)) : new List<StrokePoint>(before);
            _after = after is null ? throw new ArgumentNullException(nameof(after)) : new List<StrokePoint>(after);
        }

        public void Do(BoardDocument document)
        {
            Apply(_after);
        }

        public void Undo(BoardDocument document)
        {
            Apply(_before);
        }

        private void Apply(List<StrokePoint> points)
        {
            _stroke.Points.Clear();
            _stroke.Points.AddRange(points);
            _stroke.RecalculateBoundsFromPoints();
        }
    }
}

