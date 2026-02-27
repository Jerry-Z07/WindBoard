using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using WindBoard.Board.Elements;
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
                StrokeSnapshot es = expectedPage.Strokes[s];
                StrokeSnapshot a = actualPage.Strokes[s];

                Assert.Equal(es.EnablePressure, a.EnablePressure);
                Assert.Equal(es.BaseSize, a.BaseSize, precision: 5);
                Assert.Equal(es.ColorRgba.X, a.ColorRgba.X, precision: 5);
                Assert.Equal(es.ColorRgba.Y, a.ColorRgba.Y, precision: 5);
                Assert.Equal(es.ColorRgba.Z, a.ColorRgba.Z, precision: 5);
                Assert.Equal(es.ColorRgba.W, a.ColorRgba.W, precision: 5);

                Assert.Equal(es.Points.Count, a.Points.Count);
                for (int i = 0; i < es.Points.Count; i++)
                {
                    StrokePointSnapshot ep = es.Points[i];
                    StrokePointSnapshot ap = a.Points[i];

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
            Assert.Equal(2, doc.RootElement.GetProperty("version").GetInt32());
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
            Strokes: Array.Empty<StrokeSnapshot>(),
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

    private static BoardWorkspaceSnapshot CreateSampleSnapshot()
    {
        Guid page1 = Guid.NewGuid();
        Guid page2 = Guid.NewGuid();

        Guid textId = Guid.NewGuid();
        Guid linkId = Guid.NewGuid();
        Guid mediaId = Guid.NewGuid();
        Guid fileId = Guid.NewGuid();

        var stroke1 = new StrokeSnapshot(
            Points:
            [
                new StrokePointSnapshot(new Vector2(10.5f, 20.25f), 0.5f),
                new StrokePointSnapshot(new Vector2(12.0f, 24.0f), 0.8f),
            ],
            ColorRgba: new Vector4(0.1f, 0.2f, 0.3f, 1.0f),
            BaseSize: 3.25f,
            EnablePressure: true);

        var stroke2 = new StrokeSnapshot(
            Points:
            [
                new StrokePointSnapshot(new Vector2(-5.0f, 3.0f), 1.0f),
                new StrokePointSnapshot(new Vector2(0.0f, 3.0f), 1.0f),
                new StrokePointSnapshot(new Vector2(5.0f, 3.0f), 1.0f),
            ],
            ColorRgba: new Vector4(0.9f, 0.8f, 0.7f, 1.0f),
            BaseSize: 6.0f,
            EnablePressure: false);

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
