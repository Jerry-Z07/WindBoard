using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using WindBoard.Services;
using Xunit;

namespace WindBoard.Tests.Services;

public sealed class PageServiceTests
{
    [StaFact]
    public void InitializePagesIfNeeded_CreatesSingleCurrentPage_SharingCanvasStrokes()
    {
        var canvas = new InkCanvas { Width = 8000, Height = 6000, Strokes = new StrokeCollection() };
        var zoomPan = new ZoomPanService(new ScaleTransform(1, 1), new TranslateTransform(0, 0));
        var svc = new PageService(canvas, zoomPan);

        svc.InitializePagesIfNeeded();

        Assert.Single(svc.Pages);
        Assert.Equal(0, svc.CurrentPageIndex);
        Assert.NotNull(svc.CurrentPage);
        Assert.True(svc.CurrentPage!.IsCurrent);
        Assert.Same(canvas.Strokes, svc.CurrentPage.Strokes);
        Assert.Equal("1 / 1", svc.PageIndicatorText);
    }

    [StaFact]
    public void AddPage_SwitchesToNewPage_AndCanvasUsesNewStrokeCollection()
    {
        var canvas = new InkCanvas { Width = 8000, Height = 6000, Strokes = new StrokeCollection() };
        var zoomPan = new ZoomPanService(new ScaleTransform(1, 1), new TranslateTransform(0, 0));
        var svc = new PageService(canvas, zoomPan);
        svc.InitializePagesIfNeeded();
        var firstStrokes = canvas.Strokes;

        svc.AddPage();

        Assert.Equal(2, svc.Pages.Count);
        Assert.Equal(1, svc.CurrentPageIndex);
        Assert.True(svc.IsMultiPage);
        Assert.NotSame(firstStrokes, canvas.Strokes);
        Assert.Same(canvas.Strokes, svc.CurrentPage!.Strokes);
        Assert.Equal("2 / 2", svc.PageIndicatorText);
        Assert.True(svc.Pages[1].IsCurrent);
        Assert.False(svc.Pages[0].IsCurrent);
    }

    [StaFact]
    public void StrokeChanges_IncrementCurrentPageContentVersion()
    {
        var canvas = new InkCanvas { Width = 8000, Height = 6000, Strokes = new StrokeCollection() };
        var zoomPan = new ZoomPanService(new ScaleTransform(1, 1), new TranslateTransform(0, 0));
        var svc = new PageService(canvas, zoomPan);
        svc.InitializePagesIfNeeded();

        var page = svc.CurrentPage!;
        int before = page.ContentVersion;

        canvas.Strokes.Add(CreateStroke());

        Assert.True(page.ContentVersion > before);
    }

    private static Stroke CreateStroke()
    {
        var points = new StylusPointCollection
        {
            new StylusPoint(0, 0),
            new StylusPoint(1, 1)
        };
        return new Stroke(points);
    }
}

