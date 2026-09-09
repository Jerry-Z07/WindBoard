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
            int count = document.InkItems.Count;
            if (count <= 1)
            {
                return;
            }

            if (_fromIndex is null)
            {
                int idx = document.InkItems.IndexOf(_stroke);
                if (idx < 0 || idx == count - 1)
                {
                    return;
                }

                _fromIndex = idx;
                document.InkItems.RemoveAt(idx);
                document.InkItems.Add(_stroke);
                return;
            }

            int recorded = _fromIndex.Value;
            if (recorded >= 0 && recorded < document.InkItems.Count && ReferenceEquals(document.InkItems[recorded], _stroke))
            {
                document.InkItems.RemoveAt(recorded);
            }
            else
            {
                document.InkItems.Remove(_stroke);
            }

            document.InkItems.Add(_stroke);
        }

        public void Undo(BoardDocument document)
        {
            if (_fromIndex is not int fromIndex)
            {
                return;
            }

            int idx = document.InkItems.IndexOf(_stroke);
            if (idx < 0)
            {
                return;
            }

            document.InkItems.RemoveAt(idx);
            int insertIndex = Math.Clamp(fromIndex, 0, document.InkItems.Count);
            document.InkItems.Insert(insertIndex, _stroke);
        }
    }
}

