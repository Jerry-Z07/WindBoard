using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Board;
using WindBoard.Board.Persistence;
using WindBoard.Exporting;

namespace WindBoard.Persistence
{
    internal interface IBoardPersistenceService
    {
        Task SaveAsync(BoardDocument document, string filePath, CancellationToken cancellationToken = default);

        Task<BoardDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default);
    }

    internal interface IBoardExportService
    {
        Task ExportPngAsync(BoardWorkspaceSnapshot snapshot, int pageIndex, string filePath, BoardRasterExportOptions options, CancellationToken cancellationToken = default);

        Task ExportPngPagesToFolderAsync(BoardWorkspaceSnapshot snapshot, IReadOnlyList<int> pageIndices, string folderPath, string datePrefix, BoardRasterExportOptions options, CancellationToken cancellationToken = default);

        Task ExportPdfAsync(BoardWorkspaceSnapshot snapshot, IReadOnlyList<int> pageIndices, string filePath, BoardPdfExportOptions options, CancellationToken cancellationToken = default);

        Task ExportWbixAsync(BoardWorkspaceSnapshot snapshot, string filePath, CancellationToken cancellationToken = default);
    }
}
