using System.Text.Json;

namespace WindBoard.Board.Persistence.Wbix
{
    /// <summary>
    /// WBIX（WindBoard Interchange）工作区序列化实现。
    ///
    /// 文件结构（Zip）：
    /// - manifest.json
    /// - pages/page-000.json
    /// - pages/page-001.json
    /// - assets/（资源目录，可为空；v2 导出会尝试生成 assets/cover.png 封面图）
    /// </summary>
    internal sealed partial class WbixWorkspaceSerializer : IBoardWorkspaceSerializer
    {
        internal const string FormatName = "wbix";
        internal const int CurrentVersion = 2;

        private const string ManifestEntryName = "manifest.json";
        private const string PagesFolder = "pages";
        private const string AssetsFolder = "assets";

        // 导入属于外部输入：限制单资源大小，避免压缩包内超大条目导致 OOM。
        private const long MaxResourceBytes = 32L * 1024 * 1024;
        private const long MaxTotalExtractedBytes = 256L * 1024 * 1024;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true,
            // System.Numerics.Vector2/Vector4 是 public field（非 property），需要显式开启字段序列化。
            IncludeFields = true,
        };
    }
}

