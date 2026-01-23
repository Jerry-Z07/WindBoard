using WindBoard.Board;

namespace WindBoard.Board.Commands
{
    internal sealed class AddStrokeCommand(Stroke stroke) : IBoardCommand
    {
        private readonly Stroke _stroke = stroke;
        private int? _index;

        public void Do(BoardDocument document)
        {
            _index ??= document.Strokes.Count;
            document.Strokes.Insert(_index.Value, _stroke);
        }

        public void Undo(BoardDocument document)
        {
            if (_index is int index && index >= 0 && index < document.Strokes.Count && ReferenceEquals(document.Strokes[index], _stroke))
            {
                document.Strokes.RemoveAt(index);
                return;
            }

            document.Strokes.Remove(_stroke);
        }
    }
}

