using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using WindBoard.Board.Elements;
using WindBoard.Board.Editing;
using WindBoard.Features.Import.Models;
using WindBoard.Features.Import.Services;
using Windows.Storage;
using Xunit;

namespace WindBoard.Tests.Features.Import;

public sealed class BoardImportServiceTests : IDisposable
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

    private async Task<StorageFile> CreateTempTextFileAsync(string ext, string content)
    {
        string path = GetTempFilePath(ext);
        File.WriteAllText(path, content);
        return await StorageFile.GetFileFromPathAsync(path);
    }

    private async Task<StorageFile> CreateTempEmptyFileAsync(string ext)
    {
        string path = GetTempFilePath(ext);
        File.WriteAllBytes(path, Array.Empty<byte>());
        return await StorageFile.GetFileFromPathAsync(path);
    }

    [Fact]
    public async Task ImportElementsAsync_WithTextContentAndLinks_CreatesTextAndLinkElements()
    {
        var workspace = new BoardWorkspace();

        var request = new ImportElementsRequest(
            ImageFiles: Array.Empty<StorageFile>(),
            MediaFiles: Array.Empty<StorageFile>(),
            TextFiles: Array.Empty<StorageFile>(),
            OtherFiles: Array.Empty<StorageFile>(),
            TextContent: "Hello",
            LinkLines: "example.com\nhttps://example.com\nftp://bad.example.com\n");

        IReadOnlyList<BoardElement> created = await BoardImportService.ImportElementsAsync(
            workspace,
            cameraWorld: Vector2.Zero,
            zoom: 1.0f,
            request);

        Assert.Equal(2, created.Count);
        Assert.IsType<BoardTextElement>(created[0]);
        Assert.IsType<BoardLinkElement>(created[1]);

        var doc = workspace.CurrentPage.Session.Document;
        Assert.Equal(2, doc.ElementsBelowInk.Count);
    }

    [Fact]
    public async Task ImportElementsAsync_WithInternetShortcut_CreatesLinkElement()
    {
        StorageFile urlFile = await CreateTempTextFileAsync(".url", "[InternetShortcut]\nURL=https://example.com\n");

        var workspace = new BoardWorkspace();
        var request = new ImportElementsRequest(
            ImageFiles: Array.Empty<StorageFile>(),
            MediaFiles: Array.Empty<StorageFile>(),
            TextFiles: new[] { urlFile },
            OtherFiles: Array.Empty<StorageFile>(),
            TextContent: null,
            LinkLines: null);

        IReadOnlyList<BoardElement> created = await BoardImportService.ImportElementsAsync(
            workspace,
            cameraWorld: new Vector2(100, 50),
            zoom: 1.0f,
            request);

        Assert.Single(created);
        var link = Assert.IsType<BoardLinkElement>(created[0]);
        Assert.Equal("https://example.com", link.Url);
    }

    [Fact]
    public async Task ImportElementsAsync_WithInvalidInternetShortcut_FallsBackToTextElement()
    {
        StorageFile urlFile = await CreateTempTextFileAsync(".url", "[InternetShortcut]\nFoo=Bar\n");

        var workspace = new BoardWorkspace();
        var request = new ImportElementsRequest(
            ImageFiles: Array.Empty<StorageFile>(),
            MediaFiles: Array.Empty<StorageFile>(),
            TextFiles: new[] { urlFile },
            OtherFiles: Array.Empty<StorageFile>(),
            TextContent: null,
            LinkLines: null);

        IReadOnlyList<BoardElement> created = await BoardImportService.ImportElementsAsync(
            workspace,
            cameraWorld: Vector2.Zero,
            zoom: 1.0f,
            request);

        Assert.Single(created);
        var text = Assert.IsType<BoardTextElement>(created[0]);
        Assert.Contains("Foo=Bar", text.Text);
    }

    [Fact]
    public async Task ImportElementsAsync_WhenTextFileMissing_DoesNotCrash_AndCreatesPlaceholderTextElement()
    {
        StorageFile textFile = await CreateTempEmptyFileAsync(".txt");

        // 模拟文件在导入前被外部删除：读取应失败，但导入不应整体崩溃。
        File.Delete(textFile.Path);

        var workspace = new BoardWorkspace();
        var request = new ImportElementsRequest(
            ImageFiles: Array.Empty<StorageFile>(),
            MediaFiles: Array.Empty<StorageFile>(),
            TextFiles: new[] { textFile },
            OtherFiles: Array.Empty<StorageFile>(),
            TextContent: null,
            LinkLines: null);

        IReadOnlyList<BoardElement> created = await BoardImportService.ImportElementsAsync(
            workspace,
            cameraWorld: Vector2.Zero,
            zoom: 1.0f,
            request);

        Assert.Single(created);
        var text = Assert.IsType<BoardTextElement>(created[0]);
        Assert.False(string.IsNullOrWhiteSpace(text.Text));
    }
}
