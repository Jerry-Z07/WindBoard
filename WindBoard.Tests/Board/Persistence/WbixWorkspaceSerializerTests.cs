using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
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

    private static BoardWorkspaceSnapshot CreateSampleSnapshot()
    {
        Guid page1 = Guid.NewGuid();
        Guid page2 = Guid.NewGuid();

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
            new BoardPageSnapshot(page1, new[] { stroke1 }),
            new BoardPageSnapshot(page2, new[] { stroke2 }),
        };

        return new BoardWorkspaceSnapshot(pages, CurrentIndex: 1);
    }
}
