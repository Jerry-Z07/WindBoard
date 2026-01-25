using System.Collections.Generic;
using WindBoard.Board;

namespace WindBoard.Board.Commands
{
    /// <summary>
    /// 用“快照列表”替换当前笔迹列表（用于整笔擦除、未来的局部擦除/分段等）。
    /// </summary>
    internal sealed class ReplaceStrokesCommand : IBoardCommand
    {
        private readonly List<Stroke> _before;
        private readonly List<Stroke> _after;

        public ReplaceStrokesCommand(List<Stroke> before, List<Stroke> after)
        {
            // 这里拷贝一份，避免调用方后续修改传入的 List 导致撤销/重做异常。
            _before = new List<Stroke>(before);
            _after = new List<Stroke>(after);
        }

        public void Do(BoardDocument document)
        {
            Replace(document, _after);
        }

        public void Undo(BoardDocument document)
        {
            Replace(document, _before);
        }

        private static void Replace(BoardDocument document, List<Stroke> strokes)
        {
            document.Strokes.Clear();
            document.Strokes.AddRange(strokes);
        }
    }
}

