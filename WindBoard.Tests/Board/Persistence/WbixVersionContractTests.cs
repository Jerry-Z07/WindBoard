using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using WindBoard.Board.Persistence;
using WindBoard.Board.Persistence.Wbix;
using Xunit;

namespace WindBoard.Tests.Board.Persistence;

/// <summary>
/// WBIX 版本契约（读侧分派）测试。
///
/// 契约：<c>manifest.version</c> 决定使用哪个页读取器；未知版本在读取任何页之前被拒绝；
/// v1/v2 读扁平条目、v3 读包装条目，且两种形态互相容忍（v3 遇扁平条目走兜底，每文件告警一次）。
/// </summary>
public sealed class WbixVersionContractTests
{
    private const string PageIdText = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task Load_V1File_WithFlatStrokes_MapsToStrokeItems()
    {
        string pageJson = $$"""
            {
              "id": "{{PageIdText}}",
              "strokes": [
                {
                  "points": [
                    { "position": { "x": 1.5, "y": 2.5 }, "pressure": 0.5 },
                    { "position": { "x": 3.5, "y": 4.5 }, "pressure": 1.0 }
                  ],
                  "colorRgba": { "x": 0.1, "y": 0.2, "z": 0.3, "w": 1.0 },
                  "baseSize": 4.0,
                  "enablePressure": true
                }
              ],
              "elements": []
            }
            """;

        using MemoryStream ms = CreateWbixStream(version: 1, pageJson);
        BoardWorkspaceSnapshot snapshot = await new WbixWorkspaceSerializer().LoadAsync(ms);

        BoardPageSnapshot page = Assert.Single(snapshot.Pages);
        InkItemSnapshot item = Assert.Single(page.Strokes);
        Assert.Equal(BoardInkItemCodec.StrokeKind, item.Kind);
        Assert.NotNull(item.Stroke);
        Assert.Equal(2, item.Stroke!.Points.Count);
        Assert.Equal(1.5f, item.Stroke.Points[0].Position.X, precision: 5);
        Assert.Equal(4.0f, item.Stroke.BaseSize, precision: 5);
    }

    [Fact]
    public async Task Load_V2File_WithWrappedStrokeItem_IsAccepted()
    {
        // v1/v2 reader 同样容忍包装形态：不允许出现「今日能读的文件改版后读不了」。
        string pageJson = $$"""
            {
              "id": "{{PageIdText}}",
              "strokes": [
                {
                  "kind": "stroke",
                  "stroke": {
                    "points": [ { "position": { "x": 7.0, "y": 8.0 }, "pressure": 1.0 } ],
                    "colorRgba": { "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 },
                    "baseSize": 2.0,
                    "enablePressure": false
                  }
                }
              ],
              "elements": []
            }
            """;

        using MemoryStream ms = CreateWbixStream(version: 2, pageJson);
        BoardWorkspaceSnapshot snapshot = await new WbixWorkspaceSerializer().LoadAsync(ms);

        BoardPageSnapshot page = Assert.Single(snapshot.Pages);
        InkItemSnapshot item = Assert.Single(page.Strokes);
        Assert.Equal(BoardInkItemCodec.StrokeKind, item.Kind);
        Assert.Equal(7.0f, item.Stroke!.Points[0].Position.X, precision: 5);
    }

    [Fact]
    public async Task Load_V3File_WithWrappedStrokeAndShape_MapsBoth()
    {
        string pageJson = $$"""
            {
              "id": "{{PageIdText}}",
              "strokes": [
                {
                  "kind": "stroke",
                  "stroke": {
                    "points": [ { "position": { "x": 1.0, "y": 1.0 }, "pressure": 1.0 } ],
                    "colorRgba": { "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 },
                    "baseSize": 2.0,
                    "enablePressure": false
                  }
                },
                {
                  "kind": "rect",
                  "shape": {
                    "start": { "x": 10.0, "y": 20.0 },
                    "end": { "x": 30.0, "y": 40.0 },
                    "colorRgba": { "x": 1.0, "y": 0.0, "z": 0.0, "w": 1.0 },
                    "width": 3.0
                  }
                }
              ],
              "elements": []
            }
            """;

        using MemoryStream ms = CreateWbixStream(version: 3, pageJson);
        BoardWorkspaceSnapshot snapshot = await new WbixWorkspaceSerializer().LoadAsync(ms);

        BoardPageSnapshot page = Assert.Single(snapshot.Pages);
        Assert.Equal(2, page.Strokes.Count);

        Assert.Equal(BoardInkItemCodec.StrokeKind, page.Strokes[0].Kind);
        Assert.NotNull(page.Strokes[0].Stroke);

        Assert.Equal("rect", page.Strokes[1].Kind);
        Assert.NotNull(page.Strokes[1].Shape);
        Assert.Equal(10.0f, page.Strokes[1].Shape!.Start.X, precision: 5);
        Assert.Equal(40.0f, page.Strokes[1].Shape!.End.Y, precision: 5);
        Assert.Equal(3.0f, page.Strokes[1].Shape!.Width, precision: 5);
    }

    [Fact]
    public async Task Load_V3File_WithMixedFlatAndWrappedItems_ReadsBoth()
    {
        // v3 的扁平兜底：混入的扁平条目按 kind=stroke 读取，不影响同页的包装条目。
        string pageJson = $$"""
            {
              "id": "{{PageIdText}}",
              "strokes": [
                {
                  "points": [ { "position": { "x": 5.0, "y": 6.0 }, "pressure": 1.0 } ],
                  "colorRgba": { "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 },
                  "baseSize": 2.0,
                  "enablePressure": false
                },
                {
                  "kind": "line",
                  "shape": {
                    "start": { "x": 0.0, "y": 0.0 },
                    "end": { "x": 1.0, "y": 1.0 },
                    "colorRgba": { "x": 0.0, "y": 1.0, "z": 0.0, "w": 1.0 },
                    "width": 1.5
                  }
                }
              ],
              "elements": []
            }
            """;

        using MemoryStream ms = CreateWbixStream(version: 3, pageJson);
        BoardWorkspaceSnapshot snapshot = await new WbixWorkspaceSerializer().LoadAsync(ms);

        BoardPageSnapshot page = Assert.Single(snapshot.Pages);
        Assert.Equal(2, page.Strokes.Count);
        Assert.Equal(BoardInkItemCodec.StrokeKind, page.Strokes[0].Kind);
        Assert.Equal(5.0f, page.Strokes[0].Stroke!.Points[0].Position.X, precision: 5);
        Assert.Equal("line", page.Strokes[1].Kind);
        Assert.NotNull(page.Strokes[1].Shape);
    }

    [Fact]
    public async Task Load_V4File_WithEmptyPages_ThrowsInvalidDataException()
    {
        // 版本分派发生在读取任何页之前：即使 pages 为空，非法版本也必须被拒绝，
        // 不能静默返回空工作区。
        using MemoryStream ms = CreateWbixStream(version: 4, pageJson: "{ }", pagesJson: "[]");

        var serializer = new WbixWorkspaceSerializer();
        await Assert.ThrowsAsync<InvalidDataException>(() => serializer.LoadAsync(ms));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Load_NonPositiveVersion_ThrowsInvalidDataException(int version)
    {
        using MemoryStream ms = CreateWbixStream(version, pageJson: "{ }", pagesJson: "[]");

        var serializer = new WbixWorkspaceSerializer();
        await Assert.ThrowsAsync<InvalidDataException>(() => serializer.LoadAsync(ms));
    }

    [Fact]
    public void LegacyFlatWarningGate_ReportsOnlyOnce()
    {
        // 「每文件告警一次」的实现契约：门闩首次进入返回 true，之后一律 false。
        var gate = new WbixLegacyFlatItemWarningGate();

        Assert.True(gate.TryEnter());
        Assert.False(gate.TryEnter());
        Assert.False(gate.TryEnter());
    }

    [Fact]
    public void Converter_WrappedMode_ReportsFlatItemAndKeepsData()
    {
        string json = """
            {
              "points": [ { "position": { "x": 2.0, "y": 3.0 }, "pressure": 1.0 } ],
              "colorRgba": { "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 },
              "baseSize": 2.0,
              "enablePressure": false
            }
            """;

        int reported = 0;
        JsonSerializerOptions options = CreateOptions(InkItemReadMode.Wrapped, () => reported++);

        InkItemSnapshot? item = JsonSerializer.Deserialize<InkItemSnapshot>(json, options);

        Assert.NotNull(item);
        Assert.Equal(1, reported);
        Assert.Equal(BoardInkItemCodec.StrokeKind, item!.Kind);
        Assert.Equal(2.0f, item.Stroke!.Points[0].Position.X, precision: 5);
    }

    [Fact]
    public void Converter_LegacyFlatMode_AcceptsWrappedItemWithoutReport()
    {
        string json = """
            {
              "kind": "stroke",
              "stroke": {
                "points": [ { "position": { "x": 9.0, "y": 8.0 }, "pressure": 1.0 } ],
                "colorRgba": { "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 },
                "baseSize": 2.0,
                "enablePressure": false
              }
            }
            """;

        int reported = 0;
        JsonSerializerOptions options = CreateOptions(InkItemReadMode.LegacyFlat, () => reported++);

        InkItemSnapshot? item = JsonSerializer.Deserialize<InkItemSnapshot>(json, options);

        Assert.NotNull(item);
        Assert.Equal(0, reported);
        Assert.Equal(9.0f, item!.Stroke!.Points[0].Position.X, precision: 5);
    }

    private static JsonSerializerOptions CreateOptions(InkItemReadMode readMode, Action onUnexpectedLegacyFlatItem)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            IncludeFields = true,
        };
        options.Converters.Add(new InkItemSnapshotJsonConverter(readMode, onUnexpectedLegacyFlatItem));
        return options;
    }

    private static MemoryStream CreateWbixStream(int version, string pageJson, string? pagesJson = null)
    {
        string pages = pagesJson
            ?? $"[ {{ \"id\": \"{PageIdText}\", \"index\": 0, \"path\": \"pages/page-000.json\" }} ]";

        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "manifest.json", $$"""
                {
                  "format": "wbix",
                  "version": {{version}},
                  "createdUtc": "2026-01-01T00:00:00Z",
                  "currentIndex": 0,
                  "pages": {{pages}},
                  "resources": []
                }
                """);

            WriteZipEntry(archive, "pages/page-000.json", pageJson);
        }

        ms.Position = 0;
        return ms;
    }

    private static void WriteZipEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using Stream s = entry.Open();
        using var writer = new StreamWriter(s, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: false);
        writer.Write(content);
    }
}
