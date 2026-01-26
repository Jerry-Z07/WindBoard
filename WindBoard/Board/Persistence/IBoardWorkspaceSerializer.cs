using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WindBoard.Board.Persistence
{
    /// <summary>
    /// 多页面导入/导出接口（预留）。
    /// 
    /// 设计目标：
    /// - UI 与文件格式解耦：UI 只关心“工作区快照”，不关心具体序列化实现
    /// - 便于未来支持多种格式（例如：自研二进制、JSON、SVG/图片组合、云同步等）
    /// </summary>
    internal interface IBoardWorkspaceSerializer
    {
        Task SaveAsync(BoardWorkspaceSnapshot snapshot, Stream output, CancellationToken cancellationToken = default);

        Task<BoardWorkspaceSnapshot> LoadAsync(Stream input, CancellationToken cancellationToken = default);
    }
}

