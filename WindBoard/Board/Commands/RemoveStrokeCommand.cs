using System;
using WindBoard.Board;

namespace WindBoard.Board.Commands
{
    /// <summary>
    /// 删除指定笔迹（可撤销）。
    /// </summary>
    internal sealed class RemoveStrokeCommand(Stroke stroke) : IBoardCommand
    {
        private readonly Stroke _stroke = stroke ?? throw new ArgumentNullException(nameof(stroke));
        private int? _index;

        public void Do(BoardDocument document)
        {
            if (_index is null)
            {
                int idx = document.Strokes.IndexOf(_stroke);
                if (idx < 0)
                {
                    return;
                }

                _index = idx;
                document.Strokes.RemoveAt(idx);
                return;
            }

            int recorded = _index.Value;
            if (recorded >= 0 && recorded < document.Strokes.Count && ReferenceEquals(document.Strokes[recorded], _stroke))
            {
                document.Strokes.RemoveAt(recorded);
                return;
            }

            document.Strokes.Remove(_stroke);
        }

        public void Undo(BoardDocument document)
        {
            if (document.Strokes.Contains(_stroke))
            {
                return;
            }

            if (_index is not int index)
            {
                return;
            }

            int insertIndex = Math.Clamp(index, 0, document.Strokes.Count);
            document.Strokes.Insert(insertIndex, _stroke);
        }
    }
}

