using System.Numerics;
using Windows.UI;
using WindBoard.Features.ScreenAnnotation.Services;
using Xunit;

namespace WindBoard.Tests.Features.ScreenAnnotation;

public sealed class ScreenAnnotationSessionHostTests
{
    [Fact]
    public void BuildViewportPreset_UsesIdentityLikeScreenMapping()
    {
        var host = new ScreenAnnotationSessionHost();

        ScreenAnnotationViewportPreset preset = host.BuildViewportPreset(new Vector2(1920.0f, 1080.0f));

        AssertEx.Equal(new Vector2(960.0f, 540.0f), preset.CameraWorld, tolerance: 0.001f);
        AssertEx.Equal(1.0f, preset.Zoom, tolerance: 0.0001f);
    }

    [Fact]
    public void CanvasBackgroundColor_IsTransparent()
    {
        var host = new ScreenAnnotationSessionHost();

        Color color = host.CanvasBackgroundColor;

        Assert.Equal((byte)0, color.A);
        Assert.Equal((byte)0, color.R);
        Assert.Equal((byte)0, color.G);
        Assert.Equal((byte)0, color.B);
    }
}
