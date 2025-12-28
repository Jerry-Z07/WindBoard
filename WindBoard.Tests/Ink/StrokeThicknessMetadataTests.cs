using System.Windows.Ink;
using System.Windows.Input;
using WindBoard.Core.Ink;
using Xunit;

namespace WindBoard.Tests.Ink;

public sealed class StrokeThicknessMetadataTests
{
    [StaFact]
    public void SetLogicalThicknessDip_ThenTryGet_ReturnsTrueAndSameValue()
    {
        var stroke = CreateStroke();

        StrokeThicknessMetadata.SetLogicalThicknessDip(stroke, 3.5);

        Assert.True(StrokeThicknessMetadata.TryGetLogicalThicknessDip(stroke, out var thickness));
        Assert.Equal(3.5, thickness, precision: 12);
    }

    [StaFact]
    public void GetOrCreateLogicalThicknessDip_UsesDrawingAttributesAndZoom_AndPersistsToStroke()
    {
        var stroke = CreateStroke();
        stroke.DrawingAttributes.Width = 2;
        stroke.DrawingAttributes.Height = 4;

        var logical = StrokeThicknessMetadata.GetOrCreateLogicalThicknessDip(stroke, currentZoom: 2.0);

        Assert.Equal(6.0, logical, precision: 12);
        Assert.True(StrokeThicknessMetadata.TryGetLogicalThicknessDip(stroke, out var stored));
        Assert.Equal(logical, stored, precision: 12);
    }

    [StaFact]
    public void TryGetLogicalThicknessDip_ReturnsFalse_ForInvalidStoredValue()
    {
        var stroke = CreateStroke();
        stroke.AddPropertyData(StrokeThicknessMetadata.LogicalThicknessDipPropertyId, "not-a-number");

        Assert.False(StrokeThicknessMetadata.TryGetLogicalThicknessDip(stroke, out _));
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

