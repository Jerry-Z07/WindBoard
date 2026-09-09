using WindBoard.Board;

namespace WindBoard.Board.Commands
{
    internal sealed class AddStrokeCommand(Stroke stroke) : IBoardCommand
    {
        private readonly Stroke _stroke = stroke;
        private int? _index;

        public void Do(BoardDocument document)
        {
            _index ??= document.InkItems.Count;
            document.InkItems.Insert(_index.Value, _stroke);
        }

        public void Undo(BoardDocument document)
        {
            if (_index is int index && index >= 0 && index < document.InkItems.Count && ReferenceEquals(document.InkItems[index], _stroke))
            {
                document.InkItems.RemoveAt(index);
                return;
            }

            document.InkItems.Remove(_stroke);
        }
    }
}

