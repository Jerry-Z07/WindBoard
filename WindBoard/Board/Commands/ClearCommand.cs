using System.Collections.Generic;
using WindBoard.Board;
using WindBoard.Board.Items;

namespace WindBoard.Board.Commands
{
    /// <summary>
    /// 清空笔迹层条目集合（可撤销，Undo 时按快照原样恢复）。
    /// </summary>
    /// <remarks>
    /// 快照按 <see cref="IBoardInkItem"/> 记录：命令栈不区分条目类型，折线与未来的形状条目同栈撤销。
    /// </remarks>
    internal sealed class ClearCommand(List<IBoardInkItem> snapshot) : IBoardCommand
    {
        private readonly List<IBoardInkItem> _snapshot = snapshot;

        public void Do(BoardDocument document)
        {
            document.InkItems.Clear();
        }

        public void Undo(BoardDocument document)
        {
            document.InkItems.Clear();
            document.InkItems.AddRange(_snapshot);
        }
    }
}

