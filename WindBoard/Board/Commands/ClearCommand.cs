using System.Collections.Generic;
using WindBoard.Board;

namespace WindBoard.Board.Commands
{
    internal sealed class ClearCommand(List<Stroke> snapshot) : IBoardCommand
    {
        private readonly List<Stroke> _snapshot = snapshot;

        public void Do(BoardDocument document)
        {
            document.Strokes.Clear();
        }

        public void Undo(BoardDocument document)
        {
            document.Strokes.Clear();
            document.Strokes.AddRange(_snapshot);
        }
    }
}

