using System;
using System.Text.Json;

namespace WindBoard.Board.Persistence.Wbix
{
    /// <summary>
    /// WBIX（WindBoard Interchange）工作区序列化实现：Zip 内含 manifest.json、pages/page-NNN.json 与 assets/（资源目录，可为空，导出会尝试生成 assets/cover.png 封面）。
    /// v1/v2 的 strokes 条目为扁平形态（无 Kind）；v3 为 { kind, stroke|shape } 包装形态（InkItemSnapshot，kind 含 stroke/line/rect/ellipse/arrow），读取兼容 v1/v2，形状是 v3 内的 kind 扩展，版本不升 4。
    /// </summary>
    internal sealed partial class WbixWorkspaceSerializer : IBoardWorkspaceSerializer
    {
        internal const string FormatName = "wbix";
        internal const int CurrentVersion = 3;

        private const string ManifestEntryName = "manifest.json";
        private const string PagesFolder = "pages";
        private const string AssetsFolder = "assets";

        // 导入属于外部输入：限制单资源大小，避免压缩包内超大条目导致 OOM。
        private const long MaxResourceBytes = 32L * 1024 * 1024;
        private const long MaxTotalExtractedBytes = 256L * 1024 * 1024;

        /// <summary>
        /// 写侧与 manifest 读取共用的 JSON 选项。
        /// manifest 不含笔迹条目，读模式取 v3（包装）即可。
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions(InkItemReadMode.Wrapped, onUnexpectedLegacyFlatItem: null);

        /// <summary>
        /// 创建 JSON 选项：字段名/缩进/换行/容错策略固定，笔迹条目的读模式由版本契约注入。
        /// </summary>
        /// <param name="readMode">笔迹条目读模式（随文件版本而定）。</param>
        /// <param name="onUnexpectedLegacyFlatItem">该模式下遇到扁平条目的回调（可为 null）。</param>
        private static JsonSerializerOptions CreateJsonOptions(InkItemReadMode readMode, Action? onUnexpectedLegacyFlatItem)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                // 换行是格式契约（golden 语料锁定 CRLF），显式指定而不用 Environment.NewLine，否则字节形态随运行平台或 .NET 默认值变化。
                // 网页端（PWA）写出的 JSON 同样必须使用 CRLF，不能依赖在 Windows 上碰巧正确。
                NewLine = "\r\n",
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                PropertyNameCaseInsensitive = true,
                // System.Numerics.Vector2/Vector4 是 public field（非 property），需要显式开启字段序列化。
                IncludeFields = true,
            };

            // 显式注册的转换器优先于类型上的 [JsonConverter] 特性，从而按版本注入读模式。
            options.Converters.Add(new InkItemSnapshotJsonConverter(readMode, onUnexpectedLegacyFlatItem));
            return options;
        }
    }
}

