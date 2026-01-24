using System;
using System.Collections.Generic;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using WindBoard.Board;
using WindBoard.Board.Viewport;

namespace WindBoard.Rendering.Board
{
    internal sealed class BoardSceneRenderer : IDisposable
    {
        private ID2D1Factory1? _factory;
        private ID2D1SolidColorBrush? _strokeBrush;
        private ID2D1StrokeStyle? _strokeStyle;
        private ID2D1InkStyle? _inkStyle;
        private readonly Dictionary<Stroke, StrokeInkCacheEntry> _inkCache = new();
        private ID2D1SolidColorBrush? _gridMinorBrush;
        private ID2D1SolidColorBrush? _gridMajorBrush;
        private ID2D1SolidColorBrush? _axisBrush;

        public void Draw(ID2D1DeviceContext ctx, BoardDocument document, Stroke? activeStroke, BoardViewport viewport)
        {
            _strokeBrush ??= ctx.CreateSolidColorBrush(new Color4(0, 0, 0, 1));
            _gridMinorBrush ??= ctx.CreateSolidColorBrush(new Color4(0.92f, 0.92f, 0.92f, 1.0f));
            _gridMajorBrush ??= ctx.CreateSolidColorBrush(new Color4(0.86f, 0.86f, 0.86f, 1.0f));
            _axisBrush ??= ctx.CreateSolidColorBrush(new Color4(0.78f, 0.78f, 0.78f, 1.0f));

            using ID2D1DeviceContext2? ctx2 = TryGetDeviceContext2(ctx);
            EnsureInkStyle(ctx2);
            PruneInkCache(document, activeStroke);

            Matrix3x2 oldTransform = ctx.Transform;
            ctx.Transform = viewport.GetWorldToScreenTransform();

            viewport.GetVisibleWorldBounds(out Vector2 visibleMinWorld, out Vector2 visibleMaxWorld);

            DrawInfiniteGrid(ctx, viewport, visibleMinWorld, visibleMaxWorld);

            foreach (var stroke in document.Strokes)
            {
                if (!IsStrokeVisible(stroke, visibleMinWorld, visibleMaxWorld))
                {
                    continue;
                }

                DrawStroke(ctx, ctx2, stroke);
            }

            if (activeStroke is not null)
            {
                if (IsStrokeVisible(activeStroke, visibleMinWorld, visibleMaxWorld))
                {
                    DrawStroke(ctx, ctx2, activeStroke);
                }
            }

            ctx.Transform = oldTransform;
        }

        public void DrawBackground(ID2D1DeviceContext ctx, BoardDocument document, BoardViewport viewport)
        {
            _strokeBrush ??= ctx.CreateSolidColorBrush(new Color4(0, 0, 0, 1));
            _gridMinorBrush ??= ctx.CreateSolidColorBrush(new Color4(0.92f, 0.92f, 0.92f, 1.0f));
            _gridMajorBrush ??= ctx.CreateSolidColorBrush(new Color4(0.86f, 0.86f, 0.86f, 1.0f));
            _axisBrush ??= ctx.CreateSolidColorBrush(new Color4(0.78f, 0.78f, 0.78f, 1.0f));

            using ID2D1DeviceContext2? ctx2 = TryGetDeviceContext2(ctx);
            EnsureInkStyle(ctx2);
            PruneInkCache(document, null);

            Matrix3x2 oldTransform = ctx.Transform;
            ctx.Transform = viewport.GetWorldToScreenTransform();

            viewport.GetVisibleWorldBounds(out Vector2 visibleMinWorld, out Vector2 visibleMaxWorld);

            DrawInfiniteGrid(ctx, viewport, visibleMinWorld, visibleMaxWorld);

            foreach (var stroke in document.Strokes)
            {
                if (!IsStrokeVisible(stroke, visibleMinWorld, visibleMaxWorld))
                {
                    continue;
                }

                DrawStroke(ctx, ctx2, stroke);
            }

            ctx.Transform = oldTransform;
        }

        public void DrawActiveStroke(ID2D1DeviceContext ctx, Stroke activeStroke, BoardViewport viewport)
        {
            _strokeBrush ??= ctx.CreateSolidColorBrush(new Color4(0, 0, 0, 1));

            using ID2D1DeviceContext2? ctx2 = TryGetDeviceContext2(ctx);
            EnsureInkStyle(ctx2);

            Matrix3x2 oldTransform = ctx.Transform;
            ctx.Transform = viewport.GetWorldToScreenTransform();

            viewport.GetVisibleWorldBounds(out Vector2 visibleMinWorld, out Vector2 visibleMaxWorld);
            if (IsStrokeVisible(activeStroke, visibleMinWorld, visibleMaxWorld))
            {
                DrawStroke(ctx, ctx2, activeStroke);
            }

            ctx.Transform = oldTransform;
        }

        private void DrawStroke(ID2D1DeviceContext ctx, ID2D1DeviceContext2? ctx2, Stroke stroke)
        {
            if (_strokeBrush is null)
            {
                return;
            }

            if (stroke.Points.Count == 0)
            {
                return;
            }

            _strokeBrush.Color = stroke.Color;

            if (stroke.Points.Count == 1)
            {
                float radius = Math.Max(0.5f, stroke.BaseSize * GetStrokeWidthFactor(stroke.Points[0].Pressure) / 2.0f);
                ctx.FillEllipse(new Ellipse(stroke.Points[0].Position, radius, radius), _strokeBrush);
                return;
            }

            if (ctx2 is not null && TryDrawInkStroke(ctx2, stroke))
            {
                ctx2.DrawInk(_inkCache[stroke].Ink, _strokeBrush, _inkStyle);
                return;
            }

            EnsureStrokeStyle(ctx);

            for (int i = 1; i < stroke.Points.Count; i++)
            {
                StrokePoint p0 = stroke.Points[i - 1];
                StrokePoint p1 = stroke.Points[i];

                float widthFactor = stroke.EnablePressure
                    ? GetStrokeWidthFactor((p0.Pressure + p1.Pressure) / 2.0f)
                    : 1.0f;

                float strokeWidth = Math.Max(0.5f, stroke.BaseSize * widthFactor);
                ctx.DrawLine(p0.Position, p1.Position, _strokeBrush, strokeWidth, _strokeStyle);
            }
        }

        private static ID2D1DeviceContext2? TryGetDeviceContext2(ID2D1DeviceContext ctx)
        {
            try
            {
                return ctx.QueryInterface<ID2D1DeviceContext2>();
            }
            catch
            {
                return null;
            }
        }

        private void EnsureInkStyle(ID2D1DeviceContext2? ctx2)
        {
            if (_inkStyle is not null || ctx2 is null)
            {
                return;
            }

            var props = new InkStyleProperties
            {
                NibShape = InkNibShape.Round,
                NibTransform = Matrix3x2.Identity,
            };
            _inkStyle = ctx2.CreateInkStyle(props);
        }

        private void EnsureStrokeStyle(ID2D1DeviceContext ctx)
        {
            if (_strokeStyle is not null)
            {
                return;
            }

            _factory ??= D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded, DebugLevel.None);

            var props = new StrokeStyleProperties
            {
                StartCap = CapStyle.Round,
                EndCap = CapStyle.Round,
                DashCap = CapStyle.Round,
                LineJoin = LineJoin.Round,
                MiterLimit = 2.0f,
                DashStyle = DashStyle.Solid,
                DashOffset = 0.0f,
            };

            _strokeStyle = _factory.CreateStrokeStyle(props);
        }

        private void PruneInkCache(BoardDocument document, Stroke? activeStroke)
        {
            if (_inkCache.Count == 0)
            {
                return;
            }

            var live = new HashSet<Stroke>(document.Strokes);
            if (activeStroke is not null)
            {
                live.Add(activeStroke);
            }

            List<Stroke>? toRemove = null;
            foreach (var kv in _inkCache)
            {
                if (!live.Contains(kv.Key))
                {
                    toRemove ??= new List<Stroke>();
                    toRemove.Add(kv.Key);
                }
            }

            if (toRemove is null)
            {
                return;
            }

            foreach (Stroke stroke in toRemove)
            {
                if (_inkCache.Remove(stroke, out StrokeInkCacheEntry? entry))
                {
                    entry.Dispose();
                }
            }
        }

        private bool TryDrawInkStroke(ID2D1DeviceContext2 ctx2, Stroke stroke)
        {
            if (stroke.Points.Count < 2)
            {
                return false;
            }

            if (!_inkCache.TryGetValue(stroke, out StrokeInkCacheEntry? entry))
            {
                entry = StrokeInkCacheEntry.Create(ctx2, stroke);
                _inkCache[stroke] = entry;
                return true;
            }

            int pointCount = stroke.Points.Count;
            if (pointCount == entry.PointCount)
            {
                return true;
            }

            if (pointCount < entry.PointCount)
            {
                entry.Dispose();
                _inkCache.Remove(stroke);
                entry = StrokeInkCacheEntry.Create(ctx2, stroke);
                _inkCache[stroke] = entry;
                return true;
            }

            entry.AppendSegments(stroke, entry.PointCount);
            entry.PointCount = pointCount;
            return true;
        }

        private sealed class StrokeInkCacheEntry : IDisposable
        {
            public StrokeInkCacheEntry(ID2D1Ink ink, int pointCount)
            {
                Ink = ink;
                PointCount = pointCount;
            }

            public ID2D1Ink Ink { get; }

            public int PointCount { get; set; }

            public static StrokeInkCacheEntry Create(ID2D1DeviceContext2 ctx2, Stroke stroke)
            {
                InkPoint start = CreateInkPoint(stroke, stroke.Points[0]);
                ID2D1Ink ink = ctx2.CreateInk(start);

                var entry = new StrokeInkCacheEntry(ink, stroke.Points.Count);

                if (stroke.Points.Count > 1)
                {
                    entry.AppendSegments(stroke, 1);
                }

                return entry;
            }

            public void AppendSegments(Stroke stroke, int startPointIndex)
            {
                int pointCount = stroke.Points.Count;
                int clampedStart = Math.Max(1, startPointIndex);
                if (clampedStart >= pointCount)
                {
                    return;
                }

                int segmentCount = pointCount - clampedStart;
                var segments = new InkBezierSegment[segmentCount];
                for (int i = clampedStart; i < pointCount; i++)
                {
                    StrokePoint p0 = stroke.Points[i - 1];
                    StrokePoint p1 = stroke.Points[i];
                    segments[i - clampedStart] = CreateInkSegment(stroke, p0, p1);
                }

                Ink.AddSegments(segments, (uint)segments.Length);
            }

            public void Dispose()
            {
                Ink.Dispose();
            }
        }

        private static InkPoint CreateInkPoint(Stroke stroke, StrokePoint point)
        {
            float widthFactor = stroke.EnablePressure ? GetStrokeWidthFactor(point.Pressure) : 1.0f;
            float diameter = Math.Max(0.5f, stroke.BaseSize * widthFactor);
            float radius = diameter / 2.0f;

            return new InkPoint
            {
                X = point.Position.X,
                Y = point.Position.Y,
                Radius = radius,
            };
        }

        private static InkBezierSegment CreateInkSegment(Stroke stroke, StrokePoint p0, StrokePoint p1)
        {
            Vector2 startPos = p0.Position;
            Vector2 endPos = p1.Position;
            Vector2 delta = endPos - startPos;

            Vector2 c1Pos = startPos + delta / 3.0f;
            Vector2 c2Pos = startPos + delta * 2.0f / 3.0f;

            float r0 = CreateInkPoint(stroke, p0).Radius;
            float r3 = CreateInkPoint(stroke, p1).Radius;
            float r1 = r0 + (r3 - r0) / 3.0f;
            float r2 = r0 + (r3 - r0) * 2.0f / 3.0f;

            return new InkBezierSegment
            {
                Point1 = new InkPoint { X = c1Pos.X, Y = c1Pos.Y, Radius = r1 },
                Point2 = new InkPoint { X = c2Pos.X, Y = c2Pos.Y, Radius = r2 },
                Point3 = new InkPoint { X = endPos.X, Y = endPos.Y, Radius = r3 },
            };
        }

        private void DrawInfiniteGrid(ID2D1DeviceContext ctx, BoardViewport viewport, Vector2 visibleMinWorld, Vector2 visibleMaxWorld)
        {
            if (_gridMinorBrush is null || _gridMajorBrush is null || _axisBrush is null)
            {
                return;
            }

            float minX = visibleMinWorld.X;
            float maxX = visibleMaxWorld.X;
            float minY = visibleMinWorld.Y;
            float maxY = visibleMaxWorld.Y;

            float step = GetAdaptiveGridStepWorld(viewport.Zoom);
            if (step <= 0.0f)
            {
                return;
            }

            const int majorEvery = 5;
            float minorThicknessWorld = 1.0f / Math.Max(0.0001f, viewport.Zoom);
            float majorThicknessWorld = 1.5f / Math.Max(0.0001f, viewport.Zoom);
            float axisThicknessWorld = 2.0f / Math.Max(0.0001f, viewport.Zoom);

            long firstX = (long)Math.Floor(minX / step);
            long lastX = (long)Math.Ceiling(maxX / step);
            long firstY = (long)Math.Floor(minY / step);
            long lastY = (long)Math.Ceiling(maxY / step);

            for (long ix = firstX; ix <= lastX; ix++)
            {
                float x = (float)(ix * step);
                bool isMajor = ix % majorEvery == 0;
                ctx.DrawLine(
                    new Vector2(x, minY),
                    new Vector2(x, maxY),
                    isMajor ? _gridMajorBrush : _gridMinorBrush,
                    isMajor ? majorThicknessWorld : minorThicknessWorld);
            }

            for (long iy = firstY; iy <= lastY; iy++)
            {
                float y = (float)(iy * step);
                bool isMajor = iy % majorEvery == 0;
                ctx.DrawLine(
                    new Vector2(minX, y),
                    new Vector2(maxX, y),
                    isMajor ? _gridMajorBrush : _gridMinorBrush,
                    isMajor ? majorThicknessWorld : minorThicknessWorld);
            }

            // 世界坐标原点轴（用于方向感）
            if (0.0f >= minX && 0.0f <= maxX)
            {
                ctx.DrawLine(new Vector2(0.0f, minY), new Vector2(0.0f, maxY), _axisBrush, axisThicknessWorld);
            }

            if (0.0f >= minY && 0.0f <= maxY)
            {
                ctx.DrawLine(new Vector2(minX, 0.0f), new Vector2(maxX, 0.0f), _axisBrush, axisThicknessWorld);
            }
        }

        private static float GetAdaptiveGridStepWorld(float zoom)
        {
            // 基准：zoom=1 时每 40 DIP 一格。根据缩放自适应，保证屏幕上网格密度大致稳定。
            float step = 40.0f;
            float stepScreen = step * zoom;

            while (stepScreen < 20.0f)
            {
                step *= 2.0f;
                stepScreen = step * zoom;
            }

            while (stepScreen > 80.0f)
            {
                step /= 2.0f;
                stepScreen = step * zoom;
            }

            return step;
        }

        private static float GetStrokeWidthFactor(float normalizedPressure)
        {
            return Math.Clamp(normalizedPressure, 0.1f, 1.0f);
        }

        private static bool IsStrokeVisible(Stroke stroke, Vector2 visibleMinWorld, Vector2 visibleMaxWorld)
        {
            if (stroke.Points.Count == 0)
            {
                return false;
            }

            if (!stroke.HasBounds)
            {
                return true;
            }

            return IntersectsAabb(stroke.BoundsMin, stroke.BoundsMax, visibleMinWorld, visibleMaxWorld);
        }

        private static bool IntersectsAabb(Vector2 aMin, Vector2 aMax, Vector2 bMin, Vector2 bMax)
        {
            return aMin.X <= bMax.X
                && aMax.X >= bMin.X
                && aMin.Y <= bMax.Y
                && aMax.Y >= bMin.Y;
        }

        public void Dispose()
        {
            _strokeBrush?.Dispose();
            _strokeBrush = null;

            _strokeStyle?.Dispose();
            _strokeStyle = null;

            _inkStyle?.Dispose();
            _inkStyle = null;

            foreach (var entry in _inkCache.Values)
            {
                entry.Dispose();
            }
            _inkCache.Clear();

            _factory?.Dispose();
            _factory = null;

            _gridMinorBrush?.Dispose();
            _gridMinorBrush = null;

            _gridMajorBrush?.Dispose();
            _gridMajorBrush = null;

            _axisBrush?.Dispose();
            _axisBrush = null;
        }
    }
}
