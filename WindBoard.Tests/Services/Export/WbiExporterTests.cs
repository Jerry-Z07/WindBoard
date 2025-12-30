using System.IO;
using System.IO.Compression;
using System.Windows.Ink;
using WindBoard.Models.Export;
using WindBoard.Models.Wbi;
using WindBoard.Services.Export;
using Newtonsoft.Json;
using Xunit;
using static WindBoard.Tests.TestHelpers.InkTestHelpers;

namespace WindBoard.Tests.Services.Export;

public sealed class WbiExporterTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
    }

    private string GetTempFilePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.wbi");
        _tempFiles.Add(path);
        return path;
    }

    [StaFact]
    public async Task ExportAsync_WithSinglePage_CreatesValidZipFile()
    {
        var exporter = new WbiExporter();
        var pages = new List<BoardPage>
        {
            new BoardPage
            {
                Number = 1,
                Strokes = new StrokeCollection(),
                CanvasWidth = 8000,
                CanvasHeight = 8000
            }
        };
        pages[0].Strokes.Add(CreateStroke(100, 100, 200, 200));

        var filePath = GetTempFilePath();
        var options = new WbiExportOptions
        {
            CompressionLevel = WbiCompressionLevel.Standard,
            IncludeImageAssets = false
        };

        await exporter.ExportAsync(pages, filePath, options);

        Assert.True(File.Exists(filePath));
        using var archive = ZipFile.OpenRead(filePath);
        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.NotNull(archive.GetEntry("pages/page_001.json"));
        Assert.NotNull(archive.GetEntry("pages/page_001.isf"));
    }

    [StaFact]
    public async Task ExportAsync_WithMultiplePages_CreatesEntriesForAllPages()
    {
        var exporter = new WbiExporter();
        var pages = new List<BoardPage>
        {
            new BoardPage { Number = 1, Strokes = new StrokeCollection() },
            new BoardPage { Number = 2, Strokes = new StrokeCollection() },
            new BoardPage { Number = 3, Strokes = new StrokeCollection() }
        };

        foreach (var page in pages)
        {
            page.Strokes.Add(CreateStroke(0, 0, 50, 50));
        }

        var filePath = GetTempFilePath();
        var options = new WbiExportOptions();

        await exporter.ExportAsync(pages, filePath, options);

        using var archive = ZipFile.OpenRead(filePath);
        Assert.NotNull(archive.GetEntry("pages/page_001.json"));
        Assert.NotNull(archive.GetEntry("pages/page_002.json"));
        Assert.NotNull(archive.GetEntry("pages/page_003.json"));
    }

    [StaFact]
    public async Task ExportAsync_ManifestContainsCorrectMetadata()
    {
        var exporter = new WbiExporter();
        var pages = new List<BoardPage>
        {
            new BoardPage { Number = 1, Strokes = new StrokeCollection() },
            new BoardPage { Number = 2, Strokes = new StrokeCollection() }
        };

        var filePath = GetTempFilePath();
        var options = new WbiExportOptions { IncludeImageAssets = true };

        await exporter.ExportAsync(pages, filePath, options);

        using var archive = ZipFile.OpenRead(filePath);
        var manifestEntry = archive.GetEntry("manifest.json");
        Assert.NotNull(manifestEntry);

        using var reader = new StreamReader(manifestEntry!.Open());
        var json = reader.ReadToEnd();
        var manifest = JsonConvert.DeserializeObject<WbiManifest>(json);

        Assert.NotNull(manifest);
        Assert.Equal("1.0", manifest!.Version);
        Assert.Equal(2, manifest.PageCount);
        Assert.True(manifest.IncludeImageAssets);
        Assert.Equal(2, manifest.Pages.Count);
    }

    [StaFact]
    public async Task ExportAsync_PageDataContainsViewState()
    {
        var exporter = new WbiExporter();
        var page = new BoardPage
        {
            Number = 1,
            Strokes = new StrokeCollection(),
            CanvasWidth = 8000,
            CanvasHeight = 6000,
            Zoom = 1.5,
            PanX = -500,
            PanY = -300
        };
        page.Strokes.Add(CreateStroke(0, 0, 100, 100));

        var filePath = GetTempFilePath();
        await exporter.ExportAsync(new[] { page }, filePath, new WbiExportOptions());

        using var archive = ZipFile.OpenRead(filePath);
        var pageEntry = archive.GetEntry("pages/page_001.json");
        Assert.NotNull(pageEntry);

        using var reader = new StreamReader(pageEntry!.Open());
        var json = reader.ReadToEnd();
        var pageData = JsonConvert.DeserializeObject<WbiPageData>(json);

        Assert.NotNull(pageData);
        Assert.Equal(8000, pageData!.CanvasWidth);
        Assert.Equal(6000, pageData.CanvasHeight);
        Assert.Equal(1.5, pageData.Zoom);
        Assert.Equal(-500, pageData.PanX);
        Assert.Equal(-300, pageData.PanY);
    }

    [StaFact]
    public async Task ExportAsync_WithTextAttachment_IncludesAttachmentData()
    {
        var exporter = new WbiExporter();
        var page = new BoardPage
        {
            Number = 1,
            Strokes = new StrokeCollection()
        };
        page.Attachments.Add(new BoardAttachment
        {
            Type = BoardAttachmentType.Text,
            X = 100,
            Y = 200,
            Width = 300,
            Height = 150,
            Text = "Hello World",
            ZIndex = 5
        });

        var filePath = GetTempFilePath();
        await exporter.ExportAsync(new[] { page }, filePath, new WbiExportOptions());

        using var archive = ZipFile.OpenRead(filePath);
        var pageEntry = archive.GetEntry("pages/page_001.json");
        using var reader = new StreamReader(pageEntry!.Open());
        var json = reader.ReadToEnd();
        var pageData = JsonConvert.DeserializeObject<WbiPageData>(json);

        Assert.Single(pageData!.Attachments);
        var att = pageData.Attachments[0];
        Assert.Equal("Text", att.Type);
        Assert.Equal(100, att.X);
        Assert.Equal(200, att.Y);
        Assert.Equal("Hello World", att.Text);
        Assert.Equal(5, att.ZIndex);
    }

    [StaFact]
    public async Task ExportAsync_WithLinkAttachment_SavesUrl()
    {
        var exporter = new WbiExporter();
        var page = new BoardPage
        {
            Number = 1,
            Strokes = new StrokeCollection()
        };
        page.Attachments.Add(new BoardAttachment
        {
            Type = BoardAttachmentType.Link,
            X = 50,
            Y = 50,
            Width = 200,
            Height = 80,
            Url = "https://example.com"
        });

        var filePath = GetTempFilePath();
        await exporter.ExportAsync(new[] { page }, filePath, new WbiExportOptions());

        using var archive = ZipFile.OpenRead(filePath);
        var pageEntry = archive.GetEntry("pages/page_001.json");
        using var reader = new StreamReader(pageEntry!.Open());
        var json = reader.ReadToEnd();
        var pageData = JsonConvert.DeserializeObject<WbiPageData>(json);

        Assert.Single(pageData!.Attachments);
        Assert.Equal("Link", pageData.Attachments[0].Type);
        Assert.Equal("https://example.com", pageData.Attachments[0].Url);
    }

    [StaFact]
    public async Task ExportAsync_WithEmptyPages_ThrowsArgumentException()
    {
        var exporter = new WbiExporter();
        var filePath = GetTempFilePath();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            exporter.ExportAsync(new List<BoardPage>(), filePath, new WbiExportOptions()));
    }

    [StaFact]
    public async Task ExportAsync_ReportsProgress()
    {
        var exporter = new WbiExporter();
        var pages = new List<BoardPage>
        {
            new BoardPage { Number = 1, Strokes = new StrokeCollection() },
            new BoardPage { Number = 2, Strokes = new StrokeCollection() }
        };
        foreach (var p in pages) p.Strokes.Add(CreateStroke(0, 0, 10, 10));

        var filePath = GetTempFilePath();
        var progressReports = new List<ExportProgress>();
        var progress = new Progress<ExportProgress>(p => progressReports.Add(p));

        await exporter.ExportAsync(pages, filePath, new WbiExportOptions(), progress);

        // Give Progress<T> time to invoke callbacks
        await Task.Delay(100);

        Assert.True(progressReports.Count > 0);
        Assert.Contains(progressReports, p => p.CurrentPage == 2 && p.TotalPages == 2);
    }

    [StaFact]
    public void EstimateFileSize_ReturnsPositiveValue()
    {
        var exporter = new WbiExporter();
        var pages = new List<BoardPage>
        {
            new BoardPage { Number = 1, Strokes = new StrokeCollection() }
        };
        pages[0].Strokes.Add(CreateStroke(0, 0, 100, 100));

        var options = new WbiExportOptions { IncludeImageAssets = false };
        var estimate = exporter.EstimateFileSize(pages, options);

        Assert.True(estimate > 0);
    }

    [StaFact]
    public async Task ExportAsync_DifferentCompressionLevels_AllSucceed()
    {
        var exporter = new WbiExporter();
        var page = new BoardPage { Number = 1, Strokes = new StrokeCollection() };
        page.Strokes.Add(CreateStroke(0, 0, 100, 100));

        var levels = new[]
        {
            WbiCompressionLevel.None,
            WbiCompressionLevel.Fast,
            WbiCompressionLevel.Standard,
            WbiCompressionLevel.Maximum
        };

        foreach (var level in levels)
        {
            var filePath = GetTempFilePath();
            var options = new WbiExportOptions { CompressionLevel = level };

            await exporter.ExportAsync(new[] { page }, filePath, options);

            Assert.True(File.Exists(filePath), $"Failed for compression level: {level}");
        }
    }
}
