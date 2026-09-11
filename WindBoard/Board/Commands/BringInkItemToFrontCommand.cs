using System;
using WindBoard.Board;
using WindBoard.Board.Items;

namespace WindBoard.Board.Commands
{
    /// <summary>
    /// 将指定绘制条目置顶（移动到列表末尾，视觉上最后绘制；可撤销）。
    /// </summary>
    /// <remarks>
    /// 由原 <c>BringStrokeToFrontCommand</c> 泛化而来（design A）：选择 Dock 置顶
    /// 需同时覆盖折线笔迹与形状，实现与类型无关。
    /// </remarks>
    internal sealed class BringInkItemToFrontCommand(IBoardInkItem item) : IBoardCommand
    {
        private readonly IBoardInkItem _item = item ?? throw new ArgumentNullException(nameof(item));
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
                int idx = document.InkItems.IndexOf(_item);
                if (idx < 0 || idx == count - 1)
                {
                    return;
                }

                _fromIndex = idx;
                document.InkItems.RemoveAt(idx);
                document.InkItems.Add(_item);
                return;
            }

            int recorded = _fromIndex.Value;
            if (recorded >= 0 && recorded < document.InkItems.Count && ReferenceEquals(document.InkItems[recorded], _item))
            {
                document.InkItems.RemoveAt(recorded);
            }
            else
            {
                document.InkItems.Remove(_item);
            }

            document.InkItems.Add(_item);
        }

        public void Undo(BoardDocument document)
        {
            if (_fromIndex is not int fromIndex)
            {
                return;
            }

            int idx = document.InkItems.IndexOf(_item);
            if (idx < 0)
            {
                return;
            }

            document.InkItems.RemoveAt(idx);
            int insertIndex = Math.Clamp(fromIndex, 0, document.InkItems.Count);
            document.InkItems.Insert(insertIndex, _item);
        }
    }
}
