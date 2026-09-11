using System;
using WindBoard.Board;
using WindBoard.Board.Items;

namespace WindBoard.Board.Commands
{
    /// <summary>
    /// 删除指定绘制条目（可撤销）。
    /// </summary>
    /// <remarks>
    /// 由原 <c>RemoveStrokeCommand</c> 泛化而来（design A）：Delete 键 / 选择 Dock 删除
    /// 需同时覆盖折线笔迹与形状，实现与类型无关。
    /// </remarks>
    internal sealed class RemoveInkItemCommand(IBoardInkItem item) : IBoardCommand
    {
        private readonly IBoardInkItem _item = item ?? throw new ArgumentNullException(nameof(item));
        private int? _index;

        public void Do(BoardDocument document)
        {
            if (_index is null)
            {
                int idx = document.InkItems.IndexOf(_item);
                if (idx < 0)
                {
                    return;
                }

                _index = idx;
                document.InkItems.RemoveAt(idx);
                return;
            }

            int recorded = _index.Value;
            if (recorded >= 0 && recorded < document.InkItems.Count && ReferenceEquals(document.InkItems[recorded], _item))
            {
                document.InkItems.RemoveAt(recorded);
                return;
            }

            document.InkItems.Remove(_item);
        }

        public void Undo(BoardDocument document)
        {
            if (document.InkItems.Contains(_item))
            {
                return;
            }

            if (_index is not int index)
            {
                return;
            }

            int insertIndex = Math.Clamp(index, 0, document.InkItems.Count);
            document.InkItems.Insert(insertIndex, _item);
        }
    }
}
