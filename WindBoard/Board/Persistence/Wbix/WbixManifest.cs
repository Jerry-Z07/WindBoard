using System;
using System.Collections.Generic;
using System.Numerics;

namespace WindBoard.Board.Persistence.Wbix
{
    /// <summary>
    /// WBIX 清单文件（manifest.json）：描述当前压缩包的结构与版本。
    /// 预留资源（图片/视频等）扩展位，避免后续升级破坏旧文件。
    /// </summary>
    internal sealed record WbixManifest(
        string Format,
        int Version,
        DateTimeOffset CreatedUtc,
        int CurrentIndex,
        IReadOnlyList<WbixManifestPage> Pages,
        IReadOnlyList<WbixResourceEntry>? Resources,
        Vector2? ViewportCameraWorld = null,
        float? ViewportZoom = null,
        Vector2? ViewportSizeDip = null);

    internal sealed record WbixManifestPage(Guid Id, int Index, string Path);

    /// <summary>
    /// 资源条目（预留），v1/v2 中 Resources 允许为空数组。
    /// 字段含义：Type 为 image/video/audio/file 等，Path 为 Zip 内路径（assets/...），ContentType 为 MIME（如 image/png），Meta 为自定义元数据（尺寸、时长、校验和等）。
    /// </summary>
    internal sealed record WbixResourceEntry(
        string Id,
        string Type,
        string Path,
        string ContentType,
        IReadOnlyDictionary<string, string>? Meta);
}
