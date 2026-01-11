using System.Windows;
using System.Windows.Media;
using WindBoard.Core.Input.RealTimeStylus;
using Xunit;

namespace WindBoard.Tests.Input;

public sealed class RealTimeStylusAdapterCoordinateMappingTests
{
    [StaFact]
    public void MapStylusPointToCanvasAndViewport_RawPointLooksLikeViewport_MapsToCanvasViaInverse()
    {
        var canvasToViewport = new MatrixTransform(new Matrix(m11: 2, m12: 0, m21: 0, m22: 2, offsetX: 100, offsetY: 50));
        var rawViewport = new Point(x: 790, y: 590);

        RealTimeStylusAdapter.MapStylusPointToCanvasAndViewport(
            rawViewport,
            canvasToViewport,
            viewportWidthDip: 800,
            viewportHeightDip: 600,
            out Point canvasPoint,
            out Point viewportPoint);

        Assert.Equal(rawViewport, viewportPoint);
        Assert.Equal(new Point(x: 345, y: 270), canvasPoint);
    }

    [StaFact]
    public void MapStylusPointToCanvasAndViewport_RawPointLooksLikeCanvas_MapsToViewportViaForwardTransform()
    {
        var canvasToViewport = new MatrixTransform(new Matrix(m11: 2, m12: 0, m21: 0, m22: 2, offsetX: -1000, offsetY: 0));
        var rawCanvas = new Point(x: 900, y: 300);

        RealTimeStylusAdapter.MapStylusPointToCanvasAndViewport(
            rawCanvas,
            canvasToViewport,
            viewportWidthDip: 800,
            viewportHeightDip: 600,
            out Point canvasPoint,
            out Point viewportPoint);

        Assert.Equal(rawCanvas, canvasPoint);
        Assert.Equal(new Point(x: 800, y: 600), viewportPoint);
    }
}

