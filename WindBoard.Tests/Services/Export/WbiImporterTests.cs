using System.IO;
using System.Windows.Ink;
using WindBoard.Models.Export;
using WindBoard.Services.Export;
using Xunit;
using static WindBoard.Tests.TestHelpers.InkTestHelpers;

namespace WindBoard.Tests.Services.Export;

public sealed class WbiImporterTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
        foreach (var dir in _tempDirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    private string GetTempFilePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.wbi");
        _tempFiles.Add(path);
        return path;
    }

    private string GetTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_import_{Guid.NewGuid():N}");
        _tempDirs.Add(path);
        return path;
    }

    private async Task<string> CreateTestWbiFile(List<BoardPage> pages, WbiExportOptions? options = null)
    {
        var exporter = new WbiExporter();
        var filePath = GetTempFilePath();
        await exporter.ExportAsync(pages, filePath, options ?? new WbiExportOptions());
        return filePath;
    }

    [StaFact]
    public async Task ImportAsync_WithValidFile_ReturnsSuccessResult()
    {
        var originalPage = new BoardPage { Number = 1, Strokes = new StrokeCollection() };
        originalPage.Strokes.Add(CreateStroke(100, 100, 200, 200));
        var filePath = await CreateTestWbiFile(new List<BoardPage> { originalPage });

        var importer = new WbiImporter();
        var result = await importer.ImportAsync(filePath);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Single(result.Pages);
    }

    [StaFact]
    public async Task ImportAsync_RestoresStrokes()
    {
        var originalPage = new BoardPage { Number = 1, Strokes = new StrokeCollection() };
        originalPage.Strokes.Add(CreateStroke(50, 50, 150, 150));
        originalPage.Strokes.Add(CreateStroke(200, 200, 300, 300));
        var filePath = await CreateTestWbiFile(new List<BoardPage> { originalPage });

        var importer = new WbiImporter();
        var result = await importer.ImportAsync(filePath);

        Assert.True(result.Success);
        Assert.Equal(2, result.Pages[0].Strokes.Count);
    }

    [StaFact]
    public async Task ImportAsync_RestoresViewState()
    {
        var originalPage = new BoardPage
        {
            Number = 1,
            Strokes = new StrokeCollection(),
            CanvasWidth = 8000,
            CanvasHeight = 6000,
            Zoom = 2.5,
            PanX = -1000,
            PanY = -800
        };
        originalPage.Strokes.Add(CreateStroke(0, 0, 10, 10));
        var filePath = await CreateTestWbiFile(new List<BoardPage> { originalPage });

        var importer = new WbiImporter();
        var result = await importer.ImportAsync(filePath);

        Assert.True(result.Success);
        var page = result.Pages[0];
        Assert.Equal(8000, page.CanvasWidth);
        Assert.Equal(6000, page.CanvasHeight);
        Assert.Equal(2.5, page.Zoom);
        Assert.Equal(-1000, page.PanX);
        Assert.Equal(-800, page.PanY);
    }

    [StaFact]
    public async Task ImportAsync_RestoresTextAttachment()
    {
        var originalPage = new BoardPage { Number = 1, Strokes = new StrokeCollection() };
        originalPage.Attachments.Add(new BoardAttachment
        {
            Type = BoardAttachmentType.Text,
            X = 100,
            Y = 200,
            Width = 300,
            Height = 150,
            Text = "Test Content",
            ZIndex = 3,
            IsPinnedTop = true
        });
        var filePath = await CreateTestWbiFile(new List<BoardPage> { originalPage });

        var importer = new WbiImporter();
        var result = await importer.ImportAsync(filePath);

        Assert.True(result.Success);
        Assert.Single(result.Pages[0].Attachments);
        var att = result.Pages[0].Attachments[0];
        Assert.Equal(BoardAttachmentType.Text, att.Type);
        Assert.Equal(100, att.X);
        Assert.Equal(200, att.Y);
        Assert.Equal(300, att.Width);
        Assert.Equal(150, att.Height);
        Assert.Equal("Test Content", att.Text);
        Assert.Equal(3, att.ZIndex);
        Assert.True(att.IsPinnedTop);
    }

    [StaFact]
    public async Task ImportAsync_RestoresLinkAttachment()
    {
        var originalPage = new BoardPage { Number = 1, Strokes = new StrokeCollection() };
        originalPage.Attachments.Add(new BoardAttachment
        {
            Type = BoardAttachmentType.Link,
            X = 50,
            Y = 50,
            Url = "https://example.com/test"
        });
        var filePath = await CreateTestWbiFile(new List<BoardPage> { originalPage });

        var importer = new WbiImporter();
        var result = await importer.ImportAsync(filePath);

        Assert.True(result.Success);
        var att = result.Pages[0].Attachments[0];
        Assert.Equal(BoardAttachmentType.Link, att.Type);
        Assert.Equal("https://example.com/test", att.Url);
    }

    [StaFact]
    public async Task ImportAsync_WithMultiplePages_RestoresAll()
    {
        var pages = new List<BoardPage>
        {
            new BoardPage { Number = 1, Strokes = new StrokeCollection(), Zoom = 1.0 },
            new BoardPage { Number = 2, Strokes = new StrokeCollection(), Zoom = 1.5 },
            new BoardPage { Number = 3, Strokes = new StrokeCollection(), Zoom = 2.0 }
        };
        foreach (var p in pages) p.Strokes.Add(CreateStroke(0, 0, 100, 100));
        var filePath = await CreateTestWbiFile(pages);

        var importer = new WbiImporter();
        var result = await importer.ImportAsync(filePath);

        Assert.True(result.Success);
        Assert.Equal(3, result.Pages.Count);
        Assert.Equal(1.0, result.Pages[0].Zoom);
        Assert.Equal(1.5, result.Pages[1].Zoom);
        Assert.Equal(2.0, result.Pages[2].Zoom);
    }

    [StaFact]
    public async Task ImportAsync_WithNonExistentFile_ReturnsFailure()
    {
        var importer = new WbiImporter();
        var result = await importer.ImportAsync("/nonexistent/path/file.wbi");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("不存在", result.ErrorMessage);
    }

    [StaFact]
    public async Task ImportAsync_ReportsProgress()
    {
        var pages = new List<BoardPage>
        {
            new BoardPage { Number = 1, Strokes = new StrokeCollection() },
            new BoardPage { Number = 2, Strokes = new StrokeCollection() }
        };
        foreach (var p in pages) p.Strokes.Add(CreateStroke(0, 0, 10, 10));
        var filePath = await CreateTestWbiFile(pages);

        var importer = new WbiImporter();
        var progressReports = new List<ExportProgress>();
        var progress = new Progress<ExportProgress>(p => progressReports.Add(p));

        await importer.ImportAsync(filePath, null, progress);

        // Give Progress<T> time to invoke callbacks
        await Task.Delay(100);

        Assert.True(progressReports.Count > 0);
    }

    [StaFact]
    public async Task GetManifest_ReturnsManifestInfo()
    {
        var pages = new List<BoardPage>
        {
            new BoardPage { Number = 1, Strokes = new StrokeCollection() },
            new BoardPage { Number = 2, Strokes = new StrokeCollection() }
        };
        foreach (var p in pages) p.Strokes.Add(CreateStroke(0, 0, 10, 10));
        var filePath = await CreateTestWbiFile(pages, new WbiExportOptions { IncludeImageAssets = true });

        var importer = new WbiImporter();
        var manifest = importer.GetManifest(filePath);

        Assert.NotNull(manifest);
        Assert.Equal("1.0", manifest!.Version);
        Assert.Equal(2, manifest.PageCount);
        Assert.True(manifest.IncludeImageAssets);
    }

    [StaFact]
    public void GetManifest_WithInvalidFile_ReturnsNull()
    {
        var importer = new WbiImporter();
        var manifest = importer.GetManifest("/nonexistent/file.wbi");

        Assert.Null(manifest);
    }

    [StaFact]
    public async Task RoundTrip_PreservesAllData()
    {
        // Create complex page
        var originalPage = new BoardPage
        {
            Number = 1,
            Strokes = new StrokeCollection(),
            CanvasWidth = 10000,
            CanvasHeight = 8000,
            Zoom = 1.75,
            PanX = -250,
            PanY = -150
        };

        // Add multiple strokes
        originalPage.Strokes.Add(CreateStroke(100, 100, 200, 200));
        originalPage.Strokes.Add(CreateStroke(300, 300, 400, 400));
        originalPage.Strokes.Add(CreateStroke(500, 500, 600, 600));

        // Add various attachments
        originalPage.Attachments.Add(new BoardAttachment
        {
            Type = BoardAttachmentType.Text,
            X = 100, Y = 100, Width = 200, Height = 100,
            Text = "Test Text",
            ZIndex = 1
        });
        originalPage.Attachments.Add(new BoardAttachment
        {
            Type = BoardAttachmentType.Link,
            X = 400, Y = 100, Width = 150, Height = 80,
            Url = "https://test.com",
            ZIndex = 2
        });

        // Export
        var filePath = await CreateTestWbiFile(new List<BoardPage> { originalPage });

        // Import
        var importer = new WbiImporter();
        var result = await importer.ImportAsync(filePath);

        // Verify
        Assert.True(result.Success);
        Assert.Single(result.Pages);

        var importedPage = result.Pages[0];
        Assert.Equal(originalPage.CanvasWidth, importedPage.CanvasWidth);
        Assert.Equal(originalPage.CanvasHeight, importedPage.CanvasHeight);
        Assert.Equal(originalPage.Zoom, importedPage.Zoom);
        Assert.Equal(originalPage.PanX, importedPage.PanX);
        Assert.Equal(originalPage.PanY, importedPage.PanY);
        Assert.Equal(originalPage.Strokes.Count, importedPage.Strokes.Count);
        Assert.Equal(originalPage.Attachments.Count, importedPage.Attachments.Count);
    }
}
