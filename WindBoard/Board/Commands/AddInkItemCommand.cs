using System;
using WindBoard.Board;
using WindBoard.Board.Items;

namespace WindBoard.Board.Commands
{
    /// <summary>
    /// 向笔迹层添加一个绘制条目（可撤销）。
    /// </summary>
    /// <remarks>
    /// 由原 <c>AddStrokeCommand</c> 泛化而来（design A）：实现本身类型无关（Insert/Remove），
    /// 折线笔迹（<see cref="WindBoard.Board.Stroke"/>）与形状（<see cref="BoardShape"/>）共用同一命令。
    /// </remarks>
    internal sealed class AddInkItemCommand(IBoardInkItem item) : IBoardCommand
    {
        private readonly IBoardInkItem _item = item ?? throw new ArgumentNullException(nameof(item));
        private int? _index;

        public void Do(BoardDocument document)
        {
            _index ??= document.InkItems.Count;
            document.InkItems.Insert(_index.Value, _item);
        }

        public void Undo(BoardDocument document)
        {
            if (_index is int index && index >= 0 && index < document.InkItems.Count && ReferenceEquals(document.InkItems[index], _item))
            {
                document.InkItems.RemoveAt(index);
                return;
            }

            document.InkItems.Remove(_item);
        }
    }
}
