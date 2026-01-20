using System;
using System.Collections.Generic;
using System.Numerics;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using WindBoard.Models.InkV2;
using InkPointV2 = WindBoard.Models.InkV2.InkPoint;

namespace WindBoard.Services.InkV2.Rendering
{
    internal sealed class InkDxRenderer : IDisposable
    {
        private const float DefaultDpi = 96.0f;
        private const float DpiEpsilon = 0.01f;
        private const double DefaultCullMarginScreenDip = 24.0;
        private const double LodScreenErrorPixelsIdle = 0.75;
        private const double LodScreenErrorPixelsInteracting = 1.5;
        private const double LodQuantizeWorldDip = 0.25;

        private ID2D1Factory1? _factory;
        private ID2D1Device? _d2dDevice;
        private ID2D1DeviceContext? _d2dContext;
        private ID2D1Bitmap1? _d2dTargetBitmap;
        private ID2D1StrokeStyle? _strokeStyle;

        private ID3D11Device? _boundD3dDevice;
        private ID3D11Texture2D? _boundD3dTarget;
        private float _dpiX = DefaultDpi;
        private float _dpiY = DefaultDpi;

        private readonly Dictionary<uint, ID2D1SolidColorBrush> _solidBrushCache = new();
        private readonly Dictionary<InkFragment, FragmentCache> _fragmentCache = new(ReferenceEqualityComparer<InkFragment>.Instance);
        private readonly List<InkSegmentHit> _hitScratch = new(2048);
        private readonly HashSet<InkFragment> _visibleFragments = new();

        internal int LastSpatialHitCount { get; private set; }
        internal int LastVisibleFragmentCount { get; private set; }
        internal int LastForceVisibleFragmentCount { get; private set; }
        internal bool LastSelfHealRebuildAttempted { get; private set; }
        internal bool LastSelfHealFallbackAllFragments { get; private set; }

        public void Render(
            InkDocument document,
            InkSpatialIndex spatialIndex,
            ID3D11Device d3dDevice,
            ID3D11Texture2D d3dTarget,
            int pixelWidth,
            int pixelHeight,
            double dpiScaleX,
            double dpiScaleY,
            double zoom,
            double panXDip,
            double panYDip,
            bool isInteracting,
            IReadOnlyCollection<InkFragment>? forceVisibleFragments = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (spatialIndex == null) throw new ArgumentNullException(nameof(spatialIndex));
            if (d3dDevice == null) throw new ArgumentNullException(nameof(d3dDevice));
            if (d3dTarget == null) throw new ArgumentNullException(nameof(d3dTarget));
            if (pixelWidth <= 0 || pixelHeight <= 0) return;
            if (zoom <= 0) zoom = 1.0;

            EnsureDeviceResources(d3dDevice, d3dTarget, dpiScaleX, dpiScaleY);

            if (_d2dContext == null || _d2dTargetBitmap == null)
            {
                return;
            }

            double viewportWidthDip = pixelWidth / Math.Max(0.0001, dpiScaleX);
            double viewportHeightDip = pixelHeight / Math.Max(0.0001, dpiScaleY);

            InkRectDip cullRect = InkVisibilityCulling.ComputeWorldCullRect(
                viewportWidthDip,
                viewportHeightDip,
                zoom,
                panXDip,
                panYDip,
                cullMarginScreenDip: DefaultCullMarginScreenDip);

            InkVisibilityStats visStats = InkVisibilityCulling.GatherVisibleFragments(
                document,
                spatialIndex,
                cullRect,
                _hitScratch,
                _visibleFragments,
                forceVisibleFragments);

            LastSpatialHitCount = visStats.SpatialHitCount;
            LastVisibleFragmentCount = visStats.VisibleFragmentCount;
            LastForceVisibleFragmentCount = visStats.ForceVisibleFragmentCount;
            LastSelfHealRebuildAttempted = visStats.SelfHealRebuildAttempted;
            LastSelfHealFallbackAllFragments = visStats.SelfHealFallbackAllFragments;

            var dc = _d2dContext;
            dc.Target = _d2dTargetBitmap;
            dc.AntialiasMode = AntialiasMode.PerPrimitive;
            dc.TextAntialiasMode = TextAntialiasMode.Grayscale;

            var worldToViewport = Matrix3x2.CreateScale((float)zoom) * Matrix3x2.CreateTranslation((float)panXDip, (float)panYDip);

            double dpiScale = (dpiScaleX + dpiScaleY) * 0.5;
            double lodErrorPx = isInteracting ? LodScreenErrorPixelsInteracting : LodScreenErrorPixelsIdle;
            double lodErrorDip = lodErrorPx / Math.Max(0.0001, dpiScale);
            double worldToleranceDip = lodErrorDip / zoom;
            double quantizedToleranceDip = QuantizeDown(worldToleranceDip, LodQuantizeWorldDip);

            try
            {
                dc.BeginDraw();
                dc.Transform = Matrix3x2.Identity;
                dc.Clear(new Color4(0, 0, 0, 0));

                dc.Transform = worldToViewport;

                for (int si = 0; si < document.Strokes.Count; si++)
                {
                    InkStroke stroke = document.Strokes[si];
                    InkTool tool = stroke.Tool;

                    ID2D1SolidColorBrush brush = GetSolidBrush(tool.ColorArgb);

                    float strokeWidthWorldDip = (float)ComputeStrokeWidthWorldDip(tool, zoom);
                    if (strokeWidthWorldDip <= 0.001f)
                    {
                        strokeWidthWorldDip = 0.001f;
                    }

                    bool allowLod = quantizedToleranceDip > 0 && IsLodAllowed(tool, zoom, isInteracting);

                    for (int fi = 0; fi < stroke.Fragments.Count; fi++)
                    {
                        InkFragment fragment = stroke.Fragments[fi];
                        if (!_visibleFragments.Contains(fragment))
                        {
                            continue;
                        }

                        ID2D1PathGeometry? geometry = GetFragmentGeometry(fragment, allowLod ? quantizedToleranceDip : 0);
                        if (geometry == null)
                        {
                            continue;
                        }

                        dc.DrawGeometry(geometry, brush, strokeWidthWorldDip, _strokeStyle);
                    }
                }
            }
            finally
            {
                try
                {
                    _ = dc.EndDraw();
                }
                catch
                {
                }
            }
        }

        private void EnsureDeviceResources(ID3D11Device d3dDevice, ID3D11Texture2D d3dTarget, double dpiScaleX, double dpiScaleY)
        {
            float dpiX = (float)(DefaultDpi * dpiScaleX);
            float dpiY = (float)(DefaultDpi * dpiScaleY);
            if (dpiX <= 1) dpiX = DefaultDpi;
            if (dpiY <= 1) dpiY = DefaultDpi;

            bool deviceChanged = !ReferenceEquals(_boundD3dDevice, d3dDevice);
            bool targetChanged = !ReferenceEquals(_boundD3dTarget, d3dTarget);
            bool dpiChanged = Math.Abs(dpiX - _dpiX) > DpiEpsilon || Math.Abs(dpiY - _dpiY) > DpiEpsilon;

            if (_factory == null)
            {
                _factory = CreateFactory();
            }

            if (deviceChanged)
            {
                DisposeDeviceDependentResources();

                _boundD3dDevice = d3dDevice;

                using var dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
                _d2dDevice = _factory.CreateDevice(dxgiDevice);
                _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

                _strokeStyle = _factory.CreateStrokeStyle(
                    new StrokeStyleProperties
                    {
                        StartCap = CapStyle.Round,
                        EndCap = CapStyle.Round,
                        DashCap = CapStyle.Round,
                        LineJoin = LineJoin.Round,
                        MiterLimit = 1.0f,
                        DashStyle = DashStyle.Solid,
                        DashOffset = 0
                    });
            }

            if (deviceChanged || targetChanged || dpiChanged)
            {
                _boundD3dTarget = d3dTarget;
                _dpiX = dpiX;
                _dpiY = dpiY;

                _d2dTargetBitmap?.Dispose();
                _d2dTargetBitmap = null;

                if (_d2dContext == null)
                {
                    return;
                }

                _d2dContext.SetDpi(_dpiX, _dpiY);

                using var surface = d3dTarget.QueryInterface<IDXGISurface>();
                _d2dTargetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(
                    surface,
                    new BitmapProperties1(
                        new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                        _dpiX,
                        _dpiY,
                        BitmapOptions.Target | BitmapOptions.CannotDraw));

                if (targetChanged || dpiChanged)
                {
                    DisposeBrushCache();
                }
            }
        }

        private ID2D1SolidColorBrush GetSolidBrush(uint argb)
        {
            if (_d2dContext == null) throw new InvalidOperationException("D2D context is not initialized.");

            if (_solidBrushCache.TryGetValue(argb, out var existing))
            {
                return existing;
            }

            var color = new Color4(
                ((argb >> 16) & 0xFF) / 255.0f,
                ((argb >> 8) & 0xFF) / 255.0f,
                (argb & 0xFF) / 255.0f,
                ((argb >> 24) & 0xFF) / 255.0f);

            ID2D1SolidColorBrush brush = _d2dContext.CreateSolidColorBrush(color);
            _solidBrushCache.Add(argb, brush);
            return brush;
        }

        private ID2D1PathGeometry? GetFragmentGeometry(InkFragment fragment, double lodToleranceDip)
        {
            if (fragment == null) return null;
            if (_factory == null) return null;

            if (!_fragmentCache.TryGetValue(fragment, out var cache))
            {
                cache = new FragmentCache();
                _fragmentCache.Add(fragment, cache);
            }

            int pointCount = fragment.Points.Count;
            int pointsVersion = fragment.PointsVersion;
            if (pointCount < 2)
            {
                return null;
            }

            if (lodToleranceDip <= 0)
            {
                if (cache.FullGeometry == null || cache.FullPointCount != pointCount || cache.FullPointsVersion != pointsVersion)
                {
                    cache.FullGeometry?.Dispose();
                    cache.FullGeometry = BuildPolylineGeometry(fragment.Points);
                    cache.FullPointCount = pointCount;
                    cache.FullPointsVersion = pointsVersion;
                }
                return cache.FullGeometry;
            }

            if (cache.LodGeometry == null ||
                cache.LodPointCount != pointCount ||
                cache.LodPointsVersion != pointsVersion ||
                Math.Abs(cache.LodToleranceDip - lodToleranceDip) > 0.0001)
            {
                cache.LodGeometry?.Dispose();
                cache.LodGeometry = BuildLodPolylineGeometry(fragment.Points, lodToleranceDip);
                cache.LodPointCount = pointCount;
                cache.LodPointsVersion = pointsVersion;
                cache.LodToleranceDip = lodToleranceDip;
            }

            return cache.LodGeometry ?? cache.FullGeometry;
        }

        private ID2D1PathGeometry? BuildPolylineGeometry(List<InkPointV2> points)
        {
            if (_factory == null) return null;
            if (points.Count < 2) return null;

            var geometry = _factory.CreatePathGeometry();
            using var sink = geometry.Open();

            int firstIndex = FindFirstDistinctPointIndex(points);
            if (firstIndex < 0)
            {
                geometry.Dispose();
                return null;
            }

            var p0 = points[firstIndex];
            sink.BeginFigure(new Vector2((float)p0.XDip, (float)p0.YDip), FigureBegin.Hollow);

            double lastX = p0.XDip;
            double lastY = p0.YDip;

            for (int i = firstIndex + 1; i < points.Count; i++)
            {
                InkPointV2 p = points[i];
                if (Math.Abs(p.XDip - lastX) <= 1e-9 && Math.Abs(p.YDip - lastY) <= 1e-9)
                {
                    continue;
                }

                sink.AddLine(new Vector2((float)p.XDip, (float)p.YDip));
                lastX = p.XDip;
                lastY = p.YDip;
            }

            sink.EndFigure(FigureEnd.Open);
            sink.Close();
            return geometry;
        }

        private ID2D1PathGeometry? BuildLodPolylineGeometry(List<InkPointV2> points, double toleranceDip)
        {
            if (toleranceDip <= 0)
            {
                return BuildPolylineGeometry(points);
            }

            if (_factory == null) return null;
            int count = points.Count;
            if (count < 2) return null;

            double tol2 = toleranceDip * toleranceDip;
            var keep = new bool[count];
            keep[0] = true;
            keep[count - 1] = true;

            var stack = new Stack<(int Start, int End)>();
            stack.Push((0, count - 1));

            while (stack.Count > 0)
            {
                (int start, int end) = stack.Pop();
                if (end <= start + 1) continue;

                InkPointV2 a = points[start];
                InkPointV2 b = points[end];

                double maxD2 = 0;
                int maxIndex = -1;

                for (int i = start + 1; i < end; i++)
                {
                    InkPointV2 p = points[i];
                    double d2 = DistancePointToSegmentSquared(p.XDip, p.YDip, a.XDip, a.YDip, b.XDip, b.YDip);
                    if (d2 > maxD2)
                    {
                        maxD2 = d2;
                        maxIndex = i;
                    }
                }

                if (maxIndex >= 0 && maxD2 > tol2)
                {
                    keep[maxIndex] = true;
                    stack.Push((start, maxIndex));
                    stack.Push((maxIndex, end));
                }
            }

            var geometry = _factory.CreatePathGeometry();
            using var sink = geometry.Open();

            int first = -1;
            for (int i = 0; i < count; i++)
            {
                if (keep[i])
                {
                    first = i;
                    break;
                }
            }

            if (first < 0)
            {
                geometry.Dispose();
                return null;
            }

            InkPointV2 p0 = points[first];
            sink.BeginFigure(new Vector2((float)p0.XDip, (float)p0.YDip), FigureBegin.Hollow);

            double lastX = p0.XDip;
            double lastY = p0.YDip;

            for (int i = first + 1; i < count; i++)
            {
                if (!keep[i]) continue;

                InkPointV2 p = points[i];
                if (Math.Abs(p.XDip - lastX) <= 1e-9 && Math.Abs(p.YDip - lastY) <= 1e-9)
                {
                    continue;
                }

                sink.AddLine(new Vector2((float)p.XDip, (float)p.YDip));
                lastX = p.XDip;
                lastY = p.YDip;
            }

            sink.EndFigure(FigureEnd.Open);
            sink.Close();
            return geometry;
        }

        private static int FindFirstDistinctPointIndex(List<InkPointV2> points)
        {
            if (points.Count < 2) return -1;
            InkPointV2 a = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                InkPointV2 b = points[i];
                if (Math.Abs(a.XDip - b.XDip) > 1e-9 || Math.Abs(a.YDip - b.YDip) > 1e-9)
                {
                    return 0;
                }
            }
            return -1;
        }

        private static double ComputeStrokeWidthWorldDip(InkTool tool, double zoom)
        {
            if (zoom <= 0) zoom = 1.0;

            double baseThickness = tool.BaseThicknessDip;
            if (baseThickness <= 0 || double.IsNaN(baseThickness) || double.IsInfinity(baseThickness))
            {
                baseThickness = 1.0;
            }

            return tool.ThicknessSemantics == InkThicknessSemantics.ViewInvariant
                ? baseThickness / zoom
                : baseThickness;
        }

        private static bool IsLodAllowed(InkTool tool, double zoom, bool isInteracting)
        {
            if (zoom <= 0) zoom = 1.0;

            if (isInteracting)
            {
                return zoom < 1.25;
            }

            return zoom < 0.85;
        }

        private static double QuantizeDown(double value, double step)
        {
            if (value <= 0 || step <= 0) return 0;
            return Math.Floor(value / step) * step;
        }

        private static double DistancePointToSegmentSquared(double px, double py, double ax, double ay, double bx, double by)
        {
            double abx = bx - ax;
            double aby = by - ay;
            double abLen2 = (abx * abx) + (aby * aby);
            if (abLen2 <= 1e-12)
            {
                double dx0 = px - ax;
                double dy0 = py - ay;
                return (dx0 * dx0) + (dy0 * dy0);
            }

            double apx = px - ax;
            double apy = py - ay;
            double t = ((apx * abx) + (apy * aby)) / abLen2;
            t = Math.Clamp(t, 0.0, 1.0);

            double cx = ax + (abx * t);
            double cy = ay + (aby * t);
            double dx = px - cx;
            double dy = py - cy;
            return (dx * dx) + (dy * dy);
        }

        private static ID2D1Factory1 CreateFactory()
        {
            var options = new FactoryOptions
            {
                DebugLevel = DebugLevel.None
            };

            return D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded, options);
        }

        private void DisposeBrushCache()
        {
            foreach (var kv in _solidBrushCache)
            {
                try
                {
                    kv.Value.Dispose();
                }
                catch
                {
                }
            }
            _solidBrushCache.Clear();
        }

        private void DisposeDeviceDependentResources()
        {
            DisposeBrushCache();

            try
            {
                _strokeStyle?.Dispose();
            }
            catch
            {
            }
            _strokeStyle = null;

            try
            {
                _d2dTargetBitmap?.Dispose();
            }
            catch
            {
            }
            _d2dTargetBitmap = null;

            try
            {
                _d2dContext?.Dispose();
            }
            catch
            {
            }
            _d2dContext = null;

            try
            {
                _d2dDevice?.Dispose();
            }
            catch
            {
            }
            _d2dDevice = null;
        }

        public void Dispose()
        {
            DisposeDeviceDependentResources();

            foreach (var kv in _fragmentCache)
            {
                try
                {
                    kv.Value.Dispose();
                }
                catch
                {
                }
            }
            _fragmentCache.Clear();

            try
            {
                _factory?.Dispose();
            }
            catch
            {
            }
            _factory = null;
        }

        private sealed class FragmentCache : IDisposable
        {
            public int FullPointCount { get; set; }
            public int FullPointsVersion { get; set; }
            public ID2D1PathGeometry? FullGeometry { get; set; }
            public int LodPointCount { get; set; }
            public int LodPointsVersion { get; set; }
            public double LodToleranceDip { get; set; }
            public ID2D1PathGeometry? LodGeometry { get; set; }

            public void Dispose()
            {
                FullGeometry?.Dispose();
                FullGeometry = null;
                LodGeometry?.Dispose();
                LodGeometry = null;
            }
        }
    }

    internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static ReferenceEqualityComparer<T> Instance { get; } = new ReferenceEqualityComparer<T>();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
