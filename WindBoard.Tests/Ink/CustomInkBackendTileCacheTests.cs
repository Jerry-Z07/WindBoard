using System.Collections.Generic;
using System.Windows.Media;
using WindBoard.Controls;
using WindBoard.Core.Ink.Backend;
using WindBoard.Models.Ink;
using Xunit;

namespace WindBoard.Tests.Ink;

public sealed class CustomInkBackendTileCacheTests
{
    [StaFact]
    public void EndStroke_BakesIntoTiles_CreatesTileVisuals()
    {
        var surface = new InkSurface();
        using var backend = new CustomInkBackend(surface);

        var doc = new List<InkStrokeModel>();
        backend.BindDocument(doc);

        Assert.Equal(0, surface.TileVisualCount);

        backend.BeginStroke(
            pointerId: 1,
            style: new InkStrokeStyle(InkBrushKind.Pen, Colors.White, LogicalThicknessDip: 3.0, UsesPressure: false),
            startPoint: new InkPoint(0, 0, 0.5f, TimestampTicks: 0),
            zoomAtStart: 1.0);

        backend.AppendPoints(1, new[] { new InkPoint(10, 0, 0.5f, TimestampTicks: 0) });

        // Active stroke uses the dynamic layer only.
        Assert.Equal(0, surface.TileVisualCount);

        backend.EndStroke(1);

        Assert.True(surface.TileVisualCount > 0);
    }
}

