using WindBoard.Models.InkV2;

namespace WindBoard.Services.InkV2
{
    internal interface IInkUndoableCommand
    {
        void Undo(InkDocument document);

        void Redo(InkDocument document);
    }
}

