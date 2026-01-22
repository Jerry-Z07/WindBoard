using System.Threading;
using System.Threading.Tasks;
using WindBoard.Board;

namespace WindBoard.Persistence
{
    internal interface IBoardPersistenceService
    {
        Task SaveAsync(BoardDocument document, string filePath, CancellationToken cancellationToken = default);

        Task<BoardDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default);
    }

    internal interface IBoardExportService
    {
        Task ExportPngAsync(BoardDocument document, string filePath, CancellationToken cancellationToken = default);
    }
}

