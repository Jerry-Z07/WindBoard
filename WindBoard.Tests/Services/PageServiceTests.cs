using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindBoard.Controls;
using WindBoard.Core.Ink.Backend;
using WindBoard.Models.Ink;
using WindBoard.Services;
using WindBoard.Services.Ink;
using Xunit;

namespace WindBoard.Tests.Services;

public sealed class PageServiceTests
{
    private static (PageService Service, CustomInkBackend Backend) CreateService()
    {
        var canvas = new InkCanvas { Width = 8000, Height = 6000 };
        var zoomPan = new ZoomPanService(new ScaleTransform(1, 1), new TranslateTransform(0, 0));
        var surface = new InkSurface();
        var backend = new CustomInkBackend(surface);
        var inkService = new InkService(canvas, surface);
        inkService.SetBackend(backend);

        var svc = new PageService(canvas, inkService, zoomPan);
        return (svc, backend);
    }

    [StaFact]
    public void InitializePagesIfNeeded_CreatesSingleCurrentPage_BindsInkModel()
    {
        var (svc, backend) = CreateService();

        svc.InitializePagesIfNeeded();

        Assert.Single(svc.Pages);
        Assert.Equal(0, svc.CurrentPageIndex);
        Assert.NotNull(svc.CurrentPage);
        Assert.True(svc.CurrentPage!.IsCurrent);
        Assert.Equal("1 / 1", svc.PageIndicatorText);
        Assert.Empty(svc.CurrentPage.InkStrokes);

        backend.BeginStroke(
            pointerId: 1,
            style: new InkStrokeStyle(InkBrushKind.Pen, Colors.White, LogicalThicknessDip: 1.0, UsesPressure: false),
            startPoint: new InkPoint(0, 0, 0.5f, 0),
            zoomAtStart: 1.0);
        backend.EndStroke(pointerId: 1);

        Assert.Single(svc.CurrentPage.InkStrokes);
    }

    [StaFact]
    public void AddPage_SwitchesToNewPage_AndBindsInkDocument()
    {
        var (svc, backend) = CreateService();
        svc.InitializePagesIfNeeded();
        var firstPage = svc.CurrentPage!;

        backend.BeginStroke(
            pointerId: 1,
            style: new InkStrokeStyle(InkBrushKind.Pen, Colors.White, LogicalThicknessDip: 1.0, UsesPressure: false),
            startPoint: new InkPoint(0, 0, 0.5f, 0),
            zoomAtStart: 1.0);
        backend.EndStroke(pointerId: 1);
        Assert.Single(firstPage.InkStrokes);

        svc.AddPage();

        Assert.Equal(2, svc.Pages.Count);
        Assert.Equal(1, svc.CurrentPageIndex);
        Assert.True(svc.IsMultiPage);
        Assert.Equal("2 / 2", svc.PageIndicatorText);
        Assert.True(svc.Pages[1].IsCurrent);
        Assert.False(svc.Pages[0].IsCurrent);

        var secondPage = svc.CurrentPage!;
        Assert.Empty(secondPage.InkStrokes);

        backend.BeginStroke(
            pointerId: 2,
            style: new InkStrokeStyle(InkBrushKind.Pen, Colors.White, LogicalThicknessDip: 1.0, UsesPressure: false),
            startPoint: new InkPoint(10, 0, 0.5f, 0),
            zoomAtStart: 1.0);
        backend.EndStroke(pointerId: 2);

        Assert.Single(secondPage.InkStrokes);
        Assert.Single(firstPage.InkStrokes);
    }

    [StaFact]
    public void InkChanges_IncrementCurrentPageContentVersion()
    {
        var (svc, backend) = CreateService();
        svc.InitializePagesIfNeeded();

        var page = svc.CurrentPage!;
        int before = page.ContentVersion;

        backend.BeginStroke(
            pointerId: 1,
            style: new InkStrokeStyle(InkBrushKind.Pen, Colors.White, LogicalThicknessDip: 1.0, UsesPressure: false),
            startPoint: new InkPoint(0, 0, 0.5f, 0),
            zoomAtStart: 1.0);

        Assert.True(page.ContentVersion > before);
    }
}
