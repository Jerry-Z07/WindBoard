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
                int idx = document.InkItems.IndexOf(_stroke);
                if (idx < 0)
                {
                    return;
                }

                _index = idx;
                document.InkItems.RemoveAt(idx);
                return;
            }

            int recorded = _index.Value;
            if (recorded >= 0 && recorded < document.InkItems.Count && ReferenceEquals(document.InkItems[recorded], _stroke))
            {
                document.InkItems.RemoveAt(recorded);
                return;
            }

            document.InkItems.Remove(_stroke);
        }

        public void Undo(BoardDocument document)
        {
            if (document.InkItems.Contains(_stroke))
            {
                return;
            }

            if (_index is not int index)
            {
                return;
            }

            int insertIndex = Math.Clamp(index, 0, document.InkItems.Count);
            document.InkItems.Insert(insertIndex, _stroke);
        }
    }
}

