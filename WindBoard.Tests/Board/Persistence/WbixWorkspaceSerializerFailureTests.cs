using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using WindBoard.Board.Elements;
using WindBoard.Board.Persistence;
using WindBoard.Board.Persistence.Wbix;
using Xunit;

namespace WindBoard.Tests.Board.Persistence;

public sealed class WbixWorkspaceSerializerFailureTests
{
    [Fact]
    public async Task LoadAsync_ThrowsInvalidDataException_WhenManifestMissing()
    {
        var serializer = new WbixWorkspaceSerializer();

        using var ms = new MemoryStream();
        using (var _ = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
        }

        ms.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => serializer.LoadAsync(ms));
    }

    [Fact]
    public async Task LoadAsync_ThrowsInvalidDataException_WhenVersionUnsupported()
    {
        var serializer = new WbixWorkspaceSerializer();

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            const string manifestJson = """
                {
                  "format": "wbix",
                  "version": 999,
                  "createdUtc": "2026-01-01T00:00:00Z",
                  "currentIndex": 0,
                  "pages": [],
                  "resources": []
                }
                """;

            WriteZipEntry(archive, "manifest.json", manifestJson);
        }

        ms.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => serializer.LoadAsync(ms));
    }

    [Fact]
    public async Task LoadAsync_ThrowsInvalidDataException_WhenPagePathIsUnsafe()
    {
        var serializer = new WbixWorkspaceSerializer();

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Guid pageId = Guid.NewGuid();
            string manifestJson = $$"""
                {
                  "format": "wbix",
                  "version": 2,
                  "createdUtc": "2026-01-01T00:00:00Z",
                  "currentIndex": 0,
                  "pages": [
                    { "id": "{{pageId:D}}", "index": 0, "path": "../page.json" }
                  ],
                  "resources": []
                }
                """;

            WriteZipEntry(archive, "manifest.json", manifestJson);
        }

        ms.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => serializer.LoadAsync(ms));
    }

    [Fact]
    public async Task LoadAsync_IgnoresUnsafeResourcePath_AndMediaSourcePathFallsBackToEmpty()
    {
        var serializer = new WbixWorkspaceSerializer();

        Guid pageId = Guid.NewGuid();
        Guid mediaId = Guid.NewGuid();

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            string manifestJson = $$"""
                {
                  "format": "wbix",
                  "version": 2,
                  "createdUtc": "2026-01-01T00:00:00Z",
                  "currentIndex": 0,
                  "pages": [
                    { "id": "{{pageId:D}}", "index": 0, "path": "pages/page-000.json" }
                  ],
                  "resources": [
                    { "id": "r1", "type": "image", "path": "../evil.png", "contentType": "image/png", "meta": {} }
                  ]
                }
                """;

            string pageJson = $$"""
                {
                  "id": "{{pageId:D}}",
                  "strokes": [],
                  "elements": [
                    {
                      "type": "media",
                      "data": {
                        "id": "{{mediaId:D}}",
                        "layer": "aboveInk",
                        "positionWorld": { "x": 0, "y": 0 },
                        "sizeWorld": { "x": 320, "y": 180 },
                        "order": 0,
                        "kind": "image",
                        "sourcePath": null,
                        "displayName": "",
                        "resourceId": "r1"
                      }
                    }
                  ]
                }
                """;

            WriteZipEntry(archive, "manifest.json", manifestJson);
            WriteZipEntry(archive, "pages/page-000.json", pageJson);
        }

        ms.Position = 0;
        BoardWorkspaceSnapshot snapshot = await serializer.LoadAsync(ms);

        BoardPageSnapshot page = Assert.Single(snapshot.Pages);
        BoardElementSnapshot element = Assert.Single(page.ElementsAboveInk ?? Array.Empty<BoardElementSnapshot>());

        var media = Assert.IsType<BoardMediaElementSnapshot>(element);
        Assert.Equal(BoardMediaKind.Image, media.Kind);
        Assert.Equal(string.Empty, media.SourcePath);
    }

    private static void WriteZipEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using Stream s = entry.Open();
        using var writer = new StreamWriter(s, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: false);
        writer.Write(content);
    }
}
