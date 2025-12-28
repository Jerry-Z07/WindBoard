using System.Windows;
using System.Windows.Media;
using WindBoard.Services;
using Xunit;

namespace WindBoard.Tests.Services;

public sealed class ZoomPanServiceTests
{
    [StaFact]
    public void ZoomAt_KeepsViewportPointAnchored()
    {
        var zoomTransform = new ScaleTransform(1, 1);
        var panTransform = new TranslateTransform(0, 0);
        var svc = new ZoomPanService(zoomTransform, panTransform, minZoom: 0.5, maxZoom: 5.0);

        svc.ZoomAt(new Point(100, 50), newZoom: 2.0);

        Assert.Equal(2.0, svc.Zoom, precision: 12);
        Assert.Equal(-100.0, svc.PanX, precision: 12);
        Assert.Equal(-50.0, svc.PanY, precision: 12);
        Assert.Equal(svc.Zoom, zoomTransform.ScaleX, precision: 12);
        Assert.Equal(svc.PanX, panTransform.X, precision: 12);
    }

    [StaFact]
    public void SetZoomDirect_ClampsToMax()
    {
        var zoomTransform = new ScaleTransform(1, 1);
        var panTransform = new TranslateTransform(0, 0);
        var svc = new ZoomPanService(zoomTransform, panTransform, minZoom: 0.5, maxZoom: 2.0);

        svc.SetZoomDirect(10.0);

        Assert.Equal(2.0, svc.Zoom, precision: 12);
    }

    [StaFact]
    public void MousePan_UpdatesPanAndState()
    {
        var zoomTransform = new ScaleTransform(1, 1);
        var panTransform = new TranslateTransform(0, 0);
        var svc = new ZoomPanService(zoomTransform, panTransform);

        svc.BeginMousePan(new Point(10, 10));
        Assert.True(svc.IsMousePanning);

        Assert.True(svc.UpdateMousePan(new Point(20, 25)));
        Assert.Equal(10.0, svc.PanX, precision: 12);
        Assert.Equal(15.0, svc.PanY, precision: 12);

        svc.EndMousePan();
        Assert.False(svc.IsMousePanning);
    }

    [StaFact]
    public void TouchGesture_TwoTouches_ActivatesGestureAndUpdatesZoom()
    {
        var zoomTransform = new ScaleTransform(1, 1);
        var panTransform = new TranslateTransform(0, 0);
        var svc = new ZoomPanService(zoomTransform, panTransform, minZoom: 0.5, maxZoom: 5.0);

        Assert.False(svc.TouchDown(1, new Point(0, 0)));
        Assert.True(svc.TouchDown(2, new Point(100, 0)));
        Assert.True(svc.IsGestureActive);

        Assert.True(svc.TouchMove(2, new Point(200, 0)));
        Assert.True(svc.Zoom > 1.0);
    }
}

