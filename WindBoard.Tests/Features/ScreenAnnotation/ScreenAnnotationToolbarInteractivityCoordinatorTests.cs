using WindBoard.Features.ScreenAnnotation.Models;
using WindBoard.Features.ScreenAnnotation.Services;
using Xunit;

namespace WindBoard.Tests.Features.ScreenAnnotation;

public sealed class ScreenAnnotationToolbarInteractivityCoordinatorTests
{
    [Fact]
    public void ApplyMode_Pen_UpdatesOverlayAndPromotesToolbar()
    {
        var overlay = new FakeOverlay();
        var toolbar = new FakeToolbar();

        ScreenAnnotationToolbarInteractivityCoordinator.ApplyMode(
            ScreenAnnotationMode.Pen,
            windowState: null,
            overlay,
            toolbar);

        Assert.Equal(ScreenAnnotationMode.Pen, overlay.AppliedMode);
        Assert.Equal(ScreenAnnotationMode.Pen, toolbar.SelectedMode);
        Assert.Equal(1, toolbar.EnsureInteractiveTopMostCallCount);
        Assert.Same(overlay, toolbar.LastOverlayUsedForZOrder);
    }

    [Fact]
    public void ApplyMode_WhenOverlayIsMissing_FallsBackToWindowState()
    {
        var state = new ScreenAnnotationWindowState();
        var toolbar = new FakeToolbar();

        ScreenAnnotationToolbarInteractivityCoordinator.ApplyMode(
            ScreenAnnotationMode.Eraser,
            state,
            overlay: null,
            toolbar);

        Assert.Equal(ScreenAnnotationMode.Eraser, state.Mode);
        Assert.False(state.IsPassThrough);
        Assert.Equal(ScreenAnnotationMode.Eraser, toolbar.SelectedMode);
        Assert.Equal(1, toolbar.EnsureInteractiveTopMostCallCount);
    }

    [Fact]
    public void EnsureToolbarInteractiveAfterOverlayActivation_PromotesToolbarAgain()
    {
        var overlay = new FakeOverlay();
        var toolbar = new FakeToolbar();

        ScreenAnnotationToolbarInteractivityCoordinator.EnsureToolbarInteractiveAfterOverlayActivation(
            overlay,
            toolbar);

        Assert.Equal(1, toolbar.EnsureInteractiveTopMostCallCount);
        Assert.Same(overlay, toolbar.LastOverlayUsedForZOrder);
    }

    [Fact]
    public void EnsureToolbarInteractiveAfterOverlayWindowPositionChanged_WhenZOrderDidNotChange_DoesNothing()
    {
        var overlay = new FakeOverlay();
        var toolbar = new FakeToolbar();

        ScreenAnnotationToolbarInteractivityCoordinator.EnsureToolbarInteractiveAfterOverlayWindowPositionChanged(
            overlay,
            toolbar,
            insertAfterHwnd: new IntPtr(5678),
            windowPosFlags: 0x0004);

        Assert.Equal(0, toolbar.EnsureInteractiveTopMostCallCount);
    }

    [Fact]
    public void EnsureToolbarInteractiveAfterOverlayWindowPositionChanged_WhenOverlayAlreadyBehindToolbar_DoesNothing()
    {
        var overlay = new FakeOverlay();
        var toolbar = new FakeToolbar();

        ScreenAnnotationToolbarInteractivityCoordinator.EnsureToolbarInteractiveAfterOverlayWindowPositionChanged(
            overlay,
            toolbar,
            insertAfterHwnd: toolbar.WindowHandle,
            windowPosFlags: 0);

        Assert.Equal(0, toolbar.EnsureInteractiveTopMostCallCount);
    }

    [Fact]
    public void EnsureToolbarInteractiveAfterOverlayWindowPositionChanged_WhenOverlayMovesAheadOfToolbar_PromotesToolbar()
    {
        var overlay = new FakeOverlay();
        var toolbar = new FakeToolbar();

        ScreenAnnotationToolbarInteractivityCoordinator.EnsureToolbarInteractiveAfterOverlayWindowPositionChanged(
            overlay,
            toolbar,
            insertAfterHwnd: new IntPtr(5678),
            windowPosFlags: 0);

        Assert.Equal(1, toolbar.EnsureInteractiveTopMostCallCount);
        Assert.Same(overlay, toolbar.LastOverlayUsedForZOrder);
    }

    private sealed class FakeOverlay : IScreenAnnotationModeOverlay
    {
        internal ScreenAnnotationMode? AppliedMode { get; private set; }

        public void ApplyMode(ScreenAnnotationMode mode)
        {
            AppliedMode = mode;
        }

        public bool TryGetWindowHandle(out nint hwnd)
        {
            hwnd = new IntPtr(1234);
            return true;
        }
    }

    private sealed class FakeToolbar : IScreenAnnotationModeToolbar
    {
        internal IntPtr WindowHandle { get; } = new(4321);

        internal ScreenAnnotationMode? SelectedMode { get; private set; }

        internal int EnsureInteractiveTopMostCallCount { get; private set; }

        internal IScreenAnnotationModeOverlay? LastOverlayUsedForZOrder { get; private set; }

        public void EnsureInteractiveTopMost(IScreenAnnotationModeOverlay? overlay)
        {
            EnsureInteractiveTopMostCallCount++;
            LastOverlayUsedForZOrder = overlay;
        }

        public void SetSelectedMode(ScreenAnnotationMode mode)
        {
            SelectedMode = mode;
        }

        public bool TryGetWindowHandle(out nint hwnd)
        {
            hwnd = WindowHandle;
            return true;
        }
    }
}
