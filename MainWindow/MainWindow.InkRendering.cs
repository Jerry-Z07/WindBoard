using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using WindBoard.Controls;
using WindBoard.Models.InkV2;
using WindBoard.Services.InkV2.Rendering;

namespace WindBoard
{
    public partial class MainWindow
    {
        private InkDxRenderer? _inkDxRenderer;
        private TranslateTransform? _inkSurfaceInverseTranslate;
        private ScaleTransform? _inkSurfaceInverseScale;
        private readonly List<InkFragment> _forceVisibleFragments = new(256);

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

            UpdateInkSurfaceViewportTransform();
            InvalidateInkSurface();
        }

        private void UpdateInkSurfaceViewportTransform()
        {
            if (InkSurface == null) return;
            if (_inkSurfaceInverseTranslate == null || _inkSurfaceInverseScale == null) return;
            if (_zoomPanService == null) return;

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
            catch
            {
            }
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
