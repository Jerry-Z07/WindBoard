using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using WindBoard.Board.Elements;
using WindBoard.Board.Editing;
using WindBoard.Board.Items;
using WindBoard.Board.Persistence;
using WindBoard.Board.Persistence.Wbix;
using Xunit;

namespace WindBoard.Tests.Board.Persistence;

public sealed class WbixWorkspaceSerializerTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsSnapshot()
    {
        var snapshot = CreateSampleSnapshot();
        var serializer = new WbixWorkspaceSerializer();

        using var ms = new MemoryStream();
        await serializer.SaveAsync(snapshot, ms);

        ms.Position = 0;

        // 基础结构校验：必须存在 manifest 与 pages
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            Assert.NotNull(archive.GetEntry("manifest.json"));
            Assert.NotNull(archive.GetEntry("pages/page-000.json"));
            Assert.NotNull(archive.GetEntry("pages/page-001.json"));
        }

        ms.Position = 0;

        BoardWorkspaceSnapshot loaded = await serializer.LoadAsync(ms);

        Assert.Equal(snapshot.CurrentIndex, loaded.CurrentIndex);
        Assert.Equal(snapshot.Pages.Count, loaded.Pages.Count);

        for (int p = 0; p < snapshot.Pages.Count; p++)
        {
            BoardPageSnapshot expectedPage = snapshot.Pages[p];
            BoardPageSnapshot actualPage = loaded.Pages[p];

            Assert.Equal(expectedPage.Id, actualPage.Id);
            Assert.Equal(expectedPage.Strokes.Count, actualPage.Strokes.Count);

            for (int s = 0; s < expectedPage.Strokes.Count; s++)
            {
                InkItemSnapshot es = expectedPage.Strokes[s];
                InkItemSnapshot a = actualPage.Strokes[s];

                // v3 起：写侧固定输出 Kind 标识，读侧归一为 stroke。
                Assert.Equal(BoardInkItemCodec.StrokeKind, a.Kind);

                StrokeSnapshot esn = es.Stroke!;
                StrokeSnapshot an = a.Stroke!;

                Assert.Equal(esn.EnablePressure, an.EnablePressure);
                Assert.Equal(esn.BaseSize, an.BaseSize, precision: 5);
                Assert.Equal(esn.ColorRgba.X, an.ColorRgba.X, precision: 5);
                Assert.Equal(esn.ColorRgba.Y, an.ColorRgba.Y, precision: 5);
                Assert.Equal(esn.ColorRgba.Z, an.ColorRgba.Z, precision: 5);
                Assert.Equal(esn.ColorRgba.W, an.ColorRgba.W, precision: 5);

                Assert.Equal(esn.Points.Count, an.Points.Count);
                for (int i = 0; i < esn.Points.Count; i++)
                {
                    StrokePointSnapshot ep = esn.Points[i];
                    StrokePointSnapshot ap = an.Points[i];

                    Assert.Equal(ep.Position.X, ap.Position.X, precision: 5);
                    Assert.Equal(ep.Position.Y, ap.Position.Y, precision: 5);
                    Assert.Equal(ep.Pressure, ap.Pressure, precision: 5);
                }
            }

            IReadOnlyList<BoardElementSnapshot> expectedBelow = expectedPage.ElementsBelowInk ?? Array.Empty<BoardElementSnapshot>();
            IReadOnlyList<BoardElementSnapshot> actualBelow = actualPage.ElementsBelowInk ?? Array.Empty<BoardElementSnapshot>();
            Assert.Equal(expectedBelow.Count, actualBelow.Count);

            for (int i = 0; i < expectedBelow.Count; i++)
            {
                AssertElementEqual(expectedBelow[i], actualBelow[i]);
            }

            IReadOnlyList<BoardElementSnapshot> expectedAbove = expectedPage.ElementsAboveInk ?? Array.Empty<BoardElementSnapshot>();
            IReadOnlyList<BoardElementSnapshot> actualAbove = actualPage.ElementsAboveInk ?? Array.Empty<BoardElementSnapshot>();
            Assert.Equal(expectedAbove.Count, actualAbove.Count);

            for (int i = 0; i < expectedAbove.Count; i++)
            {
                AssertElementEqual(expectedAbove[i], actualAbove[i]);
            }
        }
    }

    [Fact]
    public async Task Save_WithResourceFile_WritesResourceAndManifestEntry()
    {
        var snapshot = CreateSampleSnapshot();
        var serializer = new WbixWorkspaceSerializer();

        var coverBytes = new byte[] { 1, 2, 3, 4, 5 };
        var resource = new WbixResourceFile(
            Id: "cover",
            Type: "image",
            Path: "assets/cover.png",
            ContentType: "image/png",
            Meta: new System.Collections.Generic.Dictionary<string, string> { ["role"] = "cover" },
            Bytes: coverBytes);

        using var ms = new MemoryStream();
        await serializer.SaveAsync(snapshot, ms, new[] { resource });

        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);

        ZipArchiveEntry? coverEntry = archive.GetEntry("assets/cover.png");
        Assert.NotNull(coverEntry);

        using (Stream s = coverEntry!.Open())
        using (var copy = new MemoryStream())
        {
            s.CopyTo(copy);
            Assert.Equal(coverBytes, copy.ToArray());
        }

        ZipArchiveEntry? manifestEntry = archive.GetEntry("manifest.json");
        Assert.NotNull(manifestEntry);

        using (Stream s = manifestEntry!.Open())
        using (var reader = new StreamReader(s))
        {
            string json = reader.ReadToEnd();
            using JsonDocument doc = JsonDocument.Parse(json);
            Assert.Equal(3, doc.RootElement.GetProperty("version").GetInt32());
            JsonElement resources = doc.RootElement.GetProperty("resources");

            bool found = false;
            foreach (JsonElement item in resources.EnumerateArray())
            {
                string? id = item.GetProperty("id").GetString();
                string? path = item.GetProperty("path").GetString();
                if (id == "cover" && path == "assets/cover.png")
                {
                    found = true;
                    break;
                }
            }

            Assert.True(found, "manifest.json 中应包含 assets/cover.png 资源条目。");
        }
    }

    [Fact]
    public async Task Save_WithEmbeddedImageResource_WritesResourceAndPageElementResourceId()
    {
        Guid pageId = Guid.NewGuid();
        Guid imageElementId = Guid.NewGuid();

        var page = new BoardPageSnapshot(
            pageId,
            Strokes: Array.Empty<InkItemSnapshot>(),
            ElementsBelowInk: Array.Empty<BoardElementSnapshot>(),
            ElementsAboveInk: new BoardElementSnapshot[]
            {
                new BoardMediaElementSnapshot(
                    imageElementId,
                    PositionWorld: new Vector2(10, 20),
                    SizeWorld: new Vector2(320, 180),
                    Order: 0,
                    Kind: BoardMediaKind.Image,
                    SourcePath: "C:\\should-not-be-written.png",
                    DisplayName: "test.png"),
            });

        var snapshot = new BoardWorkspaceSnapshot(new[] { page }, CurrentIndex: 0);
        var serializer = new WbixWorkspaceSerializer();

        var resource = new WbixResourceFile(
            Id: $"img-{imageElementId:D}",
            Type: "image",
            Path: $"assets/elements/{imageElementId:N}.png",
            ContentType: "image/png",
            Meta: new System.Collections.Generic.Dictionary<string, string>
            {
                ["role"] = "elementImage",
                ["elementId"] = imageElementId.ToString("D"),
                ["pageId"] = pageId.ToString("D"),
                ["pageIndex"] = "0",
            },
            Bytes: new byte[] { 1, 2, 3 });

        using var ms = new MemoryStream();
        await serializer.SaveAsync(snapshot, ms, new[] { resource });

        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);

        Assert.NotNull(archive.GetEntry($"assets/elements/{imageElementId:N}.png"));

        ZipArchiveEntry? pageEntry = archive.GetEntry("pages/page-000.json");
        Assert.NotNull(pageEntry);

        using (Stream s = pageEntry!.Open())
        using (var reader = new StreamReader(s))
        {
            string json = reader.ReadToEnd();
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement elements = doc.RootElement.GetProperty("elements");

            bool found = false;
            foreach (JsonElement el in elements.EnumerateArray())
            {
                if (!string.Equals(el.GetProperty("type").GetString(), "media", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                JsonElement data = el.GetProperty("data");
                string? idText = data.GetProperty("id").GetString();
                string? resourceId = data.GetProperty("resourceId").GetString();

                if (idText == imageElementId.ToString("D") && resourceId == $"img-{imageElementId:D}")
                {
                    // 内嵌图片不应落盘本地绝对路径。
                    Assert.True(data.TryGetProperty("sourcePath", out JsonElement sp) && sp.ValueKind == JsonValueKind.Null);
                    found = true;
                    break;
                }
            }

            Assert.True(found, "pages/page-000.json 中应包含 media 元素 resourceId。");
        }
    }

    [Fact]
    public async Task Load_V2File_WithoutKindField_MapsFlatStrokesToStrokeItems()
    {
        // v2 兼容路径一：旧文件 strokes 条目为"扁平"形态（无 kind/stroke 包装），
        // 读侧应包装为 Kind=stroke 的 InkItemSnapshot 且数据无损。
        Guid pageId = Guid.NewGuid();

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "manifest.json", $$"""
                {
                  "format": "wbix",
                  "version": 2,
                  "createdUtc": "2026-01-01T00:00:00Z",
                  "currentIndex": 0,
                  "pages": [ { "id": "{{pageId:D}}", "index": 0, "path": "pages/page-000.json" } ],
                  "resources": []
                }
                """);

            WriteZipEntry(archive, "pages/page-000.json", $$"""
                {
                  "id": "{{pageId:D}}",
                  "strokes": [
                    {
                      "points": [
                        { "position": { "x": 10.5, "y": 20.25 }, "pressure": 0.5 },
                        { "position": { "x": 12.0, "y": 24.0 }, "pressure": 0.8 }
                      ],
                      "colorRgba": { "x": 0.1, "y": 0.2, "z": 0.3, "w": 1.0 },
                      "baseSize": 3.25,
                      "enablePressure": true
                    }
                  ],
                  "elements": []
                }
                """);
        }

        ms.Position = 0;
        var serializer = new WbixWorkspaceSerializer();
        BoardWorkspaceSnapshot snapshot = await serializer.LoadAsync(ms);

        BoardPageSnapshot page = Assert.Single(snapshot.Pages);
        InkItemSnapshot item = Assert.Single(page.Strokes);
        Assert.Equal(BoardInkItemCodec.StrokeKind, item.Kind);

        StrokeSnapshot stroke = item.Stroke!;
        Assert.True(stroke.EnablePressure);
        Assert.Equal(3.25f, stroke.BaseSize, precision: 5);
        Assert.Equal(0.1f, stroke.ColorRgba.X, precision: 5);
        Assert.Equal(1.0f, stroke.ColorRgba.W, precision: 5);
        Assert.Equal(2, stroke.Points.Count);
        Assert.Equal(10.5f, stroke.Points[0].Position.X, precision: 5);
        Assert.Equal(20.25f, stroke.Points[0].Position.Y, precision: 5);
        Assert.Equal(0.5f, stroke.Points[0].Pressure, precision: 5);
        Assert.Equal(12.0f, stroke.Points[1].Position.X, precision: 5);
        Assert.Equal(0.8f, stroke.Points[1].Pressure, precision: 5);
    }

    [Fact]
    public async Task Load_V3File_WithExplicitNullKind_DefaultsToStroke()
    {
        // v3 兼容路径二：kind 显式为 null → 读侧归一为 stroke（不依赖 C# 初始化器缺省值）。
        Guid pageId = Guid.NewGuid();

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "manifest.json", $$"""
                {
                  "format": "wbix",
                  "version": 3,
                  "createdUtc": "2026-01-01T00:00:00Z",
                  "currentIndex": 0,
                  "pages": [ { "id": "{{pageId:D}}", "index": 0, "path": "pages/page-000.json" } ],
                  "resources": []
                }
                """);

            WriteZipEntry(archive, "pages/page-000.json", $$"""
                {
                  "id": "{{pageId:D}}",
                  "strokes": [
                    {
                      "kind": null,
                      "stroke": {
                        "points": [
                          { "position": { "x": 1.0, "y": 2.0 }, "pressure": 1.0 }
                        ],
                        "colorRgba": { "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 },
                        "baseSize": 5.0,
                        "enablePressure": false
                      }
                    }
                  ],
                  "elements": []
                }
                """);
        }

        ms.Position = 0;
        var serializer = new WbixWorkspaceSerializer();
        BoardWorkspaceSnapshot snapshot = await serializer.LoadAsync(ms);

        BoardPageSnapshot page = Assert.Single(snapshot.Pages);
        InkItemSnapshot item = Assert.Single(page.Strokes);
        Assert.Equal(BoardInkItemCodec.StrokeKind, item.Kind);

        StrokeSnapshot stroke = item.Stroke!;
        Assert.False(stroke.EnablePressure);
        Assert.Equal(5.0f, stroke.BaseSize, precision: 5);
        StrokePointSnapshot point = Assert.Single(stroke.Points);
        Assert.Equal(2.0f, point.Position.Y, precision: 5);
    }

    [Fact]
    public async Task SaveThenLoad_V2FlatFile_ReadSaveReadContentConsistent()
    {
        // PRD 验收：v2 旧文件 → 读取 → 保存（v3）→ 再读取，内容一致。
        Guid pageId = Guid.NewGuid();

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "manifest.json", $$"""
                {
                  "format": "wbix",
                  "version": 2,
                  "createdUtc": "2026-01-01T00:00:00Z",
                  "currentIndex": 0,
                  "pages": [ { "id": "{{pageId:D}}", "index": 0, "path": "pages/page-000.json" } ],
                  "resources": []
                }
                """);

            WriteZipEntry(archive, "pages/page-000.json", $$"""
                {
                  "id": "{{pageId:D}}",
                  "strokes": [
                    {
                      "points": [
                        { "position": { "x": -3.5, "y": 8.25 }, "pressure": 0.25 },
                        { "position": { "x": 40.0, "y": -1.5 }, "pressure": 1.0 },
                        { "position": { "x": 12.0, "y": 24.0 }, "pressure": 0.8 }
                      ],
                      "colorRgba": { "x": 0.9, "y": 0.5, "z": 0.1, "w": 0.75 },
                      "baseSize": 7.5,
                      "enablePressure": true
                    },
                    {
                      "points": [
                        { "position": { "x": 0.0, "y": 0.0 }, "pressure": 1.0 }
                      ],
                      "colorRgba": { "x": 0.0, "y": 1.0, "z": 0.0, "w": 1.0 },
                      "baseSize": 2.0,
                      "enablePressure": false
                    }
                  ],
                  "elements": []
                }
                """);
        }

        ms.Position = 0;
        var serializer = new WbixWorkspaceSerializer();
        BoardWorkspaceSnapshot first = await serializer.LoadAsync(ms);

        // 保存（输出 v3 格式）后重新读取。
        using var saved = new MemoryStream();
        await serializer.SaveAsync(first, saved);

        saved.Position = 0;
        BoardWorkspaceSnapshot second = await serializer.LoadAsync(saved);

        // 内容一致（JSON 形态由 v2 扁平升级为 v3 包装，数据必须无损）。
        BoardPageSnapshot firstPage = Assert.Single(first.Pages);
        BoardPageSnapshot secondPage = Assert.Single(second.Pages);
        Assert.Equal(firstPage.Id, secondPage.Id);
        Assert.Equal(firstPage.Strokes.Count, secondPage.Strokes.Count);

        for (int i = 0; i < firstPage.Strokes.Count; i++)
        {
            StrokeSnapshot expected = firstPage.Strokes[i].Stroke!;
            StrokeSnapshot actual = secondPage.Strokes[i].Stroke!;

            Assert.Equal(BoardInkItemCodec.StrokeKind, secondPage.Strokes[i].Kind);
            Assert.Equal(expected.EnablePressure, actual.EnablePressure);
            Assert.Equal(expected.BaseSize, actual.BaseSize, precision: 5);
            Assert.Equal(expected.ColorRgba.X, actual.ColorRgba.X, precision: 5);
            Assert.Equal(expected.ColorRgba.Y, actual.ColorRgba.Y, precision: 5);
            Assert.Equal(expected.ColorRgba.Z, actual.ColorRgba.Z, precision: 5);
            Assert.Equal(expected.ColorRgba.W, actual.ColorRgba.W, precision: 5);
            Assert.Equal(expected.Points.Count, actual.Points.Count);

            for (int p = 0; p < expected.Points.Count; p++)
            {
                Assert.Equal(expected.Points[p].Position.X, actual.Points[p].Position.X, precision: 5);
                Assert.Equal(expected.Points[p].Position.Y, actual.Points[p].Position.Y, precision: 5);
                Assert.Equal(expected.Points[p].Pressure, actual.Points[p].Pressure, precision: 5);
            }
        }
    }

    [Fact]
    public async Task Save_WritesKindDiscriminatedStrokeItems()
    {
        // v3 写侧格式固定：每个条目输出 { kind: "stroke", stroke: {...} } 包装形态。
        var snapshot = CreateSampleSnapshot();
        var serializer = new WbixWorkspaceSerializer();

        using var ms = new MemoryStream();
        await serializer.SaveAsync(snapshot, ms);

        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        ZipArchiveEntry? pageEntry = archive.GetEntry("pages/page-000.json");
        Assert.NotNull(pageEntry);

        using (Stream s = pageEntry!.Open())
        using (var reader = new StreamReader(s))
        {
            string json = reader.ReadToEnd();
            using JsonDocument doc = JsonDocument.Parse(json);

            JsonElement strokes = doc.RootElement.GetProperty("strokes");
            Assert.Equal(1, strokes.GetArrayLength());

            JsonElement item = strokes[0];
            Assert.Equal("stroke", item.GetProperty("kind").GetString());

            JsonElement stroke = item.GetProperty("stroke");
            Assert.Equal(2, stroke.GetProperty("points").GetArrayLength());
            Assert.Equal(3.25, stroke.GetProperty("baseSize").GetDouble(), precision: 5);
            Assert.True(stroke.GetProperty("enablePressure").GetBoolean());
        }
    }

    [Fact]
    public async Task Save_WritesKindDiscriminatedShapeItems()
    {
        // v3 写侧格式固定：形状条目输出 { kind, shape } 包装形态（design E，版本保持 3）。
        Guid pageId = Guid.NewGuid();
        var page = new BoardPageSnapshot(
            pageId,
            Strokes: new[]
            {
                new InkItemSnapshot
                {
                    Kind = BoardInkItemCodec.LineKind,
                    Shape = new ShapeSnapshot(
                        new Vector2(1.0f, 2.0f),
                        new Vector2(30.0f, 40.0f),
                        new Vector4(0.1f, 0.2f, 0.3f, 1.0f),
                        5.0f),
                },
            });

        var snapshot = new BoardWorkspaceSnapshot(new[] { page }, CurrentIndex: 0);
        var serializer = new WbixWorkspaceSerializer();

        using var ms = new MemoryStream();
        await serializer.SaveAsync(snapshot, ms);

        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        ZipArchiveEntry? pageEntry = archive.GetEntry("pages/page-000.json");
        Assert.NotNull(pageEntry);

        using (Stream s = pageEntry!.Open())
        using (var reader = new StreamReader(s))
        {
            string json = reader.ReadToEnd();
            using JsonDocument doc = JsonDocument.Parse(json);

            JsonElement strokes = doc.RootElement.GetProperty("strokes");
            Assert.Equal(1, strokes.GetArrayLength());

            JsonElement item = strokes[0];
            Assert.Equal("line", item.GetProperty("kind").GetString());

            JsonElement shape = item.GetProperty("shape");
            Assert.Equal(1.0, shape.GetProperty("start").GetProperty("x").GetDouble(), precision: 5);
            Assert.Equal(2.0, shape.GetProperty("start").GetProperty("y").GetDouble(), precision: 5);
            Assert.Equal(30.0, shape.GetProperty("end").GetProperty("x").GetDouble(), precision: 5);
            Assert.Equal(40.0, shape.GetProperty("end").GetProperty("y").GetDouble(), precision: 5);
            Assert.Equal(0.1, shape.GetProperty("colorRgba").GetProperty("x").GetDouble(), precision: 5);
            Assert.Equal(5.0, shape.GetProperty("width").GetDouble(), precision: 5);
        }
    }

    [Fact]
    public async Task Load_V3File_WithShapeItems_MapsShapePayload()
    {
        // v3 读路径：kind ∈ 形状集合 → 解析 shape 包装，数据逐值一致。
        Guid pageId = Guid.NewGuid();

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "manifest.json", $$"""
                {
                  "format": "wbix",
                  "version": 3,
                  "createdUtc": "2026-01-01T00:00:00Z",
                  "currentIndex": 0,
                  "pages": [ { "id": "{{pageId:D}}", "index": 0, "path": "pages/page-000.json" } ],
                  "resources": []
                }
                """);

            WriteZipEntry(archive, "pages/page-000.json", $$"""
                {
                  "id": "{{pageId:D}}",
                  "strokes": [
                    {
                      "kind": "rect",
                      "shape": {
                        "start": { "x": -5.5, "y": 10.25 },
                        "end": { "x": 100.0, "y": 60.0 },
                        "colorRgba": { "x": 0.9, "y": 0.8, "z": 0.7, "w": 0.6 },
                        "width": 4.5
                      }
                    }
                  ],
                  "elements": []
                }
                """);
        }

        ms.Position = 0;
        var serializer = new WbixWorkspaceSerializer();
        BoardWorkspaceSnapshot loaded = await serializer.LoadAsync(ms);

        BoardPageSnapshot page = Assert.Single(loaded.Pages);
        InkItemSnapshot item = Assert.Single(page.Strokes);
        Assert.Equal(BoardInkItemCodec.RectKind, item.Kind);
        Assert.Null(item.Stroke);
        Assert.NotNull(item.Shape);

        ShapeSnapshot shape = item.Shape!;
        AssertEx.Equal(-5.5f, shape.Start.X);
        AssertEx.Equal(10.25f, shape.Start.Y);
        AssertEx.Equal(100.0f, shape.End.X);
        AssertEx.Equal(60.0f, shape.End.Y);
        Assert.Equal(0.9f, shape.ColorRgba.X, precision: 5);
        Assert.Equal(0.6f, shape.ColorRgba.W, precision: 5);
        Assert.Equal(4.5f, shape.Width, precision: 5);
    }

    [Fact]
    public async Task SaveThenLoad_ShapeFile_ReadSaveReadContentConsistent()
    {
        // PRD 验收：含形状的 v3 文件 → 读取 → 保存 → 再读取，内容逐值一致。
        Guid pageId = Guid.NewGuid();

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "manifest.json", $$"""
                {
                  "format": "wbix",
                  "version": 3,
                  "createdUtc": "2026-01-01T00:00:00Z",
                  "currentIndex": 0,
                  "pages": [ { "id": "{{pageId:D}}", "index": 0, "path": "pages/page-000.json" } ],
                  "resources": []
                }
                """);

            WriteZipEntry(archive, "pages/page-000.json", $$"""
                {
                  "id": "{{pageId:D}}",
                  "strokes": [
                    {
                      "kind": "ellipse",
                      "shape": {
                        "start": { "x": 0.0, "y": 0.0 },
                        "end": { "x": 80.0, "y": 50.0 },
                        "colorRgba": { "x": 0.25, "y": 0.5, "z": 0.75, "w": 1.0 },
                        "width": 6.0
                      }
                    },
                    {
                      "kind": "arrow",
                      "shape": {
                        "start": { "x": -10.5, "y": 3.25 },
                        "end": { "x": 42.0, "y": -8.0 },
                        "colorRgba": { "x": 1.0, "y": 0.0, "z": 0.0, "w": 0.9 },
                        "width": 2.25
                      }
                    },
                    {
                      "points": [
                        { "position": { "x": 1.0, "y": 2.0 }, "pressure": 1.0 }
                      ],
                      "colorRgba": { "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 },
                      "baseSize": 3.0,
                      "enablePressure": false
                    }
                  ],
                  "elements": []
                }
                """);
        }

        ms.Position = 0;
        var serializer = new WbixWorkspaceSerializer();
        BoardWorkspaceSnapshot first = await serializer.LoadAsync(ms);

        using var saved = new MemoryStream();
        await serializer.SaveAsync(first, saved);

        saved.Position = 0;
        BoardWorkspaceSnapshot second = await serializer.LoadAsync(saved);

        BoardPageSnapshot firstPage = Assert.Single(first.Pages);
        BoardPageSnapshot secondPage = Assert.Single(second.Pages);
        Assert.Equal(firstPage.Strokes.Count, secondPage.Strokes.Count);

        for (int i = 0; i < firstPage.Strokes.Count; i++)
        {
            InkItemSnapshot expected = firstPage.Strokes[i];
            InkItemSnapshot actual = secondPage.Strokes[i];

            Assert.Equal(expected.Kind, actual.Kind);

            if (expected.Shape is not null)
            {
                Assert.NotNull(actual.Shape);
                ShapeSnapshot es = expected.Shape;
                ShapeSnapshot a = actual.Shape!;
                AssertEx.Equal(es.Start.X, a.Start.X);
                AssertEx.Equal(es.Start.Y, a.Start.Y);
                AssertEx.Equal(es.End.X, a.End.X);
                AssertEx.Equal(es.End.Y, a.End.Y);
                Assert.Equal(es.ColorRgba.X, a.ColorRgba.X, precision: 5);
                Assert.Equal(es.ColorRgba.Y, a.ColorRgba.Y, precision: 5);
                Assert.Equal(es.ColorRgba.Z, a.ColorRgba.Z, precision: 5);
                Assert.Equal(es.ColorRgba.W, a.ColorRgba.W, precision: 5);
                Assert.Equal(es.Width, a.Width, precision: 5);
            }
            else
            {
                Assert.NotNull(actual.Stroke);
                Assert.NotNull(actual.Stroke!.Points);
            }
        }
    }

    [Fact]
    public async Task Load_V3File_WithUnknownShapeKind_SkipsSingleItem()
    {
        // 未知 kind（未来版本形状）：跳过该条目不阻断加载（容错约定）。
        Guid pageId = Guid.NewGuid();

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "manifest.json", $$"""
                {
                  "format": "wbix",
                  "version": 3,
                  "createdUtc": "2026-01-01T00:00:00Z",
                  "currentIndex": 0,
                  "pages": [ { "id": "{{pageId:D}}", "index": 0, "path": "pages/page-000.json" } ],
                  "resources": []
                }
                """);

            WriteZipEntry(archive, "pages/page-000.json", $$"""
                {
                  "id": "{{pageId:D}}",
                  "strokes": [
                    {
                      "kind": "hexagon",
                      "shape": {
                        "start": { "x": 0.0, "y": 0.0 },
                        "end": { "x": 10.0, "y": 10.0 },
                        "colorRgba": { "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 },
                        "width": 3.0
                      }
                    },
                    {
                      "kind": "line",
                      "shape": {
                        "start": { "x": 0.0, "y": 0.0 },
                        "end": { "x": 20.0, "y": 0.0 },
                        "colorRgba": { "x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0 },
                        "width": 3.0
                      }
                    }
                  ],
                  "elements": []
                }
                """);
        }

        ms.Position = 0;
        var serializer = new WbixWorkspaceSerializer();
        BoardWorkspaceSnapshot loaded = await serializer.LoadAsync(ms);

        // 快照层：转换器容错，未知 kind 条目原样保留（kind 为字符串透传）。
        BoardPageSnapshot page = Assert.Single(loaded.Pages);
        Assert.Equal(2, page.Strokes.Count);

        // 域重建层：未知 kind 条目被跳过（Codec 约定），已知形状正常重建。
        IReadOnlyList<BoardPage> pages = BoardWorkspaceSnapshotApplier.CreatePages(loaded);
        BoardPage appliedPage = Assert.Single(pages);
        Assert.Single(appliedPage.Session.Document.InkItems);
        var shape = Assert.IsType<BoardShape>(appliedPage.Session.Document.InkItems[0]);
        Assert.Equal(BoardShapeKind.Line, shape.Kind);
    }

    private static void WriteZipEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using Stream s = entry.Open();
        using var writer = new StreamWriter(s, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: false);
        writer.Write(content);
    }

    private static BoardWorkspaceSnapshot CreateSampleSnapshot()
    {
        Guid page1 = Guid.NewGuid();
        Guid page2 = Guid.NewGuid();

        Guid textId = Guid.NewGuid();
        Guid linkId = Guid.NewGuid();
        Guid mediaId = Guid.NewGuid();
        Guid fileId = Guid.NewGuid();

        var stroke1 = new InkItemSnapshot
        {
            Stroke = new StrokeSnapshot(
                Points:
                [
                    new StrokePointSnapshot(new Vector2(10.5f, 20.25f), 0.5f),
                    new StrokePointSnapshot(new Vector2(12.0f, 24.0f), 0.8f),
                ],
                ColorRgba: new Vector4(0.1f, 0.2f, 0.3f, 1.0f),
                BaseSize: 3.25f,
                EnablePressure: true),
        };

        var stroke2 = new InkItemSnapshot
        {
            Stroke = new StrokeSnapshot(
                Points:
                [
                    new StrokePointSnapshot(new Vector2(-5.0f, 3.0f), 1.0f),
                    new StrokePointSnapshot(new Vector2(0.0f, 3.0f), 1.0f),
                    new StrokePointSnapshot(new Vector2(5.0f, 3.0f), 1.0f),
                ],
                ColorRgba: new Vector4(0.9f, 0.8f, 0.7f, 1.0f),
                BaseSize: 6.0f,
                EnablePressure: false),
        };

        var pages = new[]
        {
            new BoardPageSnapshot(
                page1,
                Strokes: new[] { stroke1 },
                ElementsBelowInk: new BoardElementSnapshot[]
                {
                    new BoardTextElementSnapshot(textId, new Vector2(10, 20), new Vector2(300, 120), Order: 0, Text: "Hello"),
                    new BoardLinkElementSnapshot(linkId, new Vector2(1, 2), new Vector2(200, 80), Order: 1, Url: "https://example.com", Title: null),
                },
                ElementsAboveInk: new BoardElementSnapshot[]
                {
                    new BoardMediaElementSnapshot(mediaId, new Vector2(100, 200), new Vector2(360, 200), Order: 0, Kind: BoardMediaKind.Video, SourcePath: "C:\\video.mp4", DisplayName: "video.mp4"),
                    new BoardFileElementSnapshot(fileId, new Vector2(110, 210), new Vector2(420, 240), Order: 1, SourcePath: "C:\\file.pdf", DisplayName: "file.pdf"),
                }),

            new BoardPageSnapshot(page2, new[] { stroke2 }, ElementsBelowInk: Array.Empty<BoardElementSnapshot>(), ElementsAboveInk: Array.Empty<BoardElementSnapshot>()),
        };

        return new BoardWorkspaceSnapshot(pages, CurrentIndex: 1);
    }

    private static void AssertElementEqual(BoardElementSnapshot expected, BoardElementSnapshot actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.Order, actual.Order);

        AssertVector2Equal(expected.PositionWorld, actual.PositionWorld);
        AssertVector2Equal(expected.SizeWorld, actual.SizeWorld);

        switch (expected)
        {
            case BoardTextElementSnapshot et when actual is BoardTextElementSnapshot at:
                Assert.Equal(et.Text, at.Text);
                return;

            case BoardLinkElementSnapshot el when actual is BoardLinkElementSnapshot al:
                Assert.Equal(el.Url, al.Url);
                Assert.Equal(el.Title, al.Title);
                return;

            case BoardMediaElementSnapshot em when actual is BoardMediaElementSnapshot am:
                Assert.Equal(em.Kind, am.Kind);
                Assert.Equal(em.SourcePath, am.SourcePath);
                Assert.Equal(em.DisplayName, am.DisplayName);
                return;

            case BoardFileElementSnapshot ef when actual is BoardFileElementSnapshot af:
                Assert.Equal(ef.SourcePath, af.SourcePath);
                Assert.Equal(ef.DisplayName, af.DisplayName);
                return;
        }

        throw new InvalidOperationException("未知元素快照类型。");
    }

    private static void AssertVector2Equal(Vector2 expected, Vector2 actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 5);
        Assert.Equal(expected.Y, actual.Y, precision: 5);
    }
}
