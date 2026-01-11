using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;
using WindBoard.Services;
using Xunit;

namespace WindBoard.Tests.Services;

public sealed class PageServiceTests
{
    [StaFact]
    public void InitializePagesIfNeeded_CreatesSingleCurrentPage_AndCapturesViewState()
    {
        var canvas = new Canvas { Width = 8000, Height = 6000 };
        var zoomPan = new ZoomPanService(new ScaleTransform(1, 1), new TranslateTransform(0, 0));
        zoomPan.SetViewDirect(zoom: 1.25, panX: -100, panY: -200);
        var svc = new PageService(canvas, zoomPan);

        svc.InitializePagesIfNeeded();

        Assert.Single(svc.Pages);
        Assert.Equal(0, svc.CurrentPageIndex);
        Assert.NotNull(svc.CurrentPage);
        Assert.True(svc.CurrentPage!.IsCurrent);
        Assert.Equal(8000, svc.CurrentPage.CanvasWidth);
        Assert.Equal(6000, svc.CurrentPage.CanvasHeight);
        Assert.Equal(1.25, svc.CurrentPage.Zoom, precision: 6);
        Assert.Equal(-100, svc.CurrentPage.PanX, precision: 6);
        Assert.Equal(-200, svc.CurrentPage.PanY, precision: 6);
        Assert.Equal("1 / 1", svc.PageIndicatorText);
    }

    [StaFact]
    public void AddPage_SwitchesToNewPage_AndUpdatesCanvasAndView()
    {
        var canvas = new Canvas { Width = 8000, Height = 6000 };
        var zoomPan = new ZoomPanService(new ScaleTransform(1, 1), new TranslateTransform(0, 0));
        zoomPan.SetViewDirect(zoom: 1.5, panX: -10, panY: -20);
        var svc = new PageService(canvas, zoomPan);
        svc.InitializePagesIfNeeded();

        svc.AddPage();

        Assert.Equal(2, svc.Pages.Count);
        Assert.Equal(1, svc.CurrentPageIndex);
        Assert.True(svc.IsMultiPage);
        Assert.Equal(8000, canvas.Width);
        Assert.Equal(8000, canvas.Height);
        Assert.Equal(1.5, zoomPan.Zoom, precision: 6);
        Assert.Equal(0, zoomPan.PanX, precision: 6);
        Assert.Equal(0, zoomPan.PanY, precision: 6);
        Assert.Equal("2 / 2", svc.PageIndicatorText);
        Assert.True(svc.Pages[1].IsCurrent);
        Assert.False(svc.Pages[0].IsCurrent);
    }

    [StaFact]
    public void ReplaceAllPages_LoadsCurrentPageIntoCanvas_AndRenumbersPages()
    {
        var canvas = new Canvas { Width = 8000, Height = 6000 };
        var zoomPan = new ZoomPanService(new ScaleTransform(1, 1), new TranslateTransform(0, 0));
        var svc = new PageService(canvas, zoomPan);

        var pages = new List<BoardPage>
        {
            new BoardPage { Number = 99, CanvasWidth = 1000, CanvasHeight = 2000, Zoom = 1.0, PanX = 10, PanY = 20 },
            new BoardPage { Number = 42, CanvasWidth = 3000, CanvasHeight = 4000, Zoom = 2.0, PanX = -5, PanY = -6 }
        };

        svc.ReplaceAllPages(pages, currentIndex: 1);

        Assert.Equal(2, svc.Pages.Count);
        Assert.Equal(2, svc.Pages[1].Number);
        Assert.Equal(1, svc.CurrentPageIndex);
        Assert.Equal(3000, canvas.Width);
        Assert.Equal(4000, canvas.Height);
        Assert.Equal(2.0, zoomPan.Zoom, precision: 6);
        Assert.Equal(-5, zoomPan.PanX, precision: 6);
        Assert.Equal(-6, zoomPan.PanY, precision: 6);
    }
}
