using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WindBoard.Models.Export;
using WindBoard.Services.Export;
using Xunit;
using static WindBoard.Tests.TestHelpers.InkTestHelpers;

namespace WindBoard.Tests.Services.Export;

public sealed class ExportRendererTests
{
    [StaFact]
    public void RenderPage_WithEmptyPage_ReturnsBackgroundOnlyBitmap()
    {
        var renderer = new ExportRenderer();
        var page = new BoardPage { Strokes = new StrokeCollection() };
        var options = new ImageExportOptions
        {
            Width = 800,
            Height = 600,
            BackgroundColor = Colors.White
        };

        var result = renderer.RenderPage(page, options);

        Assert.NotNull(result);
        Assert.Equal(800, result.PixelWidth);
        Assert.Equal(600, result.PixelHeight);
    }

    [StaFact]
    public void RenderPage_WithStrokes_ReturnsBitmapContainingContent()
    {
        var renderer = new ExportRenderer();
        var page = new BoardPage { Strokes = new StrokeCollection() };
        page.Strokes.Add(CreateStroke(100, 100, 200, 200));

        var options = new ImageExportOptions
        {
            Width = 400,
            Height = 300,
            KeepAspectRatio = true,
            BackgroundColor = Color.FromRgb(0x0F, 0x12, 0x16)
        };

        var result = renderer.RenderPage(page, options);

        Assert.NotNull(result);
        Assert.Equal(400, result.PixelWidth);
        Assert.Equal(300, result.PixelHeight);
    }

    [StaFact]
    public void CalculateContentBounds_WithNoContent_ReturnsEmptyRect()
    {
        var renderer = new ExportRenderer();
        var page = new BoardPage { Strokes = new StrokeCollection() };

        var bounds = renderer.CalculateContentBounds(page);

        Assert.True(bounds.IsEmpty);
    }

    [StaFact]
    public void CalculateContentBounds_WithStrokes_ReturnsStrokeBounds()
    {
        var renderer = new ExportRenderer();
        var page = new BoardPage { Strokes = new StrokeCollection() };
        page.Strokes.Add(CreateStroke(100, 100, 300, 300));

        var bounds = renderer.CalculateContentBounds(page);

        Assert.False(bounds.IsEmpty);
        Assert.True(bounds.Width > 0);
        Assert.True(bounds.Height > 0);
    }

    [StaFact]
    public void CalculateContentBounds_WithAttachments_IncludesAttachmentBounds()
    {
        var renderer = new ExportRenderer();
        var page = new BoardPage { Strokes = new StrokeCollection() };
        page.Attachments.Add(new BoardAttachment
        {
            Type = BoardAttachmentType.Text,
            X = 500,
            Y = 500,
            Width = 200,
            Height = 100,
            Text = "Test"
        });

        var bounds = renderer.CalculateContentBounds(page);

        Assert.False(bounds.IsEmpty);
        Assert.True(bounds.Right >= 700); // X + Width
        Assert.True(bounds.Bottom >= 600); // Y + Height
    }

    [StaFact]
    public void CalculateContentBounds_WithStrokesAndAttachments_ReturnsUnionBounds()
    {
        var renderer = new ExportRenderer();
        var page = new BoardPage { Strokes = new StrokeCollection() };
        page.Strokes.Add(CreateStroke(100, 100, 200, 200));
        page.Attachments.Add(new BoardAttachment
        {
            Type = BoardAttachmentType.Text,
            X = 500,
            Y = 500,
            Width = 200,
            Height = 100,
            Text = "Test"
        });

        var bounds = renderer.CalculateContentBounds(page);

        Assert.False(bounds.IsEmpty);
        // Should include both stroke area and attachment area
        Assert.True(bounds.Left <= 100);
        Assert.True(bounds.Right >= 700);
    }

    [StaFact]
    public void RenderPageToPng_ReturnsValidPngData()
    {
        var renderer = new ExportRenderer();
        var page = new BoardPage { Strokes = new StrokeCollection() };
        page.Strokes.Add(CreateStroke(50, 50, 150, 150));

        var options = new ImageExportOptions
        {
            Width = 200,
            Height = 200,
            Format = ExportFormat.Png
        };

        var pngData = renderer.RenderPageToPng(page, options);

        Assert.NotNull(pngData);
        Assert.True(pngData.Length > 0);
        // PNG magic bytes
        Assert.Equal(0x89, pngData[0]);
        Assert.Equal(0x50, pngData[1]); // P
        Assert.Equal(0x4E, pngData[2]); // N
        Assert.Equal(0x47, pngData[3]); // G
    }

    [StaFact]
    public void RenderPageToJpeg_ReturnsValidJpegData()
    {
        var renderer = new ExportRenderer();
        var page = new BoardPage { Strokes = new StrokeCollection() };
        page.Strokes.Add(CreateStroke(50, 50, 150, 150));

        var options = new ImageExportOptions
        {
            Width = 200,
            Height = 200,
            Format = ExportFormat.Jpg,
            JpegQuality = 85
        };

        var jpegData = renderer.RenderPageToJpeg(page, options);

        Assert.NotNull(jpegData);
        Assert.True(jpegData.Length > 0);
        // JPEG magic bytes (SOI marker)
        Assert.Equal(0xFF, jpegData[0]);
        Assert.Equal(0xD8, jpegData[1]);
    }

    [StaFact]
    public void EstimateFileSize_ReturnsPositiveValue()
    {
        var renderer = new ExportRenderer();
        var page = new BoardPage { Strokes = new StrokeCollection() };
        page.Strokes.Add(CreateStroke(0, 0, 100, 100));

        var options = new ImageExportOptions
        {
            Width = 1920,
            Height = 1080,
            Format = ExportFormat.Png
        };

        var estimate = renderer.EstimateFileSize(page, options);

        Assert.True(estimate > 0);
    }

    [StaFact]
    public void RenderPage_WithDifferentResolutions_ProducesCorrectSizes()
    {
        var renderer = new ExportRenderer();
        var page = new BoardPage { Strokes = new StrokeCollection() };
        page.Strokes.Add(CreateStroke(0, 0, 100, 100));

        var options1080p = new ImageExportOptions { Width = 1920, Height = 1080 };
        var options4K = new ImageExportOptions { Width = 3840, Height = 2160 };

        var result1080p = renderer.RenderPage(page, options1080p);
        var result4K = renderer.RenderPage(page, options4K);

        Assert.Equal(1920, result1080p.PixelWidth);
        Assert.Equal(1080, result1080p.PixelHeight);
        Assert.Equal(3840, result4K.PixelWidth);
        Assert.Equal(2160, result4K.PixelHeight);
    }
}
