using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WindBoard.Board.Persistence;

namespace WindBoard.Exporting
{
    /// <summary>
    /// 导出服务接口（PNG / PDF / WBIX）。
    /// 
    /// 说明：
    /// - 该接口属于导出能力，因此放在 Exporting 模块内；
    /// - 输入建议使用 <see cref="BoardWorkspaceSnapshot"/>，避免导出过程中 UI 继续编辑导致数据竞争。
    /// </summary>
    internal interface IBoardExportService
    {
        Task ExportPngAsync(BoardWorkspaceSnapshot snapshot, int pageIndex, string filePath, BoardRasterExportOptions options, CancellationToken cancellationToken = default);

        Task ExportPngPagesToFolderAsync(BoardWorkspaceSnapshot snapshot, IReadOnlyList<int> pageIndices, string folderPath, string datePrefix, BoardRasterExportOptions options, CancellationToken cancellationToken = default);

        Task ExportPdfAsync(BoardWorkspaceSnapshot snapshot, IReadOnlyList<int> pageIndices, string filePath, BoardPdfExportOptions options, CancellationToken cancellationToken = default);

        Task ExportWbixAsync(BoardWorkspaceSnapshot snapshot, string filePath, CancellationToken cancellationToken = default);
    }
}

