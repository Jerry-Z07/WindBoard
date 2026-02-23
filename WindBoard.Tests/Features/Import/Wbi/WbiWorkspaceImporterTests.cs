using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading.Tasks;
using WindBoard.Board.Elements;
using WindBoard.Features.Import.Wbi;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Input.Inking;
using Xunit;

namespace WindBoard.Tests.Features.Import.Wbi;

public sealed class WbiWorkspaceImporterTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (string file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
    }

    private string GetTempFilePath(string ext)
    {
        string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}{ext}");
        _tempFiles.Add(path);
        return path;
    }

    private static async Task WriteJsonEntryAsync<T>(ZipArchive archive, string entryName, T value)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using Stream s = entry.Open();
        await JsonSerializer.SerializeAsync(s, value, new JsonSerializerOptions { WriteIndented = true });
    }

    private static async Task WriteBytesEntryAsync(ZipArchive archive, string entryName, byte[] bytes)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using Stream s = entry.Open();
        await s.WriteAsync(bytes, 0, bytes.Length);
    }

    private async Task<string> CreateWbiFileAsync(WbiManifest manifest, IReadOnlyDictionary<string, WbiPageData> pages, IReadOnlyDictionary<string, byte[]>? extraEntries = null)
    {
        string filePath = GetTempFilePath(".wbi");

        await using (var fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
        {
            await WriteJsonEntryAsync(archive, "manifest.json", manifest);

            foreach ((string id, WbiPageData pageData) in pages)
            {
                await WriteJsonEntryAsync(archive, $"pages/{id}.json", pageData);
            }

            if (extraEntries is not null)
            {
                foreach ((string name, byte[] bytes) in extraEntries)
                {
                    await WriteBytesEntryAsync(archive, name, bytes);
                }
            }
        }

        return filePath;
    }

    [Fact]
    public async Task PreviewReader_WithValidFile_ReturnsPreview()
    {
        var manifest = new WbiManifest
        {
            Version = "1.0",
            MinCompatibleVersion = "1.0",
            CreatedAt = DateTime.UtcNow,
            IncludeImageAssets = false,
            PageCount = 1,
            Pages = new List<WbiPageRef> { new() { Id = "page_001", Number = 1 } },
        };

        var page = new WbiPageData { Number = 1 };
        string path = await CreateWbiFileAsync(manifest, new Dictionary<string, WbiPageData> { ["page_001"] = page });

        WbiPreviewReader.WbiPreview? preview = await WbiPreviewReader.TryReadAsync(path);

        Assert.NotNull(preview);
        Assert.Equal("1.0", preview!.Manifest.Version);
        Assert.Single(preview.Manifest.Pages);
    }

    [Fact]
    public async Task PreviewReader_WithMissingManifest_ReturnsNull()
    {
        string filePath = GetTempFilePath(".wbi");
        await using (var fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
        {
            // 空包：缺 manifest.json
            _ = archive.CreateEntry("pages/page_001.json");
        }

        WbiPreviewReader.WbiPreview? preview = await WbiPreviewReader.TryReadAsync(filePath);
        Assert.Null(preview);
    }

    [Fact]
    public async Task ImportAsync_WithMinimalFile_Succeeds()
    {
        var manifest = new WbiManifest
        {
            Version = "1.0",
            MinCompatibleVersion = "1.0",
            CreatedAt = DateTime.UtcNow,
            IncludeImageAssets = false,
            PageCount = 1,
            Pages = new List<WbiPageRef> { new() { Id = "page_001", Number = 1 } },
        };

        var page = new WbiPageData
        {
            Number = 1,
            StrokesFile = null,
            Attachments = new List<WbiAttachmentData>(),
        };

        string path = await CreateWbiFileAsync(manifest, new Dictionary<string, WbiPageData> { ["page_001"] = page });

        var importer = new WbiWorkspaceImporter();
        WbiWorkspaceImportResult result = await importer.ImportAsync(path);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Single(result.Pages);
    }

    [Fact]
    public async Task ImportAsync_MapsAttachmentsToElements_AndRespectsPinnedTopAndZIndex()
    {
        string missingImage = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.png");
        string missingVideo = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.mp4");

        var manifest = new WbiManifest
        {
            Version = "1.0",
            MinCompatibleVersion = "1.0",
            CreatedAt = DateTime.UtcNow,
            IncludeImageAssets = false,
            PageCount = 1,
            Pages = new List<WbiPageRef> { new() { Id = "page_001", Number = 1 } },
        };

        var page = new WbiPageData
        {
            Number = 1,
            Attachments = new List<WbiAttachmentData>
            {
                new()
                {
                    Type = "Text",
                    Text = "Hello",
                    X = 10,
                    Y = 20,
                    Width = 300,
                    Height = 120,
                    ZIndex = 5,
                    IsPinnedTop = false,
                },
                new()
                {
                    Type = "Link",
                    Url = "https://example.com",
                    X = 1,
                    Y = 2,
                    Width = 200,
                    Height = 80,
                    ZIndex = 1,
                    IsPinnedTop = false,
                },
                new()
                {
                    Type = "Image",
                    FilePath = missingImage,
                    X = 100,
                    Y = 200,
                    Width = 320,
                    Height = 180,
                    ZIndex = 0,
                    IsPinnedTop = true,
                },
                new()
                {
                    Type = "Video",
                    FilePath = missingVideo,
                    X = 110,
                    Y = 210,
                    Width = 360,
                    Height = 200,
                    ZIndex = 2,
                    IsPinnedTop = true,
                },
            },
        };

        string path = await CreateWbiFileAsync(manifest, new Dictionary<string, WbiPageData> { ["page_001"] = page });

        var importer = new WbiWorkspaceImporter();
        WbiWorkspaceImportResult result = await importer.ImportAsync(path);

        Assert.True(result.Success);
        Assert.Single(result.Pages);

        var doc = result.Pages[0].Session.Document;

        // below-ink：按 z_index 升序插入（1 -> 5）
        Assert.Equal(2, doc.ElementsBelowInk.Count);
        Assert.IsType<BoardLinkElement>(doc.ElementsBelowInk[0]);
        Assert.IsType<BoardTextElement>(doc.ElementsBelowInk[1]);

        // above-ink：按 z_index 升序插入（0 -> 2）
        Assert.Equal(2, doc.ElementsAboveInk.Count);
        Assert.IsType<BoardMediaElement>(doc.ElementsAboveInk[0]);
        Assert.IsType<BoardMediaElement>(doc.ElementsAboveInk[1]);

        Assert.Contains(result.MissingResources, s => s.Contains("图片文件不存在"));
        Assert.Contains(result.MissingResources, s => s.Contains("视频文件不存在"));
    }

    [Fact]
    public async Task ImportAsync_WithIsfStrokes_LoadsStrokes()
    {
        byte[] isfBytes = await CreateIsfBytesAsync();

        var manifest = new WbiManifest
        {
            Version = "1.0",
            MinCompatibleVersion = "1.0",
            CreatedAt = DateTime.UtcNow,
            IncludeImageAssets = false,
            PageCount = 1,
            Pages = new List<WbiPageRef> { new() { Id = "page_001", Number = 1 } },
        };

        var page = new WbiPageData
        {
            Number = 1,
            StrokesFile = "page_001.isf",
        };

        string path = await CreateWbiFileAsync(
            manifest,
            new Dictionary<string, WbiPageData> { ["page_001"] = page },
            extraEntries: new Dictionary<string, byte[]> { ["pages/page_001.isf"] = isfBytes });

        var importer = new WbiWorkspaceImporter();
        WbiWorkspaceImportResult result = await importer.ImportAsync(path);

        Assert.True(result.Success);
        Assert.Single(result.Pages);
        Assert.True(result.Pages[0].Session.Document.Strokes.Count > 0);
        Assert.True(result.Pages[0].Session.Document.Strokes[0].Points.Count >= 2);
    }

    private static async Task<byte[]> CreateIsfBytesAsync()
    {
        var attributes = new InkDrawingAttributes
        {
            Color = Color.FromArgb(255, 255, 0, 0),
            IgnorePressure = false,
            Size = new Size(4, 4),
        };

        var builder = new InkStrokeBuilder();
        builder.SetDefaultDrawingAttributes(attributes);

        InkStroke stroke = builder.CreateStroke(new[]
        {
            new Point(0, 0),
            new Point(10, 10),
        });

        var container = new InkStrokeContainer();
        container.AddStroke(stroke);

        using var mem = new InMemoryRandomAccessStream();
        await container.SaveAsync(mem.GetOutputStreamAt(0));
        mem.Seek(0);

        using Stream stream = mem.AsStreamForRead();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }
}
