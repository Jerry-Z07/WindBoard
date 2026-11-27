using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Board.Persistence;
using WindBoard.Features.Export.Models;

namespace WindBoard.Features.Export.Services
{
    /// <summary>
    /// 导出服务接口（PNG / PDF / WBIX），随导出能力放在 Exporting 模块内。
    /// 输入使用 <see cref="BoardWorkspaceSnapshot"/>，避免导出过程中 UI 继续编辑造成数据竞争。
    /// </summary>
    internal interface IBoardExportService
    {
        Task ExportPngAsync(BoardWorkspaceSnapshot snapshot, int pageIndex, string filePath, BoardRasterExportOptions options, CancellationToken cancellationToken = default);

        Task ExportPngPagesToFolderAsync(BoardWorkspaceSnapshot snapshot, IReadOnlyList<int> pageIndices, string folderPath, string datePrefix, BoardRasterExportOptions options, CancellationToken cancellationToken = default);

        Task ExportPdfAsync(BoardWorkspaceSnapshot snapshot, IReadOnlyList<int> pageIndices, string filePath, BoardPdfExportOptions options, CancellationToken cancellationToken = default);

        Task ExportWbixAsync(BoardWorkspaceSnapshot snapshot, string filePath, CancellationToken cancellationToken = default);
    }
}
