using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using WindBoard.Board.Elements;
using WindBoard.Board.Persistence;
using WindBoard.Board.Persistence.Wbix;
using Xunit;

namespace WindBoard.Tests.Board.Persistence;

/// <summary>
/// WBIX 双端一致性测试台（P0.5）的 C# 侧：把 <c>.wbix</c> 包用生产读侧读成域模型，输出"规范化 dump"，
/// 供 <c>web/scripts/interop.mjs</c> 与网页端实现逐字段比对。
/// </summary>
/// <remarks>
/// <para>
/// 用法：本测试由 interop 脚本通过环境变量驱动；正常执行 <c>dotnet test</c> 时没有这些环境变量，
/// 测试会优雅跳过（见 <see cref="SkipOrReturn"/>），不影响既有测试基线。
/// </para>
/// <list type="bullet">
/// <item><c>WINDBOARD_WBIX_INTEROP_DIR</c>：待处理目录，只扫描该目录顶层的 <c>*.wbix</c>。</item>
/// <item><c>WINDBOARD_WBIX_INTEROP_MODE</c>：<c>read</c>（默认，只产出 dump）或 <c>rewrite</c>（额外用生产写侧重写整包）。</item>
/// </list>
/// <para>
/// 输出：<c>&lt;dir&gt;/dump-cs/&lt;包名&gt;.json</c>；rewrite 模式另写 <c>&lt;dir&gt;/rewritten-cs/&lt;包名&gt;.wbix</c>。
/// </para>
/// <para>
/// <b>dump 规范（两端必须逐字一致；改这里必须同步改 <c>web/scripts/interop.mjs</c>）</b>：
/// </para>
/// <list type="number">
/// <item>对象键按序数（ordinal）升序排列；数组保持原顺序。</item>
/// <item>
/// 所有浮点数输出为<b>字符串</b>，内容为 float32 的最短往返文本（本侧直接用 <c>JsonSerializer.Serialize(float)</c>，
/// 它天然是 .NET 的最短往返格式；网页端用 <c>formatF32</c>）；整数（版本号、页码下标、order）保持 JSON 数值。
/// </item>
/// <item>顶层键：<c>format</c> / <c>version</c> / <c>createdUtc</c> / <c>currentIndex</c> / <c>viewport</c> / <c>pages</c> / <c>resources</c>。</item>
/// <item>
/// <c>createdUtc</c> 只记录<b>存在性</b>：有值时为占位符 <c>&lt;createdUtc&gt;</c>（生产写侧取
/// <c>DateTimeOffset.UtcNow</c>，时钟不可注入），缺省时为 null。
/// </item>
/// <item><c>viewport</c> = <c>{ cameraWorld, zoom, sizeDip }</c>，缺省项输出 null。</item>
/// <item>
/// <c>pages[i]</c> = <c>{ id, strokes, elements }</c>；<c>strokes</c> 为墨迹条目数组（顺序即 z-order）；
/// <c>elements</c> 先 belowInk 组、后 aboveInk 组，组内保持生产读侧给出的顺序。
/// </item>
/// <item>
/// 墨迹条目：stroke 输出 <c>kind</c> / <c>points[{ position{x,y}, pressure }]</c> / <c>colorRgba{x,y,z,w}</c> /
/// <c>baseSize</c> / <c>enablePressure</c>；形状输出 <c>kind</c> / <c>start{x,y}</c> / <c>end{x,y}</c> /
/// <c>colorRgba{x,y,z,w}</c> / <c>width</c>。
/// </item>
/// <item>
/// 元素公共字段 <c>id</c> / <c>layer</c> / <c>order</c> / <c>positionWorld</c> / <c>sizeWorld</c> / <c>type</c>；
/// text 追加 <c>text</c>，link 追加 <c>url</c> / <c>title</c>，media 追加 <c>kind</c> / <c>displayName</c> /
/// <c>resourceId</c>，file 追加 <c>sourcePath</c> / <c>displayName</c>。
/// </item>
/// <item>
/// <b>media 元素的 <c>sourcePath</c> 不参与比对</b>：桌面读侧会把内嵌资源提取到临时文件并改写该字段（属平台相关
/// 运行时行为，不是格式语义）。域模型也不保留 <c>resourceId</c>，因此这里从原始页 JSON 按元素 id 回填
/// （见 <see cref="ReadMediaResourceIds"/>）。
/// </item>
/// <item><c>resources[i]</c> = <c>{ id, type, path, contentType, meta }</c>；<c>meta</c> 为 null 或字符串映射（键同样序数排序）。</item>
/// </list>
/// <para>
/// 比对口径（AC12）：dump 与 diff 都在<b>解压后的文本层</b>做，只锁"条目名 + 条目顺序 + 条目文本"，
/// 不锁 zip 压缩字节与整包 SHA-256。
/// </para>
/// </remarks>
public sealed class WbixInteropDumpTests
{
    /// <summary>待处理目录的环境变量名。</summary>
    internal const string InteropDirectoryVariable = "WINDBOARD_WBIX_INTEROP_DIR";

    /// <summary>处理模式的环境变量名。</summary>
    private const string InteropModeVariable = "WINDBOARD_WBIX_INTEROP_MODE";

    /// <summary>只产出 dump 的模式。</summary>
    private const string ReadMode = "read";

    /// <summary>产出 dump 并用生产写侧重写的模式。</summary>
    private const string RewriteMode = "rewrite";

    /// <summary>dump 输出子目录名。</summary>
    private const string DumpFolderName = "dump-cs";

    /// <summary>重写产物输出子目录名。</summary>
    private const string RewrittenFolderName = "rewritten-cs";

    /// <summary>manifest 条目名（与生产代码一致）。</summary>
    private const string ManifestEntryName = "manifest.json";

    /// <summary>时间戳占位符（具体值不可注入，只锁存在性）。</summary>
    private const string CreatedUtcPlaceholder = "<createdUtc>";

    /// <summary>media 元素类型文本。</summary>
    private const string MediaElementType = "media";

    /// <summary>笔迹下方层级文本。</summary>
    private const string BelowInkLayer = "belowInk";

    /// <summary>笔迹上方层级文本。</summary>
    private const string AboveInkLayer = "aboveInk";

    /// <summary>dump JSON 选项（只影响缩进，键序由 <see cref="SortedDictionary{TKey,TValue}"/> 保证）。</summary>
    private static readonly JsonSerializerOptions DumpJsonOptions = new()
    {
        WriteIndented = true,
        // 不转义非 ASCII 与 HTML 敏感字符，dump 文件保持可读（仅本测试输出用，不做 HTML 嵌入）。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>读 manifest 用的 JSON 选项（对齐生产读侧：大小写不敏感 + 允许尾随逗号 + 允许注释）。</summary>
    private static readonly JsonSerializerOptions PackageJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>写 dump 文件用的 UTF-8 编码（不写 BOM，便于网页端直接读取）。</summary>
    private static readonly UTF8Encoding DumpFileEncoding = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>页数少于 manifest 记录时的兜底空映射。</summary>
    private static readonly IReadOnlyDictionary<Guid, string> EmptyMediaResourceIds = new Dictionary<Guid, string>();

    /// <summary>
    /// 遍历 interop 目录下的 <c>*.wbix</c>，输出规范化 dump（rewrite 模式额外重写整包）。
    /// </summary>
    /// <remarks>
    /// 任一包处理失败都会让测试失败（interop 脚本据此判定 B/D/E 步不通过），错误信息带包名。
    /// 未设置环境变量时由 <see cref="WbixInteropFactAttribute"/> 在发现阶段标记为跳过。
    /// </remarks>
    [WbixInteropFact]
    public async Task Dump_PackagesInInteropDirectory()
    {
        string? directory = Environment.GetEnvironmentVariable(InteropDirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            // 理论上不可达：[WbixInteropFact] 在发现阶段就会把本测试标记为跳过。
            throw new InvalidOperationException($"环境变量 {InteropDirectoryVariable} 缺失。");
        }

        string mode = (Environment.GetEnvironmentVariable(InteropModeVariable) ?? ReadMode).Trim();
        if (!IsKnownMode(mode))
        {
            throw new InvalidOperationException(
                $"环境变量 {InteropModeVariable} 只能是 {ReadMode} 或 {RewriteMode}，实际为 '{mode}'。");
        }

        string root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"interop 目录不存在：{root}");
        }

        string[] packages = Directory.GetFiles(root, "*.wbix", SearchOption.TopDirectoryOnly);
        Array.Sort(packages, StringComparer.Ordinal);
        if (packages.Length == 0)
        {
            throw new InvalidOperationException($"interop 目录下没有 .wbix 文件：{root}");
        }

        bool rewrite = string.Equals(mode, RewriteMode, StringComparison.OrdinalIgnoreCase);
        string dumpDirectory = Path.Combine(root, DumpFolderName);
        string rewrittenDirectory = Path.Combine(root, RewrittenFolderName);

        // 每次运行先清空自己的输出目录，保证可重复运行（不残留上一轮的旧文件）。
        ResetDirectory(dumpDirectory);
        if (rewrite)
        {
            ResetDirectory(rewrittenDirectory);
        }

        var serializer = new WbixWorkspaceSerializer();

        foreach (string packagePath in packages)
        {
            string packageName = Path.GetFileNameWithoutExtension(packagePath);
            try
            {
                byte[] packageBytes = await File.ReadAllBytesAsync(packagePath);
                BoardWorkspaceSnapshot snapshot = await LoadSnapshotAsync(serializer, packageBytes);
                PackageMetadata metadata = ReadPackageMetadata(packageBytes);
                SortedDictionary<string, object?> dump = BuildDump(snapshot, metadata);

                string dumpPath = Path.Combine(dumpDirectory, packageName + ".json");
                await File.WriteAllTextAsync(
                    dumpPath,
                    JsonSerializer.Serialize(dump, DumpJsonOptions),
                    DumpFileEncoding);

                if (rewrite)
                {
                    string rewrittenPath = Path.Combine(rewrittenDirectory, packageName + ".wbix");
                    await WriteRewrittenAsync(serializer, packageBytes, metadata, snapshot, rewrittenPath);
                }
            }
            catch (InvalidDataException ex)
            {
                throw CreatePackageFailure(packageName, ex);
            }
            catch (JsonException ex)
            {
                throw CreatePackageFailure(packageName, ex);
            }
            catch (IOException ex)
            {
                throw CreatePackageFailure(packageName, ex);
            }
        }
    }

    /// <summary>是否为已知的处理模式。</summary>
    private static bool IsKnownMode(string mode)
    {
        return string.Equals(mode, ReadMode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, RewriteMode, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>把单个包的失败包上包名，便于脚本定位。</summary>
    private static InvalidDataException CreatePackageFailure(string packageName, Exception inner)
    {
        return new InvalidDataException($"处理 {packageName}.wbix 失败：{inner.Message}", inner);
    }

    /// <summary>清空并重建目录（可重复运行）。</summary>
    private static void ResetDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    /// <summary>用生产读侧把整包读成域模型。</summary>
    private static async Task<BoardWorkspaceSnapshot> LoadSnapshotAsync(WbixWorkspaceSerializer serializer, byte[] packageBytes)
    {
        using var input = new MemoryStream(packageBytes, writable: false);
        return await serializer.LoadAsync(input);
    }

    /// <summary>用生产写侧重写整包（资源条目原样带过去，否则内嵌图片的 <c>resourceId</c> 会丢失）。</summary>
    private static async Task WriteRewrittenAsync(
        WbixWorkspaceSerializer serializer,
        byte[] packageBytes,
        PackageMetadata metadata,
        BoardWorkspaceSnapshot snapshot,
        string outputPath)
    {
        IReadOnlyList<WbixResourceFile> resourceFiles = ReadResourceFiles(packageBytes, metadata);
        await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await serializer.SaveAsync(snapshot, output, resourceFiles);
    }

    /// <summary>读出 manifest 里声明的全部资源条目字节（重写时原样回写）。</summary>
    private static IReadOnlyList<WbixResourceFile> ReadResourceFiles(byte[] packageBytes, PackageMetadata metadata)
    {
        var files = new List<WbixResourceFile>();
        if (metadata.Resources is null)
        {
            return files;
        }

        using var archive = new ZipArchive(new MemoryStream(packageBytes, writable: false), ZipArchiveMode.Read);
        foreach (WbixResourceEntry resource in metadata.Resources)
        {
            ZipArchiveEntry? entry = archive.GetEntry(NormalizeZipPath(resource.Path));
            if (entry is null)
            {
                // 资源条目缺失时生产读侧也不会用到它（惰性解析），重写时跳过即可。
                continue;
            }

            files.Add(new WbixResourceFile(
                resource.Id,
                resource.Type,
                resource.Path,
                resource.ContentType,
                resource.Meta,
                ReadEntryBytes(entry)));
        }

        return files;
    }

    /// <summary>
    /// 读取 manifest 与"页内 media 元素的 resourceId 映射"。
    /// </summary>
    /// <remarks>
    /// 生产域模型（<see cref="BoardMediaElementSnapshot"/>）把内嵌资源解析成临时文件路径并丢弃 <c>resourceId</c>，
    /// 而网页端域模型保留它；为了让两端 dump 能对齐，这里直接扫原始页 JSON 回填该字段（只做 dump 用的旁路读取，
    /// 不参与生产读写路径）。
    /// </remarks>
    private static PackageMetadata ReadPackageMetadata(byte[] packageBytes)
    {
        using var archive = new ZipArchive(new MemoryStream(packageBytes, writable: false), ZipArchiveMode.Read);
        ZipArchiveEntry manifestEntry = archive.GetEntry(ManifestEntryName)
            ?? throw new InvalidDataException($"zip 缺少条目 {ManifestEntryName}");

        WbixManifest manifest;
        using (Stream stream = manifestEntry.Open())
        {
            manifest = JsonSerializer.Deserialize<WbixManifest>(stream, PackageJsonOptions)
                ?? throw new InvalidDataException($"{ManifestEntryName} 解析结果为空");
        }

        // 与生产读侧一致：按 manifest.pages[].index 排序后逐页读取（顺序即 dump 的 pages 顺序）。
        List<WbixManifestPage> orderedPages = manifest.Pages.OrderBy(page => page.Index).ToList();
        var mediaResourceIdsByPage = new List<IReadOnlyDictionary<Guid, string>>(orderedPages.Count);
        foreach (WbixManifestPage page in orderedPages)
        {
            mediaResourceIdsByPage.Add(ReadMediaResourceIds(archive, page.Path));
        }

        return new PackageMetadata(
            manifest.Format,
            manifest.Version,
            manifest.CreatedUtc,
            manifest.Resources,
            mediaResourceIdsByPage);
    }

    /// <summary>从页 JSON 里收集"media 元素 id → resourceId"（只用于补齐 dump）。</summary>
    private static IReadOnlyDictionary<Guid, string> ReadMediaResourceIds(ZipArchive archive, string pagePath)
    {
        var map = new Dictionary<Guid, string>();
        ZipArchiveEntry? entry = archive.GetEntry(NormalizeZipPath(pagePath));
        if (entry is null)
        {
            return map;
        }

        using Stream stream = entry.Open();
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !TryGetProperty(root, "elements", out JsonElement elements)
            || elements.ValueKind != JsonValueKind.Array)
        {
            return map;
        }

        foreach (JsonElement element in elements.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object
                || !TryGetProperty(element, "type", out JsonElement type)
                || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString()?.Trim(), MediaElementType, StringComparison.OrdinalIgnoreCase)
                || !TryGetProperty(element, "data", out JsonElement data)
                || data.ValueKind != JsonValueKind.Object
                || !TryGetProperty(data, "id", out JsonElement idElement)
                || idElement.ValueKind != JsonValueKind.String
                || !Guid.TryParse(idElement.GetString(), out Guid elementId)
                || !TryGetProperty(data, "resourceId", out JsonElement resourceIdElement)
                || resourceIdElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? resourceId = resourceIdElement.GetString();
            if (!string.IsNullOrWhiteSpace(resourceId))
            {
                map[elementId] = resourceId;
            }
        }

        return map;
    }

    /// <summary>按名取 JSON 属性（忽略大小写，对齐生产读侧的大小写不敏感反序列化）。</summary>
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>zip 条目路径归一（与生产读侧一致：反斜杠按正斜杠处理）。</summary>
    private static string NormalizeZipPath(string path)
    {
        return path.Replace('\\', '/');
    }

    /// <summary>读取 zip 条目全部字节。</summary>
    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>构造顶层 dump。</summary>
    private static SortedDictionary<string, object?> BuildDump(BoardWorkspaceSnapshot snapshot, PackageMetadata metadata)
    {
        SortedDictionary<string, object?> dump = NewObject();
        dump["format"] = metadata.Format;
        dump["version"] = metadata.Version;
        // 时间戳不可注入：只锁"存在性"，具体值不参与比对。
        dump["createdUtc"] = metadata.CreatedUtc == default ? null : CreatedUtcPlaceholder;
        dump["currentIndex"] = snapshot.CurrentIndex;
        dump["viewport"] = BuildViewport(snapshot);
        dump["pages"] = BuildPages(snapshot, metadata);
        dump["resources"] = BuildResources(metadata.Resources);
        return dump;
    }

    /// <summary>构造 <c>viewport</c> 节点。</summary>
    private static SortedDictionary<string, object?> BuildViewport(BoardWorkspaceSnapshot snapshot)
    {
        SortedDictionary<string, object?> viewport = NewObject();
        viewport["cameraWorld"] = snapshot.ViewportCameraWorld is { } camera ? BuildVec2(camera) : null;
        viewport["zoom"] = snapshot.ViewportZoom is { } zoom ? F32Text(zoom) : null;
        viewport["sizeDip"] = snapshot.ViewportSizeDip is { } size ? BuildVec2(size) : null;
        return viewport;
    }

    /// <summary>构造 <c>pages</c> 数组。</summary>
    private static List<object?> BuildPages(BoardWorkspaceSnapshot snapshot, PackageMetadata metadata)
    {
        var pages = new List<object?>(snapshot.Pages.Count);
        for (int index = 0; index < snapshot.Pages.Count; index++)
        {
            BoardPageSnapshot page = snapshot.Pages[index];
            IReadOnlyDictionary<Guid, string> mediaResourceIds = index < metadata.MediaResourceIdsByPage.Count
                ? metadata.MediaResourceIdsByPage[index]
                : EmptyMediaResourceIds;

            SortedDictionary<string, object?> node = NewObject();
            node["id"] = page.Id.ToString();
            node["strokes"] = BuildInkItems(page.Strokes);
            node["elements"] = BuildElements(page, mediaResourceIds);
            pages.Add(node);
        }

        return pages;
    }

    /// <summary>构造墨迹条目数组（顺序即 z-order）。</summary>
    private static List<object?> BuildInkItems(IReadOnlyList<InkItemSnapshot> items)
    {
        var nodes = new List<object?>(items.Count);
        foreach (InkItemSnapshot item in items)
        {
            SortedDictionary<string, object?> node = NewObject();
            node["kind"] = item.Kind;

            if (item.Stroke is { } stroke)
            {
                var points = new List<object?>(stroke.Points.Count);
                foreach (StrokePointSnapshot point in stroke.Points)
                {
                    SortedDictionary<string, object?> pointNode = NewObject();
                    pointNode["position"] = BuildVec2(point.Position);
                    pointNode["pressure"] = F32Text(point.Pressure);
                    points.Add(pointNode);
                }

                node["points"] = points;
                node["colorRgba"] = BuildVec4(stroke.ColorRgba);
                node["baseSize"] = F32Text(stroke.BaseSize);
                node["enablePressure"] = stroke.EnablePressure;
            }
            else if (item.Shape is { } shape)
            {
                node["start"] = BuildVec2(shape.Start);
                node["end"] = BuildVec2(shape.End);
                node["colorRgba"] = BuildVec4(shape.ColorRgba);
                node["width"] = F32Text(shape.Width);
            }

            nodes.Add(node);
        }

        return nodes;
    }

    /// <summary>构造元素数组（先 belowInk 组，后 aboveInk 组）。</summary>
    private static List<object?> BuildElements(BoardPageSnapshot page, IReadOnlyDictionary<Guid, string> mediaResourceIds)
    {
        var nodes = new List<object?>();
        AppendElementNodes(nodes, page.ElementsBelowInk, BelowInkLayer, mediaResourceIds);
        AppendElementNodes(nodes, page.ElementsAboveInk, AboveInkLayer, mediaResourceIds);
        return nodes;
    }

    /// <summary>追加一层的元素节点。</summary>
    private static void AppendElementNodes(
        List<object?> target,
        IReadOnlyList<BoardElementSnapshot>? elements,
        string layer,
        IReadOnlyDictionary<Guid, string> mediaResourceIds)
    {
        if (elements is null)
        {
            return;
        }

        foreach (BoardElementSnapshot element in elements)
        {
            SortedDictionary<string, object?> node = NewObject();
            node["id"] = element.Id.ToString();
            node["layer"] = layer;
            node["order"] = element.Order;
            node["positionWorld"] = BuildVec2(element.PositionWorld);
            node["sizeWorld"] = BuildVec2(element.SizeWorld);

            switch (element)
            {
                case BoardTextElementSnapshot text:
                    node["type"] = "text";
                    node["text"] = text.Text;
                    break;

                case BoardLinkElementSnapshot link:
                    node["type"] = "link";
                    node["url"] = link.Url;
                    node["title"] = link.Title;
                    break;

                case BoardMediaElementSnapshot media:
                    node["type"] = MediaElementType;
                    node["kind"] = MediaKindText(media.Kind);
                    node["displayName"] = media.DisplayName;
                    // sourcePath 不参与比对（桌面读侧会改写成临时文件路径）；resourceId 由旁路读取回填。
                    node["resourceId"] = mediaResourceIds.TryGetValue(media.Id, out string? resourceId) ? resourceId : null;
                    break;

                case BoardFileElementSnapshot file:
                    node["type"] = "file";
                    node["sourcePath"] = file.SourcePath;
                    node["displayName"] = file.DisplayName;
                    break;

                default:
                    node["type"] = "unknown";
                    break;
            }

            target.Add(node);
        }
    }

    /// <summary>构造 <c>resources</c> 数组。</summary>
    private static List<object?> BuildResources(IReadOnlyList<WbixResourceEntry>? resources)
    {
        var nodes = new List<object?>();
        if (resources is null)
        {
            return nodes;
        }

        foreach (WbixResourceEntry resource in resources)
        {
            SortedDictionary<string, object?> node = NewObject();
            node["id"] = resource.Id;
            node["type"] = resource.Type;
            node["path"] = resource.Path;
            node["contentType"] = resource.ContentType;
            node["meta"] = resource.Meta is null ? null : BuildStringMap(resource.Meta);
            nodes.Add(node);
        }

        return nodes;
    }

    /// <summary>构造字符串映射节点（键序数排序）。</summary>
    private static SortedDictionary<string, object?> BuildStringMap(IReadOnlyDictionary<string, string> source)
    {
        SortedDictionary<string, object?> node = NewObject();
        foreach (KeyValuePair<string, string> pair in source)
        {
            node[pair.Key] = pair.Value;
        }

        return node;
    }

    /// <summary>媒体种类文本（对齐生产枚举）。</summary>
    private static string MediaKindText(BoardMediaKind kind)
    {
        return kind switch
        {
            BoardMediaKind.Image => "image",
            BoardMediaKind.Audio => "audio",
            _ => "video",
        };
    }

    /// <summary>二维向量节点。</summary>
    private static SortedDictionary<string, object?> BuildVec2(Vector2 value)
    {
        SortedDictionary<string, object?> node = NewObject();
        node["x"] = F32Text(value.X);
        node["y"] = F32Text(value.Y);
        return node;
    }

    /// <summary>颜色节点。</summary>
    private static SortedDictionary<string, object?> BuildVec4(Vector4 value)
    {
        SortedDictionary<string, object?> node = NewObject();
        node["x"] = F32Text(value.X);
        node["y"] = F32Text(value.Y);
        node["z"] = F32Text(value.Z);
        node["w"] = F32Text(value.W);
        return node;
    }

    /// <summary>新建一个键序数排序的空对象节点。</summary>
    private static SortedDictionary<string, object?> NewObject()
    {
        return new SortedDictionary<string, object?>(StringComparer.Ordinal);
    }

    /// <summary>
    /// float32 → 最短往返文本。
    /// </summary>
    /// <remarks>
    /// <see cref="JsonSerializer.Serialize{TValue}(TValue, JsonSerializerOptions?)"/> 对 <see cref="float"/> 输出的就是
    /// .NET 的最短往返格式（整数无小数点、<c>-0</c> 保留符号、指数形态 <c>1E-07</c>），与网页端 <c>formatF32</c> 一致。
    /// </remarks>
    private static string F32Text(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException(
                $"dump 不支持非有限浮点数：{value.ToString(CultureInfo.InvariantCulture)}");
        }

        return JsonSerializer.Serialize(value);
    }

    /// <summary>manifest 里与 dump 相关的元数据（域模型不携带这些字段）。</summary>
    /// <param name="Format">manifest 的 <c>format</c> 原文。</param>
    /// <param name="Version">manifest 的 <c>version</c>。</param>
    /// <param name="CreatedUtc">manifest 的 <c>createdUtc</c>（缺省为 <see langword="default"/>）。</param>
    /// <param name="Resources">manifest 的资源列表（可为 null）。</param>
    /// <param name="MediaResourceIdsByPage">按 manifest 页顺序排列的"media 元素 id → resourceId"映射。</param>
    private sealed record PackageMetadata(
        string Format,
        int Version,
        DateTimeOffset CreatedUtc,
        IReadOnlyList<WbixResourceEntry>? Resources,
        IReadOnlyList<IReadOnlyDictionary<Guid, string>> MediaResourceIdsByPage);
}

/// <summary>
/// 只在设置了 <see cref="WbixInteropDumpTests.InteropDirectoryVariable"/> 时才执行的 Fact。
/// </summary>
/// <remarks>
/// 未设置环境变量时把 <see cref="FactAttribute.Skip"/> 填上，测试在发现阶段就被标记为"已跳过"，
/// 因此正常的 <c>dotnet test WindBoard.slnx</c> 不会因为缺少 interop 目录而变红。
/// <para>
/// 这里不用"运行时抛 <c>Xunit.Sdk.SkipException</c>"的写法：xUnit v2 没有运行时 Skip 基础设施
/// （<c>SkipException.ForSkip</c> 的文档写明 only works in v3 and later），运行时抛该异常只会让测试失败。
/// </para>
/// </remarks>
public sealed class WbixInteropFactAttribute : FactAttribute
{
    public WbixInteropFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(WbixInteropDumpTests.InteropDirectoryVariable)))
        {
            Skip = $"未设置环境变量 {WbixInteropDumpTests.InteropDirectoryVariable}，跳过 WBIX 双端一致性 dump 生成。";
        }
    }
}
