using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using WindBoard.Controls;
using WindBoard.Models.InkV2;
using WindBoard.Services.InkV2;
using WindBoard.Services.InkV2.Rendering;

namespace WindBoard
{
    public partial class MainWindow
    {
        private InkDxRenderer? _inkDxRenderer;
        private TranslateTransform? _inkSurfaceInverseTranslate;
        private ScaleTransform? _inkSurfaceInverseScale;
        private readonly List<InkFragment> _forceVisibleFragments = new(256);
        private readonly List<InkSegmentHit> _cpuHitScratch = new(2048);
        private readonly HashSet<InkFragment> _cpuVisibleFragments = new();
        private bool? _lastInkRendererWasCpu;
        private bool _inkDebugPendingPostStrokeRenderCheck;
        private int _inkDebugPendingPostStrokeCount;
        private Size _lastSyncedInkSurfaceSize;

        private void InitializeInkSurfaceRenderer()
        {
            if (InkSurface == null) return;

            _inkDxRenderer ??= new InkDxRenderer();

            _inkSurfaceInverseTranslate ??= new TranslateTransform();
            _inkSurfaceInverseScale ??= new ScaleTransform(1, 1);

            if (InkSurface.RenderTransform is not TransformGroup group ||
                group.Children.Count != 2 ||
                !ReferenceEquals(group.Children[0], _inkSurfaceInverseTranslate) ||
                !ReferenceEquals(group.Children[1], _inkSurfaceInverseScale))
            {
                group = new TransformGroup();
                group.Children.Add(_inkSurfaceInverseTranslate);
                group.Children.Add(_inkSurfaceInverseScale);
                InkSurface.RenderTransform = group;
            }

            InkSurface.RenderFrame -= InkSurface_RenderFrame;
            InkSurface.RenderFrame += InkSurface_RenderFrame;
            InkSurface.RenderFallbackFrame -= InkSurface_RenderFallbackFrame;
            InkSurface.RenderFallbackFrame += InkSurface_RenderFallbackFrame;

            if (Viewport != null)
            {
                Viewport.SizeChanged -= Viewport_SizeChangedForInkSurface;
                Viewport.SizeChanged += Viewport_SizeChangedForInkSurface;
            }

            UpdateInkSurfaceViewportTransform();
            InvalidateInkSurface();
        }

        private void Viewport_SizeChangedForInkSurface(object sender, SizeChangedEventArgs e)
        {
            UpdateInkSurfaceViewportTransform();
            InvalidateInkSurface();
        }

        private void UpdateInkSurfaceViewportSize()
        {
            if (InkSurface == null) return;
            if (Viewport == null) return;

            double w = Viewport.ActualWidth;
            double h = Viewport.ActualHeight;
            if (w <= 0 || h <= 0 || double.IsNaN(w) || double.IsNaN(h) || double.IsInfinity(w) || double.IsInfinity(h))
            {
                return;
            }

            // 如果 XAML 绑定失败，InkSurface 可能会保持 0x0（来源图像初始也是 0），导致 DX/CPU 都无法正确渲染。
            // 这里强制同步一次尺寸到 Viewport。
            if (Math.Abs(_lastSyncedInkSurfaceSize.Width - w) < 0.5 &&
                Math.Abs(_lastSyncedInkSurfaceSize.Height - h) < 0.5)
            {
                return;
            }

            _lastSyncedInkSurfaceSize = new Size(w, h);
            InkSurface.Width = w;
            InkSurface.Height = h;
            Debug.WriteLine($"[InkSurfaceSize] Sync InkSurface to Viewport: {w:F1}x{h:F1}");

            TryForceInkSurfaceArrange(w, h);
        }

        private void TryForceInkSurfaceArrange(double width, double height)
        {
            if (InkSurface == null) return;
            if (width <= 0 || height <= 0) return;

            // 部分机器上 InkSurface 会长期保持 Actual=0x0（即使 Width/Height 已设置），导致整个 ink 层完全不渲染。
            // 这里做一次兜底：当检测到未被正常 Arrange 时，手动 Measure/Arrange 一次。
            if (InkSurface.ActualWidth >= 1 && InkSurface.ActualHeight >= 1)
            {
                return;
            }

            try
            {
                InkSurface.Measure(new Size(width, height));
                InkSurface.Arrange(new Rect(0, 0, width, height));
                Debug.WriteLine($"[InkSurfaceSize] Force Arrange: Actual={InkSurface.ActualWidth:F1}x{InkSurface.ActualHeight:F1}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InkSurfaceSize] Force Arrange failed: {ex}");
            }
        }

        private void UpdateInkSurfaceViewportTransform()
        {
            if (InkSurface == null) return;
            if (_inkSurfaceInverseTranslate == null || _inkSurfaceInverseScale == null) return;
            if (_zoomPanService == null) return;

            UpdateInkSurfaceViewportSize();

            double zoom = _zoomPanService.Zoom;
            if (zoom <= 0) zoom = 1.0;

            _inkSurfaceInverseTranslate.X = -_zoomPanService.PanX;
            _inkSurfaceInverseTranslate.Y = -_zoomPanService.PanY;
            _inkSurfaceInverseScale.ScaleX = 1.0 / zoom;
            _inkSurfaceInverseScale.ScaleY = 1.0 / zoom;
        }

        private void InvalidateInkSurface()
        {
            try
            {
                InkSurface?.InvalidateSurface();
            }
            catch
            {
            }
        }

        private void InkSurface_RenderFrame(object? sender, InkSurfaceRenderEventArgs e)
        {
            if (_inkDxRenderer == null) return;
            if (_pageService?.CurrentPage is not BoardPage page) return;
            if (_zoomPanService == null) return;

            LogInkRendererPath(isCpu: false);

            UpdateInkSurfaceViewportTransform();

            bool isInteracting = _zoomPanService.IsGestureActive || _zoomPanService.IsMousePanning;

            try
            {
                IReadOnlyCollection<InkFragment>? forceVisible = BuildForceVisibleFragments();

                _inkDxRenderer.Render(
                    page.Ink,
                    page.InkSpatialIndex,
                    e.Device,
                    e.RenderTargetTexture,
                    e.PixelWidth,
                    e.PixelHeight,
                    e.DpiScaleX,
                    e.DpiScaleY,
                    _zoomPanService.Zoom,
                    _zoomPanService.PanX,
                    _zoomPanService.PanY,
                    isInteracting,
                    forceVisibleFragments: forceVisible);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InkRender][DX] Exception: {ex}");
                TryLogPostStrokeRenderSummary(page, rendererName: "DX", extra: () =>
                {
                    var dx = _inkDxRenderer;
                    if (dx == null) return;

                    Debug.WriteLine(
                        $"[InkRender][DX] hits={dx.LastSpatialHitCount} visibleFragments={dx.LastVisibleFragmentCount} " +
                        $"forceVisible={dx.LastForceVisibleFragmentCount} selfHealRebuild={dx.LastSelfHealRebuildAttempted} " +
                        $"selfHealAll={dx.LastSelfHealFallbackAllFragments}");
                });

                throw;
            }

            TryLogPostStrokeRenderSummary(page, rendererName: "DX", extra: () =>
            {
                var dx = _inkDxRenderer;
                if (dx == null) return;

                Debug.WriteLine(
                    $"[InkRender][DX] hits={dx.LastSpatialHitCount} visibleFragments={dx.LastVisibleFragmentCount} " +
                    $"forceVisible={dx.LastForceVisibleFragmentCount} selfHealRebuild={dx.LastSelfHealRebuildAttempted} " +
                    $"selfHealAll={dx.LastSelfHealFallbackAllFragments}");
            });
        }

        private void InkSurface_RenderFallbackFrame(object? sender, InkSurfaceFallbackRenderEventArgs e)
        {
            if (_pageService?.CurrentPage is not BoardPage page) return;
            if (_zoomPanService == null) return;

            LogInkRendererPath(isCpu: true);

            UpdateInkSurfaceViewportTransform();

            double zoom = _zoomPanService.Zoom;
            if (zoom <= 0) zoom = 1.0;

            double viewportWidthDip = e.PixelWidth / Math.Max(0.0001, e.DpiScaleX);
            double viewportHeightDip = e.PixelHeight / Math.Max(0.0001, e.DpiScaleY);

            IReadOnlyCollection<InkFragment>? forceVisible = BuildForceVisibleFragments();

            InkRectDip cullRect = InkVisibilityCulling.ComputeWorldCullRect(
                viewportWidthDip,
                viewportHeightDip,
                zoom,
                _zoomPanService.PanX,
                _zoomPanService.PanY,
                cullMarginScreenDip: 24.0);

            InkVisibilityStats visStats = InkVisibilityCulling.GatherVisibleFragments(
                page.Ink,
                page.InkSpatialIndex,
                cullRect,
                _cpuHitScratch,
                _cpuVisibleFragments,
                forceVisible);

            TryLogPostStrokeRenderSummary(page, rendererName: "CPU", extra: () =>
            {
                Debug.WriteLine(
                    $"[InkRender][CPU] hits={visStats.SpatialHitCount} visibleFragments={visStats.VisibleFragmentCount} " +
                    $"forceVisible={visStats.ForceVisibleFragmentCount} selfHealRebuild={visStats.SelfHealRebuildAttempted} " +
                    $"selfHealAll={visStats.SelfHealFallbackAllFragments}");
            });

            try
            {
                var matrix = new Matrix(zoom, 0, 0, zoom, _zoomPanService.PanX, _zoomPanService.PanY);
                e.DrawingContext.PushTransform(new MatrixTransform(matrix));
                InkCpuRenderer.RenderInk(e.DrawingContext, page.Ink, zoom, _cpuVisibleFragments);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[InkRender][CPU] Exception: {ex}");
            }
            finally
            {
                try
                {
                    e.DrawingContext.Pop();
                }
                catch
                {
                }
            }
        }

        private void LogInkRendererPath(bool isCpu)
        {
            if (_lastInkRendererWasCpu == isCpu)
            {
                return;
            }

            _lastInkRendererWasCpu = isCpu;
            Debug.WriteLine($"[InkRender] Renderer={(isCpu ? "CPU" : "DX")}");
        }

        private void TryLogPostStrokeRenderSummary(BoardPage page, string rendererName, Action? extra)
        {
            if (!_inkDebugPendingPostStrokeRenderCheck)
            {
                return;
            }

            _inkDebugPendingPostStrokeRenderCheck = false;

            try
            {
                double zoom = _zoomPanService.Zoom;
                if (zoom <= 0 || double.IsNaN(zoom) || double.IsInfinity(zoom)) zoom = 1.0;

                double viewportW = Viewport?.ActualWidth ?? 0;
                double viewportH = Viewport?.ActualHeight ?? 0;
                double marginWorld = 24.0 / zoom;
                double worldLeft = (0 - _zoomPanService.PanX) / zoom;
                double worldTop = (0 - _zoomPanService.PanY) / zoom;
                double worldWidth = viewportW / zoom;
                double worldHeight = viewportH / zoom;

                var cullRect = new Rect(
                    worldLeft - marginWorld,
                    worldTop - marginWorld,
                    worldWidth + marginWorld * 2,
                    worldHeight + marginWorld * 2);

                Rect inkBounds = InkCpuRenderer.CalculateInkBounds(page.Ink);
                bool intersects = !inkBounds.IsEmpty && cullRect.IntersectsWith(inkBounds);

                string lastStrokeInfo = string.Empty;
                try
                {
                    if (page.Ink.Strokes.Count > 0)
                    {
                        InkStroke lastStroke = page.Ink.Strokes[^1];
                        if (lastStroke.Fragments.Count > 0)
                        {
                            InkFragment lastFragment = lastStroke.Fragments[^1];
                            List<InkPoint> pts = lastFragment.Points;
                            if (pts.Count > 0)
                            {
                                InkPoint p0 = pts[0];
                                InkPoint p1 = pts[^1];
                                lastStrokeInfo =
                                    $" lastStrokePoints={pts.Count} lastFirst=({p0.XDip:F1},{p0.YDip:F1}) lastLast=({p1.XDip:F1},{p1.YDip:F1})";
                            }
                        }
                    }
                }
                catch
                {
                }

                Debug.WriteLine(
                    $"[InkRender][AfterStroke] renderer={rendererName} contentVersion={page.ContentVersion} " +
                    $"strokes={page.Ink.Strokes.Count} (expectedStrokes={_inkDebugPendingPostStrokeCount}) " +
                    $"spatialSegments={page.InkSpatialIndex.SegmentCount} spatialCells={page.InkSpatialIndex.CellCount} " +
                    $"zoom={zoom:F3} pan=({_zoomPanService.PanX:F1},{_zoomPanService.PanY:F1}) " +
                    $"viewportDip=({Viewport?.ActualWidth:F1},{Viewport?.ActualHeight:F1}) " +
                    $"offset=({Viewport?.HorizontalOffset:F1},{Viewport?.VerticalOffset:F1}) " +
                    $"inkSurfaceDip=({InkSurface?.ActualWidth:F1},{InkSurface?.ActualHeight:F1}) " +
                    $"cache={(Viewport?.CacheMode != null)} " +
                    $"cullRect=({cullRect.X:F1},{cullRect.Y:F1},{cullRect.Width:F1},{cullRect.Height:F1}) " +
                    $"inkBounds=({inkBounds.X:F1},{inkBounds.Y:F1},{inkBounds.Width:F1},{inkBounds.Height:F1}) " +
                    $"intersects={intersects}{lastStrokeInfo}");

                extra?.Invoke();
            }
            catch
            {
            }
        }

        private void DebugMarkPostStrokeRenderCheck(BoardPage? page)
        {
            if (page == null)
            {
                return;
            }

            _inkDebugPendingPostStrokeRenderCheck = true;
            _inkDebugPendingPostStrokeCount = page.Ink.Strokes.Count;
        }

        private IReadOnlyCollection<InkFragment>? BuildForceVisibleFragments()
        {
            _forceVisibleFragments.Clear();

            if (_inkMode?.HasActiveStroke == true)
            {
                _inkMode.CollectActiveFragments(_forceVisibleFragments);
            }

            if (_selectedInkStrokes.Count > 0)
            {
                for (int si = 0; si < _selectedInkStrokes.Count; si++)
                {
                    InkStroke stroke = _selectedInkStrokes[si];
                    for (int fi = 0; fi < stroke.Fragments.Count; fi++)
                    {
                        _forceVisibleFragments.Add(stroke.Fragments[fi]);
                    }
                }
            }

            return _forceVisibleFragments.Count == 0 ? null : _forceVisibleFragments;
        }

        private void SetInkSurfaceEnabled(bool enabled)
        {
            if (InkSurface == null) return;

            InkSurface.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            if (enabled)
            {
                UpdateInkSurfaceViewportTransform();
                InvalidateInkSurface();
            }
        }
    }
}
