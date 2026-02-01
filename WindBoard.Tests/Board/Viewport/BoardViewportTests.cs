using System.Numerics;
using WindBoard.Board.Viewport;
using Xunit;

namespace WindBoard.Tests.Board.Viewport;

public sealed class BoardViewportTests
{
    // 会把宽高钳制到至少 1 DIP
    [Fact]
    public void UpdateViewportSize_ClampsSizeToAtLeastOneDip()
    {
        var viewport = new BoardViewport();

        viewport.UpdateViewportSize(new Vector2(0.0f, -10.0f));

        AssertEx.Equal(new Vector2(1.0f, 1.0f), viewport.ViewportSizeDip);
    }

    // ScreenToWorld 与 WorldToScreen 可近似互逆
    [Fact]
    public void ScreenToWorld_And_WorldToScreen_AreApproximatelyInverse()
    {
        var viewport = new BoardViewport();
        viewport.UpdateViewportSize(new Vector2(200.0f, 100.0f));

        // 组合一下平移与缩放，验证在非默认状态下仍能回到原点。
        viewport.PanByScreenDelta(new Vector2(10.0f, -20.0f));
        viewport.ZoomAboutScreenPoint(new Vector2(50.0f, 25.0f), zoomFactor: 3.0f);

        Vector2 world = new(123.4f, -56.7f);
        Vector2 screen = Vector2.Transform(world, viewport.GetWorldToScreenTransform());
        Vector2 roundTrip = viewport.ScreenToWorld(screen);

        AssertEx.Equal(world, roundTrip, tolerance: 0.001f);
    }

    // 缩放后锚点处世界坐标保持不变
    [Fact]
    public void ZoomAboutScreenPoint_KeepsAnchorWorldPosition()
    {
        var viewport = new BoardViewport();
        viewport.UpdateViewportSize(new Vector2(300.0f, 200.0f));

        Vector2 anchor = new(25.0f, 40.0f);
        Vector2 worldBefore = viewport.ScreenToWorld(anchor);

        viewport.ZoomAboutScreenPoint(anchor, zoomFactor: 2.0f);

        Vector2 worldAfter = viewport.ScreenToWorld(anchor);
        AssertEx.Equal(worldBefore, worldAfter, tolerance: 0.0005f);
    }

    // 会钳制到最小与最大缩放
    [Fact]
    public void ZoomAboutScreenPoint_ClampsToMinAndMaxZoom()
    {
        var viewport = new BoardViewport();
        viewport.UpdateViewportSize(new Vector2(100.0f, 100.0f));

        Vector2 anchor = viewport.ViewportCenterDip;

        viewport.ZoomAboutScreenPoint(anchor, zoomFactor: 0.000001f);
        AssertEx.Equal(0.05f, viewport.Zoom, tolerance: 0.000001f);

        viewport.ZoomAboutScreenPoint(anchor, zoomFactor: 1_000_000.0f);
        AssertEx.Equal(32.0f, viewport.Zoom, tolerance: 0.000001f);
    }

    // 会按当前缩放修正相机位移
    [Fact]
    public void PanByScreenDelta_AdjustsCameraByZoom()
    {
        var viewport = new BoardViewport();
        viewport.UpdateViewportSize(new Vector2(100.0f, 100.0f));

        viewport.ZoomAboutScreenPoint(viewport.ViewportCenterDip, zoomFactor: 2.0f);

        Vector2 before = viewport.CameraWorld;
        Vector2 delta = new(10.0f, 20.0f);
        viewport.PanByScreenDelta(delta);
        Vector2 after = viewport.CameraWorld;

        AssertEx.Equal(before - delta / viewport.Zoom, after, tolerance: 0.000001f);
    }

    // 默认状态下以视口中心对称
    [Fact]
    public void GetVisibleWorldBounds_IsSymmetricAroundViewportCenter_ByDefault()
    {
        var viewport = new BoardViewport();
        viewport.UpdateViewportSize(new Vector2(100.0f, 50.0f));

        viewport.GetVisibleWorldBounds(out Vector2 minWorld, out Vector2 maxWorld);

        AssertEx.Equal(new Vector2(-50.0f, -25.0f), minWorld);
        AssertEx.Equal(new Vector2(50.0f, 25.0f), maxWorld);
    }
}
