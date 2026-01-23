using WindBoard.Board;

namespace WindBoard.Board.Commands
{
    internal interface IBoardCommand
    {
        void Do(BoardDocument document);

        void Undo(BoardDocument document);
    }
}

