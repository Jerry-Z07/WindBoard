using System;
using WindBoard.Board;

namespace WindBoard.Board.Commands
{
    /// <summary>
    /// 将指定笔迹置顶（移动到列表末尾，视觉上最后绘制）。
    /// </summary>
    internal sealed class BringStrokeToFrontCommand(Stroke stroke) : IBoardCommand
    {
        private readonly Stroke _stroke = stroke ?? throw new ArgumentNullException(nameof(stroke));
        private int? _fromIndex;

        public void Do(BoardDocument document)
        {
            int count = document.Strokes.Count;
            if (count <= 1)
            {
                return;
            }

            if (_fromIndex is null)
            {
                int idx = document.Strokes.IndexOf(_stroke);
                if (idx < 0 || idx == count - 1)
                {
                    return;
                }

                _fromIndex = idx;
                document.Strokes.RemoveAt(idx);
                document.Strokes.Add(_stroke);
                return;
            }

            int recorded = _fromIndex.Value;
            if (recorded >= 0 && recorded < document.Strokes.Count && ReferenceEquals(document.Strokes[recorded], _stroke))
            {
                document.Strokes.RemoveAt(recorded);
            }
            else
            {
                document.Strokes.Remove(_stroke);
            }

            document.Strokes.Add(_stroke);
        }

        public void Undo(BoardDocument document)
        {
            if (_fromIndex is not int fromIndex)
            {
                return;
            }

            int idx = document.Strokes.IndexOf(_stroke);
            if (idx < 0)
            {
                return;
            }

            document.Strokes.RemoveAt(idx);
            int insertIndex = Math.Clamp(fromIndex, 0, document.Strokes.Count);
            document.Strokes.Insert(insertIndex, _stroke);
        }
    }
}

