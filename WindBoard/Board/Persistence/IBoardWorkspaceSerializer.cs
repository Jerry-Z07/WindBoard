using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WindBoard.Board.Persistence
{
    /// <summary>
    /// 多页面导入/导出接口（预留）：UI 只面对工作区快照，与具体序列化实现解耦。
    /// 便于后续接入其它格式（自研二进制、JSON、SVG/图片组合、云同步等）。
    /// </summary>
    internal interface IBoardWorkspaceSerializer
    {
        Task SaveAsync(BoardWorkspaceSnapshot snapshot, Stream output, CancellationToken cancellationToken = default);

        Task<BoardWorkspaceSnapshot> LoadAsync(Stream input, CancellationToken cancellationToken = default);
    }
}

