using System;
using System.Collections.Generic;

namespace WindBoard.Board.Persistence.Wbix
{
    /// <summary>
    /// WBIX 清单文件（manifest.json）。
    /// 
    /// 设计目标：
    /// - 清晰描述当前压缩包的结构与版本；
    /// - 预留资源（图片/视频等）扩展位，避免未来升级破坏旧文件。
    /// </summary>
    internal sealed record WbixManifest(
        string Format,
        int Version,
        DateTimeOffset CreatedUtc,
        int CurrentIndex,
        IReadOnlyList<WbixManifestPage> Pages,
        IReadOnlyList<WbixResourceEntry> Resources);

    internal sealed record WbixManifestPage(Guid Id, int Index, string Path);

    /// <summary>
    /// 资源条目（预留）。
    /// 
    /// v1/v2 版本中 Resources 允许为空数组；后续可逐步补齐：
    /// - Type：image / video / audio / file ...
    /// - Path：assets/...（Zip 内路径）
    /// - ContentType：MIME，例如 image/png
    /// - Meta：自定义元数据（尺寸、时长、校验和等）
    /// </summary>
    internal sealed record WbixResourceEntry(
        string Id,
        string Type,
        string Path,
        string ContentType,
        IReadOnlyDictionary<string, string>? Meta);
}
