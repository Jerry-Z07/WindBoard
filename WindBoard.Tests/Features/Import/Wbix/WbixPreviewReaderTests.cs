using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using WindBoard.Board.Persistence.Wbix;
using Xunit;

namespace WindBoard.Tests.Features.Import.Wbix;

public sealed class WbixPreviewReaderTests : IDisposable
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

    [Fact]
    public async Task TryReadAsync_WithMissingManifest_ReturnsNull()
    {
        string filePath = GetTempFilePath(".wbix");

        await using (var fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
        {
            _ = archive.CreateEntry("pages/page_001.json");
        }

        WbixPreviewReader.WbixPreview? preview = await WbixPreviewReader.TryReadAsync(filePath);
        Assert.Null(preview);
    }

    [Fact]
    public async Task TryReadAsync_WithMinimalManifest_ReturnsPreview()
    {
        string filePath = GetTempFilePath(".wbix");

        var manifest = new WbixManifest(
            Format: "wbix",
            Version: 1,
            CreatedUtc: DateTimeOffset.UtcNow,
            CurrentIndex: 0,
            Pages: new List<WbixManifestPage>
            {
                new(Guid.NewGuid(), 0, "pages/page_001.json"),
            },
            Resources: null);

        await using (var fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
        {
            await WriteJsonEntryAsync(archive, "manifest.json", manifest);
            _ = archive.CreateEntry("pages/page_001.json");
        }

        WbixPreviewReader.WbixPreview? preview = await WbixPreviewReader.TryReadAsync(filePath);

        Assert.NotNull(preview);
        Assert.Equal(1, preview!.Manifest.Version);
        Assert.Single(preview.Manifest.Pages);
        Assert.Null(preview.CoverPngBytes);
    }
}

