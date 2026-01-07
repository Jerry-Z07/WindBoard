using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using WindBoard.Core.Ink;
using WindBoard.Core.Ink.Adapters;
using WindBoard.Models.Ink;
using Xunit;

namespace WindBoard.Tests.Ink;

public sealed class WpfStrokeAdapterTests
{
    [StaFact]
    public void ToModel_PreservesStyleAndPoints()
    {
        var points = new StylusPointCollection
        {
            new StylusPoint(0, 0, 0.2f),
            new StylusPoint(10, 0, 0.8f)
        };

        var stroke = new Stroke(points);
        stroke.DrawingAttributes.Color = Colors.Red;
        stroke.DrawingAttributes.IgnorePressure = false;
        stroke.DrawingAttributes.Width = 2.0;
        stroke.DrawingAttributes.Height = 2.0;

        StrokeThicknessMetadata.SetLogicalThicknessDip(stroke, 4.0);

        var model = WpfStrokeAdapter.ToModel(stroke, currentZoom: 1.0);

        Assert.Equal(Colors.Red, model.Style.Color);
        Assert.Equal(4.0, model.Style.LogicalThicknessDip, precision: 6);
        Assert.True(model.Style.UsesPressure);
        Assert.Equal(2, model.Points.Count);
        Assert.Equal(0.2f, model.Points[0].Pressure, precision: 6);
        Assert.Equal(0.8f, model.Points[1].Pressure, precision: 6);

        // logical / render (2.0) -> zoomAtCreation ~= 2.0
        Assert.Equal(2.0, model.ZoomAtCreation, precision: 6);
    }

    [StaFact]
    public void ToWpfStroke_RoundTripsLogicalThickness()
    {
        var model = new InkStrokeModel
        {
            Id = System.Guid.NewGuid(),
            ZoomAtCreation = 2.0,
            Style = new InkStrokeStyle(InkBrushKind.Pen, Colors.Blue, LogicalThicknessDip: 6.0, UsesPressure: true)
        };
        model.Points.Add(new InkPoint(0, 0, 0.5f, TimestampTicks: 0));
        model.Points.Add(new InkPoint(10, 0, 0.9f, TimestampTicks: 0));

        var stroke = WpfStrokeAdapter.ToWpfStroke(model, currentZoom: 1.0);
        Assert.NotNull(stroke);

        Assert.Equal(Colors.Blue, stroke!.DrawingAttributes.Color);
        Assert.False(stroke.DrawingAttributes.IgnorePressure);
        Assert.Equal(3.0, stroke.DrawingAttributes.Width, precision: 6);
        Assert.Equal(3.0, stroke.DrawingAttributes.Height, precision: 6);

        Assert.True(StrokeThicknessMetadata.TryGetLogicalThicknessDip(stroke, out double logical));
        Assert.Equal(6.0, logical, precision: 6);
    }
}

