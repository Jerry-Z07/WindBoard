using System.Collections.Generic;

namespace WindBoard.Board.Persistence.Wbix
{
    /// <summary>
    /// WBIX 资源文件（写入 Zip 的二进制条目）。
    /// </summary>
    /// <remarks>
    /// 说明：
    /// - 该类型用于把资源数据与其在 manifest.json 中的描述绑定在一起；
    /// - v2 目前主要用于封面图（assets/cover.png）；
    /// - 后续可用于图片/视频/音频等资源导出，并通过 manifest.Resources 进行索引。
    /// </remarks>
    internal sealed record WbixResourceFile(
        string Id,
        string Type,
        string Path,
        string ContentType,
        IReadOnlyDictionary<string, string>? Meta,
        byte[] Bytes);
}
