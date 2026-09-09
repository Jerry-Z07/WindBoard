using System.Collections.Generic;
using WindBoard.Board;
using WindBoard.Board.Items;

namespace WindBoard.Board.Commands
{
    /// <summary>
    /// 用“快照列表”替换当前笔迹层条目列表（用于整笔擦除、未来的局部擦除/分段等）。
    /// </summary>
    /// <remarks>
    /// 快照按 <see cref="IBoardInkItem"/> 记录：像素擦除只替换折线段，整笔删除可替换任意条目类型，
    /// 撤销/重做均按对象引用恢复，与条目类型无关。
    /// </remarks>
    internal sealed class ReplaceStrokesCommand : IBoardCommand
    {
        private readonly List<IBoardInkItem> _before;
        private readonly List<IBoardInkItem> _after;

        public ReplaceStrokesCommand(List<IBoardInkItem> before, List<IBoardInkItem> after)
        {
            // 这里拷贝一份，避免调用方后续修改传入的 List 导致撤销/重做异常。
            _before = new List<IBoardInkItem>(before);
            _after = new List<IBoardInkItem>(after);
        }

        public void Do(BoardDocument document)
        {
            Replace(document, _after);
        }

        public void Undo(BoardDocument document)
        {
            Replace(document, _before);
        }

        private static void Replace(BoardDocument document, List<IBoardInkItem> items)
        {
            document.InkItems.Clear();
            document.InkItems.AddRange(items);
        }
    }
}

