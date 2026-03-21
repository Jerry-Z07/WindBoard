using System.Numerics;
using Windows.UI;
using WindBoard.Board.Editing;
using WindBoard.Features.ScreenAnnotation.Models;
using WindBoard.Features.ScreenAnnotation.Services;
using WindBoard.Settings;
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

    [Fact]
    public void Constructor_UsesFirstValidPaletteColorAndMiddlePresetAsDefaults()
    {
        var snapshot = new PenSettingsSnapshot
        {
            PaletteHexes = ["#112233", "#445566", null],
            ThicknessPresets = [1.5f, 7.0f, 9.0f],
            UseThicknessSlider = false,
        };

        var host = new ScreenAnnotationSessionHost(snapshot);

        Color color = host.DefaultPenColor;

        Assert.Equal((byte)0xFF, color.A);
        Assert.Equal((byte)0x11, color.R);
        Assert.Equal((byte)0x22, color.G);
        Assert.Equal((byte)0x33, color.B);
        Assert.Equal(7.0f, host.DefaultPenBaseSize);
    }

    [Fact]
    public void Constructor_WhenPaletteHasNoValidColor_FallsBackToBoardDefaultBlack()
    {
        var snapshot = new PenSettingsSnapshot
        {
            PaletteHexes = [null, "bad-color", ""],
            ThicknessPresets = [2.0f, 3.0f, 5.0f],
            UseThicknessSlider = true,
        };

        var host = new ScreenAnnotationSessionHost(snapshot);

        Color color = host.DefaultPenColor;

        Assert.Equal((byte)0xFF, color.A);
        Assert.Equal((byte)0x00, color.R);
        Assert.Equal((byte)0x00, color.G);
        Assert.Equal((byte)0x00, color.B);
    }

    [Fact]
    public void Constructor_DefaultsToPixelStrokeEraser()
    {
        var snapshot = new PenSettingsSnapshot
        {
            PaletteHexes = ["#ABCDEF", "#123456", "#654321"],
            ThicknessPresets = [2.0f, 3.0f, 5.0f],
            UseThicknessSlider = false,
        };

        var host = new ScreenAnnotationSessionHost(snapshot);

        Assert.IsType<PixelStrokeEraser>(host.DefaultEraser);
    }

    [Fact]
    public void CreateInitialDrawingStateSnapshot_UsesResolvedDefaultsAndStartsNotClearable()
    {
        var snapshot = new PenSettingsSnapshot
        {
            PaletteHexes = ["#ABCDEF", "#123456", "#654321"],
            ThicknessPresets = [2.0f, 6.0f, 8.0f],
            UseThicknessSlider = false,
        };

        var host = new ScreenAnnotationSessionHost(snapshot);

        ScreenAnnotationDrawingStateSnapshot state = host.CreateInitialDrawingStateSnapshot();

        Assert.Equal(host.DefaultPenColor, state.PenColor);
        Assert.Equal(host.DefaultPenBaseSize, state.PenBaseSize);
        Assert.Equal(ScreenAnnotationEraserMode.Pixel, state.EraserMode);
        Assert.False(state.CanClear);
    }
}
