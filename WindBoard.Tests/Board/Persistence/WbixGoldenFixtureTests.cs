using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WindBoard.Board.Elements;
using WindBoard.Board.Persistence;
using WindBoard.Board.Persistence.Wbix;
using Xunit;

namespace WindBoard.Tests.Board.Persistence;

/// <summary>
/// WBIX 字节级 golden 测试：把"写出形态"逐字节锁死，使第二个独立实现（网页端）能安全对齐。
/// </summary>
/// <remarks>
/// 断言范围（对应 design D12 / prd AC12）：
/// - Zip 条目名与写出顺序（pages → assets → manifest 最后）；
/// - 每个文本条目解压后与语料逐字节一致（含缩进、键序、数字格式、转义）；
/// - <c>createdUtc</c> 为 UTC ISO-8601 且固定 7 位小数（时间戳本身不可注入，故校验格式后归一化再比对）；
/// - 读回一致：写出的文件重新读入后关键字段与模型一致。
///
/// 语料位于 <c>WindBoard.Tests/Fixtures/wbix/</c>，重建方式见 <see cref="WbixGoldenCorpus"/>。
/// </remarks>
public sealed partial class WbixGoldenFixtureTests
{
    /// <summary>完整模型（多页、多元素、多形状、资源）。</summary>
    private const string StandardModelName = "standard";

    /// <summary>最小模型（空页、无资源、视口缺省为 null）。</summary>
    private const string MinimalModelName = "minimal";

    /// <summary>manifest 条目名（与生产代码一致）。</summary>
    private const string ManifestEntryName = "manifest.json";

    /// <summary>时间戳占位符（语料中以它代替真实时间）。</summary>
    private const string CreatedUtcPlaceholder = "<createdUtc>";

    /// <summary>v1/v2 样本的页 Id（固定值，便于跨端对齐）。</summary>
    private static readonly Guid LegacyPageId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    private static readonly JsonSerializerOptions ModelJsonOptions = CreateModelJsonOptions();

    [Fact]
    public async Task Save_StandardModel_MatchesGoldenCorpus()
    {
        await AssertSaveMatchesGoldenAsync(StandardModelName);
    }

    [Fact]
    public async Task Save_MinimalModel_MatchesGoldenCorpus()
    {
        await AssertSaveMatchesGoldenAsync(MinimalModelName);
    }

    [Fact]
    public async Task Save_StandardModel_WritesUtcCreatedTimestamp()
    {
        // createdUtc 取 DateTimeOffset.UtcNow（无可注入时钟），因此只锁"格式"：UTC + ISO-8601。
        // System.Text.Json 会去掉小数部分末尾的 0，故小数位数为 0~7 位可变，不能写死 7 位。
        WbixGoldenModel model = ReadModel(StandardModelName);
        byte[] bytes = await SaveAsync(CreateSnapshot(model), CreateResourceFiles(model));

        string manifestJson = ReadEntryText(bytes, ManifestEntryName);
        string normalized = ReplaceCreatedUtc(manifestJson, out string createdUtcValue);
        Assert.NotEqual(manifestJson, normalized);

        Assert.Matches(CreatedUtcValuePattern(), createdUtcValue);

        Assert.True(
            DateTimeOffset.TryParse(createdUtcValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed),
            $"createdUtc 无法解析为 DateTimeOffset：'{createdUtcValue}'");
        Assert.Equal(TimeSpan.Zero, parsed.Offset);

        // 规范性：写出的字符串必须是该值的规范形态——重新序列化后逐字符相同（web 端据此对齐）。
        Assert.Equal($"\"{createdUtcValue}\"", JsonSerializer.Serialize(parsed));

        TimeSpan age = DateTimeOffset.UtcNow - parsed;
        Assert.True(age.Duration() < TimeSpan.FromMinutes(5), $"createdUtc 与当前时间偏差过大：{createdUtcValue}");
    }

    [Fact]
    public async Task Load_SavedStandardFile_RoundTripsModel()
    {
        WbixGoldenModel model = ReadModel(StandardModelName);
        byte[] bytes = await SaveAsync(CreateSnapshot(model), CreateResourceFiles(model));

        using var stream = new MemoryStream(bytes, writable: false);
        var serializer = new WbixWorkspaceSerializer();
        BoardWorkspaceSnapshot loaded = await serializer.LoadAsync(stream);

        AssertStandardSnapshot(loaded, model);
    }

    [Fact]
    public async Task Load_CommittedV3Fixture_MatchesModel()
    {
        // 冻结的 v3 样本：不经过写侧，直接验证读侧对语料的解释与模型一致。
        // 重建模式下语料可能尚未落盘（用例顺序不定），改用同源字节，保证一次重建即可跑完。
        WbixGoldenModel model = ReadModel(StandardModelName);
        byte[] bytes = WbixGoldenCorpus.RegenerateRequested
            ? await SaveAsync(CreateSnapshot(model), CreateResourceFiles(model))
            : WbixGoldenCorpus.ReadBinary("standard-v3");

        using var stream = new MemoryStream(bytes, writable: false);
        var serializer = new WbixWorkspaceSerializer();
        BoardWorkspaceSnapshot loaded = await serializer.LoadAsync(stream);

        AssertStandardSnapshot(loaded, model);
    }

    [Theory]
    [InlineData("legacy-v1")]
    [InlineData("legacy-v2")]
    public async Task Load_CommittedLegacyFixture_MapsFlatStrokes(string fixtureName)
    {
        // v1/v2 样本为"扁平笔迹"形态（无 kind/stroke 包装）：桌面端自 v3 起不再产出该形态，
        // 故语料为按格式手写的样本，用于锁定"版本分派 → 扁平读取"这一读侧契约。
        byte[] bytes = WbixGoldenCorpus.RegenerateRequested
            ? BuildLegacyFixture(ParseLegacyFixtureVersion(fixtureName))
            : WbixGoldenCorpus.ReadBinary(fixtureName);

        using var stream = new MemoryStream(bytes, writable: false);
        var serializer = new WbixWorkspaceSerializer();
        BoardWorkspaceSnapshot snapshot = await serializer.LoadAsync(stream);

        BoardPageSnapshot page = Assert.Single(snapshot.Pages);
        Assert.Equal(LegacyPageId, page.Id);
        Assert.Equal(2, page.Strokes.Count);

        foreach (InkItemSnapshot item in page.Strokes)
        {
            Assert.Equal(BoardInkItemCodec.StrokeKind, item.Kind);
            Assert.NotNull(item.Stroke);
            Assert.Null(item.Shape);
        }

        StrokeSnapshot first = page.Strokes[0].Stroke!;
        Assert.True(first.EnablePressure);
        AssertEx.Equal(3.25f, first.BaseSize);
        AssertEx.Equal(0.1f, first.ColorRgba.X);
        AssertEx.Equal(1.0f, first.ColorRgba.W);
        Assert.Equal(2, first.Points.Count);
        AssertEx.Equal(new Vector2(10.5f, 20.25f), first.Points[0].Position);
        AssertEx.Equal(0.8f, first.Points[1].Pressure);

        StrokeSnapshot second = page.Strokes[1].Stroke!;
        Assert.False(second.EnablePressure);
        Assert.Single(second.Points);
    }

    private static async Task AssertSaveMatchesGoldenAsync(string modelName)
    {
        WbixGoldenModel model = ReadModel(modelName);
        IReadOnlyList<WbixResourceFile> resources = CreateResourceFiles(model);
        byte[] bytes = await SaveAsync(CreateSnapshot(model), resources);

        if (WbixGoldenCorpus.RegenerateRequested)
        {
            RegenerateCorpus(modelName, bytes, resources);
            return;
        }

        AssertGoldenCorpus(modelName, bytes, resources);
    }

    private static void AssertGoldenCorpus(string modelName, byte[] bytes, IReadOnlyList<WbixResourceFile> resources)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);

        string[] expectedEntries = WbixGoldenCorpus.ReadEntryList(modelName);
        Assert.Equal(expectedEntries, GetEntryNames(archive));

        foreach (string entryName in expectedEntries)
        {
            ZipArchiveEntry entry = archive.GetEntry(entryName)
                ?? throw new InvalidOperationException($"Zip 缺少条目：{entryName}（语料与写侧不一致）");
            byte[] actualBytes = ReadEntryBytes(entry);

            WbixResourceFile? resource = FindResourceByPath(resources, entryName);
            if (resource is not null)
            {
                // 二进制资源不做文本语料，直接与模型声明的字节比对。
                Assert.Equal(resource.Bytes, actualBytes);
                continue;
            }

            string actualText = Encoding.UTF8.GetString(actualBytes);
            AssertUsesCrlfOnly(actualText, entryName);

            string actualForCompare = actualText;
            if (string.Equals(entryName, ManifestEntryName, StringComparison.Ordinal))
            {
                actualForCompare = ReplaceCreatedUtc(actualText, out _);
            }

            string expectedText = WbixGoldenCorpus.ReadExpectedText(modelName, entryName);
            Assert.Equal(
                WbixGoldenCorpus.NormalizeEol(expectedText).TrimEnd('\n'),
                WbixGoldenCorpus.NormalizeEol(actualForCompare));
        }
    }

    private static void RegenerateCorpus(string modelName, byte[] bytes, IReadOnlyList<WbixResourceFile> resources)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);

        string[] entryNames = GetEntryNames(archive);
        WbixGoldenCorpus.WriteEntryList(modelName, entryNames);

        foreach (string entryName in entryNames)
        {
            if (FindResourceByPath(resources, entryName) is not null)
            {
                continue;
            }

            ZipArchiveEntry entry = archive.GetEntry(entryName)
                ?? throw new InvalidOperationException($"Zip 缺少条目：{entryName}");
            string text = Encoding.UTF8.GetString(ReadEntryBytes(entry));

            if (string.Equals(entryName, ManifestEntryName, StringComparison.Ordinal))
            {
                text = ReplaceCreatedUtc(text, out _);
            }

            WbixGoldenCorpus.WriteExpectedText(modelName, entryName, text);
        }

        if (!string.Equals(modelName, StandardModelName, StringComparison.Ordinal))
        {
            return;
        }

        // v3 冻结样本 = 生产写侧对本模型的真实输出；v1/v2 为手写样本（见 Load_CommittedLegacyFixture_MapsFlatStrokes）。
        WbixGoldenCorpus.WriteBinary("standard-v3", bytes);
        WbixGoldenCorpus.WriteBinary("legacy-v1", BuildLegacyFixture(version: 1));
        WbixGoldenCorpus.WriteBinary("legacy-v2", BuildLegacyFixture(version: 2));
    }

    private static WbixGoldenModel ReadModel(string modelName)
    {
        string json = WbixGoldenCorpus.ReadModelText(modelName);
        return JsonSerializer.Deserialize<WbixGoldenModel>(json, ModelJsonOptions)
            ?? throw new InvalidOperationException($"模型反序列化失败（返回 null）：model/{modelName}.json");
    }

    private static JsonSerializerOptions CreateModelJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            IncludeFields = true,
        };

        // 与写侧一致：模型中的笔迹条目按 v3 包装形态解析。
        options.Converters.Add(new InkItemSnapshotJsonConverter(InkItemReadMode.Wrapped));
        return options;
    }

    private static BoardWorkspaceSnapshot CreateSnapshot(WbixGoldenModel model)
    {
        var pages = new List<BoardPageSnapshot>(model.Pages.Count);
        foreach (WbixPagePayload payload in model.Pages)
        {
            (IReadOnlyList<BoardElementSnapshot>? below, IReadOnlyList<BoardElementSnapshot>? above) =
                CreateElementSnapshots(payload.Elements);

            pages.Add(new BoardPageSnapshot(
                payload.Id,
                payload.Strokes ?? Array.Empty<InkItemSnapshot>(),
                below,
                above));
        }

        return new BoardWorkspaceSnapshot(
            pages,
            model.CurrentIndex,
            model.ViewportCameraWorld,
            model.ViewportZoom,
            model.ViewportSizeDip);
    }

    private static (IReadOnlyList<BoardElementSnapshot>? Below, IReadOnlyList<BoardElementSnapshot>? Above) CreateElementSnapshots(
        IReadOnlyList<WbixPageElement>? elements)
    {
        if (elements is null || elements.Count == 0)
        {
            return (null, null);
        }

        List<BoardElementSnapshot>? below = null;
        List<BoardElementSnapshot>? above = null;

        foreach (WbixPageElement element in elements)
        {
            BoardElementSnapshot snapshot = CreateElementSnapshot(element);
            if (IsAboveInk(element.Data))
            {
                above ??= new List<BoardElementSnapshot>();
                above.Add(snapshot);
            }
            else
            {
                below ??= new List<BoardElementSnapshot>();
                below.Add(snapshot);
            }
        }

        return (below, above);
    }

    private static bool IsAboveInk(JsonElement data)
    {
        string layer = ReadString(data, "layer");
        return string.Equals(layer, "aboveInk", StringComparison.OrdinalIgnoreCase);
    }

    private static BoardElementSnapshot CreateElementSnapshot(WbixPageElement element)
    {
        JsonElement data = element.Data;

        Guid id = data.GetProperty("id").GetGuid();
        Vector2 positionWorld = ReadVector2(data.GetProperty("positionWorld"));
        Vector2 sizeWorld = ReadVector2(data.GetProperty("sizeWorld"));
        int order = data.GetProperty("order").GetInt32();

        switch (element.Type)
        {
            case "text":
                return new BoardTextElementSnapshot(id, positionWorld, sizeWorld, order, ReadString(data, "text"));

            case "link":
                return new BoardLinkElementSnapshot(
                    id,
                    positionWorld,
                    sizeWorld,
                    order,
                    ReadString(data, "url"),
                    ReadNullableString(data, "title"));

            case "media":
                return new BoardMediaElementSnapshot(
                    id,
                    positionWorld,
                    sizeWorld,
                    order,
                    ParseMediaKind(ReadString(data, "kind")),
                    ReadString(data, "sourcePath"),
                    ReadString(data, "displayName"));

            case "file":
                return new BoardFileElementSnapshot(
                    id,
                    positionWorld,
                    sizeWorld,
                    order,
                    ReadString(data, "sourcePath"),
                    ReadString(data, "displayName"));

            default:
                throw new InvalidOperationException($"模型中出现未知元素类型：'{element.Type}'");
        }
    }

    private static BoardMediaKind ParseMediaKind(string kind)
    {
        return kind switch
        {
            "image" => BoardMediaKind.Image,
            "audio" => BoardMediaKind.Audio,
            "video" => BoardMediaKind.Video,
            _ => throw new InvalidOperationException($"模型中出现未知媒体类型：'{kind}'"),
        };
    }

    private static Vector2 ReadVector2(JsonElement element)
    {
        return new Vector2(element.GetProperty("x").GetSingle(), element.GetProperty("y").GetSingle());
    }

    private static string ReadString(JsonElement data, string propertyName)
    {
        return ReadNullableString(data, propertyName) ?? string.Empty;
    }

    private static string? ReadNullableString(JsonElement data, string propertyName)
    {
        if (!data.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static IReadOnlyList<WbixResourceFile> CreateResourceFiles(WbixGoldenModel model)
    {
        if (model.Resources is null || model.Resources.Count == 0)
        {
            return Array.Empty<WbixResourceFile>();
        }

        var files = new List<WbixResourceFile>(model.Resources.Count);
        foreach (WbixGoldenResource resource in model.Resources)
        {
            files.Add(new WbixResourceFile(
                resource.Id,
                resource.Type,
                resource.Path,
                resource.ContentType,
                resource.Meta,
                Convert.FromBase64String(resource.Base64)));
        }

        return files;
    }

    private static WbixResourceFile? FindResourceByPath(IReadOnlyList<WbixResourceFile> resources, string entryName)
    {
        foreach (WbixResourceFile resource in resources)
        {
            if (string.Equals(resource.Path, entryName, StringComparison.Ordinal))
            {
                return resource;
            }
        }

        return null;
    }

    private static async Task<byte[]> SaveAsync(BoardWorkspaceSnapshot snapshot, IReadOnlyList<WbixResourceFile> resources)
    {
        var serializer = new WbixWorkspaceSerializer();
        using var stream = new MemoryStream();
        await serializer.SaveAsync(snapshot, stream, resources);
        return stream.ToArray();
    }

    private static string[] GetEntryNames(ZipArchive archive)
    {
        var names = new string[archive.Entries.Count];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = archive.Entries[i].FullName;
        }

        return names;
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string ReadEntryText(byte[] wbixBytes, string entryName)
    {
        using var archive = new ZipArchive(new MemoryStream(wbixBytes, writable: false), ZipArchiveMode.Read);
        ZipArchiveEntry entry = archive.GetEntry(entryName)
            ?? throw new InvalidOperationException($"Zip 缺少条目：{entryName}");
        return Encoding.UTF8.GetString(ReadEntryBytes(entry));
    }

    private static string ReplaceCreatedUtc(string manifestJson, out string createdUtcValue)
    {
        using JsonDocument document = JsonDocument.Parse(manifestJson);
        createdUtcValue = document.RootElement.GetProperty("createdUtc").GetString() ?? string.Empty;
        return manifestJson.Replace(createdUtcValue, CreatedUtcPlaceholder, StringComparison.Ordinal);
    }

    private static void AssertUsesCrlfOnly(string text, string entryName)
    {
        Assert.False(text.EndsWith('\n'), $"{entryName}：条目文本不应有尾随换行。");

        string withoutCrlf = text.Replace("\r\n", string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', withoutCrlf);
        Assert.DoesNotContain('\r', withoutCrlf);
    }

    private static void AssertStandardSnapshot(BoardWorkspaceSnapshot snapshot, WbixGoldenModel model)
    {
        Assert.Equal(3, snapshot.Pages.Count);
        Assert.Equal(1, snapshot.CurrentIndex);
        Assert.True(snapshot.ViewportZoom.HasValue);
        AssertEx.Equal(1.25f, snapshot.ViewportZoom.GetValueOrDefault());
        AssertEx.Equal(new Vector2(-12.5f, 8.25f), snapshot.ViewportCameraWorld.GetValueOrDefault());
        AssertEx.Equal(new Vector2(1280f, 720f), snapshot.ViewportSizeDip.GetValueOrDefault());

        BoardPageSnapshot page0 = snapshot.Pages[0];
        Assert.Equal(model.Pages[0].Id, page0.Id);

        string[] expectedKinds = { "stroke", "line", "rect", "ellipse", "arrow" };
        var kinds = new string[page0.Strokes.Count];
        for (int i = 0; i < kinds.Length; i++)
        {
            kinds[i] = page0.Strokes[i].Kind;
        }

        Assert.Equal(expectedKinds, kinds);

        StrokeSnapshot stroke = page0.Strokes[0].Stroke!;
        Assert.True(stroke.EnablePressure);
        AssertEx.Equal(3.25f, stroke.BaseSize);
        AssertEx.Equal(0.5f, stroke.Points[0].Pressure);
        AssertEx.Equal(new Vector2(-3.75f, 41.5f), stroke.Points[2].Position);

        ShapeSnapshot arrow = page0.Strokes[4].Shape!;
        AssertEx.Equal(new Vector2(420.5f, 360.25f), arrow.End);
        AssertEx.Equal(2.5f, arrow.Width);

        Assert.NotNull(page0.ElementsBelowInk);
        Assert.Equal(2, page0.ElementsBelowInk!.Count);

        BoardTextElementSnapshot text = Assert.IsType<BoardTextElementSnapshot>(page0.ElementsBelowInk[0]);
        Assert.Equal(ReadString(model.Pages[0].Elements![0].Data, "text"), text.Text);

        BoardLinkElementSnapshot link = Assert.IsType<BoardLinkElementSnapshot>(page0.ElementsBelowInk[1]);
        Assert.Null(link.Title);

        Assert.NotNull(page0.ElementsAboveInk);
        Assert.Equal(3, page0.ElementsAboveInk!.Count);

        BoardMediaElementSnapshot image = Assert.IsType<BoardMediaElementSnapshot>(page0.ElementsAboveInk[0]);
        Assert.Equal(BoardMediaKind.Image, image.Kind);
        Assert.Equal("photo.png", image.DisplayName);
        // 内嵌图片不落盘本地路径：读侧把资源提取到临时目录，路径随之改写。
        Assert.Equal("img-0001.png", Path.GetFileName(image.SourcePath));

        BoardMediaElementSnapshot video = Assert.IsType<BoardMediaElementSnapshot>(page0.ElementsAboveInk[1]);
        Assert.Equal(BoardMediaKind.Video, video.Kind);
        Assert.Equal("D:/media/clip.mp4", video.SourcePath);

        BoardFileElementSnapshot file = Assert.IsType<BoardFileElementSnapshot>(page0.ElementsAboveInk[2]);
        Assert.Equal(2, file.Order);
        Assert.Equal("D:/docs/报告.docx", file.SourcePath);

        BoardPageSnapshot page1 = snapshot.Pages[1];
        Assert.Equal(model.Pages[1].Id, page1.Id);
        Assert.Equal(2, page1.Strokes.Count);
        Assert.Single(page1.Strokes[0].Stroke!.Points);
        Assert.False(page1.Strokes[0].Stroke!.EnablePressure);
        AssertEx.Equal(0.33333334f, page1.Strokes[1].Stroke!.Points[0].Pressure);

        // 第 3 页专锁极端坐标（±1e6）的浮点写法与变宽多段笔。
        BoardPageSnapshot page2 = snapshot.Pages[2];
        Assert.Equal(model.Pages[2].Id, page2.Id);

        StrokeSnapshot extreme = Assert.Single(page2.Strokes).Stroke!;
        Assert.True(extreme.EnablePressure);
        AssertEx.Equal(12.75f, extreme.BaseSize);
        Assert.Equal(4, extreme.Points.Count);
        AssertEx.Equal(new Vector2(1000000f, -1000000f), extreme.Points[0].Position);
        AssertEx.Equal(new Vector2(-1000000f, 1000000f), extreme.Points[1].Position);
        AssertEx.Equal(new Vector2(999999.94f, -0.5f), extreme.Points[2].Position);
        AssertEx.Equal(new Vector2(-999999.5f, 0.125f), extreme.Points[3].Position);
        AssertEx.Equal(0.15f, extreme.Points[0].Pressure);
        AssertEx.Equal(0.95f, extreme.Points[3].Pressure);
        Assert.Empty(page2.ElementsBelowInk!);
        Assert.Empty(page2.ElementsAboveInk!);
    }

    /// <summary>
    /// 由 legacy 语料名得到它代表的格式版本。
    /// </summary>
    private static int ParseLegacyFixtureVersion(string fixtureName)
    {
        return fixtureName switch
        {
            "legacy-v1" => 1,
            "legacy-v2" => 2,
            _ => throw new InvalidOperationException($"未知的 legacy 语料名：{fixtureName}"),
        };
    }

    /// <summary>
    /// 构造 v1/v2 手写样本：manifest 的 version 为 1 或 2，页内笔迹为扁平形态（无 kind/stroke 包装）。
    /// </summary>
    private static byte[] BuildLegacyFixture(int version)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, ManifestEntryName, $$"""
                {
                  "format": "wbix",
                  "version": {{version}},
                  "createdUtc": "2026-01-01T00:00:00+00:00",
                  "currentIndex": 0,
                  "pages": [ { "id": "{{LegacyPageId:D}}", "index": 0, "path": "pages/page-000.json" } ],
                  "resources": []
                }
                """);

            WriteZipEntry(archive, "pages/page-000.json", $$"""
                {
                  "id": "{{LegacyPageId:D}}",
                  "strokes": [
                    {
                      "points": [
                        { "position": { "x": 10.5, "y": 20.25 }, "pressure": 0.5 },
                        { "position": { "x": 12.0, "y": 24.0 }, "pressure": 0.8 }
                      ],
                      "colorRgba": { "x": 0.1, "y": 0.2, "z": 0.3, "w": 1.0 },
                      "baseSize": 3.25,
                      "enablePressure": true
                    },
                    {
                      "points": [
                        { "position": { "x": -1.0, "y": -2.0 }, "pressure": 1.0 }
                      ],
                      "colorRgba": { "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 },
                      "baseSize": 2.0,
                      "enablePressure": false
                    }
                  ],
                  "elements": []
                }
                """);
        }

        return stream.ToArray();
    }

    private static void WriteZipEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    /// <summary>createdUtc 的固化格式：UTC ISO-8601，固定 7 位小数。</summary>
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?\+00:00$", RegexOptions.CultureInvariant)]
    private static partial Regex CreatedUtcValuePattern();
}
