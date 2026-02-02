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
        private readonly struct VisibleWorldBounds
        {
            public VisibleWorldBounds(Vector2 min, Vector2 max)
            {
                Min = min;
                Max = max;
            }

            public Vector2 Min { get; }

            public Vector2 Max { get; }
        }

        private ID2D1Factory1? _factory;
        private ID2D1SolidColorBrush? _strokeBrush;
        private ID2D1StrokeStyle? _strokeStyle;
        private ID2D1InkStyle? _inkStyle;
        private readonly Dictionary<Stroke, StrokeInkCacheEntry> _inkCache = new();

        public void Draw(ID2D1DeviceContext ctx, BoardDocument document, Stroke? activeStroke, BoardViewport viewport)
        {
            EnsureStrokeBrush(ctx);

            WithOptionalDeviceContext2(ctx, ctx2 =>
            {
                PruneInkCache(document, activeStroke);

                WithWorldTransform(ctx, viewport, () =>
                {
                    viewport.GetVisibleWorldBounds(out Vector2 visibleMinWorld, out Vector2 visibleMaxWorld);
                    DrawSceneInWorldBounds(ctx, ctx2, document, activeStroke, new VisibleWorldBounds(visibleMinWorld, visibleMaxWorld));
                });
            });
        }

        public void DrawBackground(ID2D1DeviceContext ctx, BoardDocument document, BoardViewport viewport)
        {
            EnsureStrokeBrush(ctx);

            WithOptionalDeviceContext2(ctx, ctx2 =>
            {
                PruneInkCache(document, null);

                WithWorldTransform(ctx, viewport, () =>
                {
                    viewport.GetVisibleWorldBounds(out Vector2 visibleMinWorld, out Vector2 visibleMaxWorld);
                    DrawSceneInWorldBounds(ctx, ctx2, document, null, new VisibleWorldBounds(visibleMinWorld, visibleMaxWorld));
                });
            });
        }

        public void DrawBackgroundInScreenRect(ID2D1DeviceContext ctx, BoardDocument document, BoardViewport viewport, Rect screenRectDip)
        {
            EnsureStrokeBrush(ctx);

            GetVisibleWorldBoundsFromScreenRect(viewport, screenRectDip, out Vector2 visibleMinWorld, out Vector2 visibleMaxWorld);

            WithOptionalDeviceContext2(ctx, ctx2 =>
            {
                PruneInkCache(document, null);

                WithWorldTransform(ctx, viewport, () =>
                {
                    DrawSceneInWorldBounds(ctx, ctx2, document, null, new VisibleWorldBounds(visibleMinWorld, visibleMaxWorld));
                });
            });
        }

        public void DrawActiveStroke(ID2D1DeviceContext ctx, Stroke activeStroke, BoardViewport viewport)
        {
            EnsureStrokeBrush(ctx);

            WithOptionalDeviceContext2(ctx, ctx2 =>
            {
                WithWorldTransform(ctx, viewport, () =>
                {
                    viewport.GetVisibleWorldBounds(out Vector2 visibleMinWorld, out Vector2 visibleMaxWorld);
                    DrawStrokeIfVisible(ctx, ctx2, activeStroke, visibleMinWorld, visibleMaxWorld);
                });
            });
        }

        private void EnsureStrokeBrush(ID2D1DeviceContext ctx)
        {
            _strokeBrush ??= ctx.CreateSolidColorBrush(new Color4(0, 0, 0, 1));
        }

        private void WithOptionalDeviceContext2(ID2D1DeviceContext ctx, Action<ID2D1DeviceContext2?> action)
        {
            // 某些系统/驱动环境可能不支持 DeviceContext2，这里做一次安全降级。
            using ID2D1DeviceContext2? ctx2 = TryGetDeviceContext2(ctx);
            EnsureInkStyle(ctx2);
            action(ctx2);
        }

        private static void WithWorldTransform(ID2D1DeviceContext ctx, BoardViewport viewport, Action action)
        {
            Matrix3x2 oldTransform = ctx.Transform;
            ctx.Transform = viewport.GetWorldToScreenTransform();
            try
            {
                action();
            }
            finally
            {
                ctx.Transform = oldTransform;
            }
        }

        private static void GetVisibleWorldBoundsFromScreenRect(BoardViewport viewport, Rect screenRectDip, out Vector2 visibleMinWorld, out Vector2 visibleMaxWorld)
        {
            // 局部重绘：把屏幕矩形转换为世界坐标 AABB，用于裁剪可见笔迹的计算。
            Vector2 worldTopLeft = viewport.ScreenToWorld(new Vector2(screenRectDip.Left, screenRectDip.Top));
            Vector2 worldBottomRight = viewport.ScreenToWorld(new Vector2(screenRectDip.Right, screenRectDip.Bottom));

            visibleMinWorld = new Vector2(
                Math.Min(worldTopLeft.X, worldBottomRight.X),
                Math.Min(worldTopLeft.Y, worldBottomRight.Y));

            visibleMaxWorld = new Vector2(
                Math.Max(worldTopLeft.X, worldBottomRight.X),
                Math.Max(worldTopLeft.Y, worldBottomRight.Y));
        }

        private void DrawSceneInWorldBounds(
            ID2D1DeviceContext ctx,
            ID2D1DeviceContext2? ctx2,
            BoardDocument document,
            Stroke? activeStroke,
            VisibleWorldBounds visibleWorldBounds)
        {
            // 绘制顺序：文档笔迹 → 活动笔迹（可选）。

            foreach (var stroke in document.Strokes)
            {
                if (!BoardSceneMath.IsStrokeVisible(stroke, visibleWorldBounds.Min, visibleWorldBounds.Max))
                {
                    continue;
                }

                DrawStroke(ctx, ctx2, stroke);
            }

            if (activeStroke is null)
            {
                return;
            }

            DrawStrokeIfVisible(ctx, ctx2, activeStroke, visibleWorldBounds.Min, visibleWorldBounds.Max);
        }

        private void DrawStrokeIfVisible(ID2D1DeviceContext ctx, ID2D1DeviceContext2? ctx2, Stroke stroke, Vector2 visibleMinWorld, Vector2 visibleMaxWorld)
        {
            if (!BoardSceneMath.IsStrokeVisible(stroke, visibleMinWorld, visibleMaxWorld))
            {
                return;
            }

            DrawStroke(ctx, ctx2, stroke);
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
                float radius = Math.Max(0.5f, stroke.BaseSize * BoardSceneMath.GetStrokeWidthFactor(stroke.Points[0].Pressure) / 2.0f);
                ctx.FillEllipse(new Ellipse(stroke.Points[0].Position, radius, radius), _strokeBrush);
                return;
            }

            if (ctx2 is not null && TryDrawInkStroke(ctx2, stroke))
            {
                StrokeInkCacheEntry entry = _inkCache[stroke];

                // Ink 缓存只会在“点数变化”时做增量追加；对于选择工具造成的“点坐标整体变化”，
                // 通过对 DeviceContext 临时叠加平移变换，避免每一帧都重建 Ink，提升拖动流畅度。
                Vector2 offsetWorld = entry.DrawOffsetWorld;
                if (offsetWorld.LengthSquared() <= 0.0000001f)
                {
                    ctx2.DrawInk(entry.Ink, _strokeBrush, _inkStyle);
                    return;
                }

                Matrix3x2 oldTransform = ctx2.Transform;
                ctx2.Transform = Matrix3x2.CreateTranslation(offsetWorld) * oldTransform;
                try
                {
                    ctx2.DrawInk(entry.Ink, _strokeBrush, _inkStyle);
                }
                finally
                {
                    ctx2.Transform = oldTransform;
                }
                return;
            }

            EnsureStrokeStyle(ctx);

            for (int i = 1; i < stroke.Points.Count; i++)
            {
                StrokePoint p0 = stroke.Points[i - 1];
                StrokePoint p1 = stroke.Points[i];

                float widthFactor = stroke.EnablePressure
                    ? BoardSceneMath.GetStrokeWidthFactor((p0.Pressure + p1.Pressure) / 2.0f)
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

        private static bool NearlyEqual(Vector2 a, Vector2 b, float eps = 0.0001f)
        {
            // 这里的比较用于判断“是否为纯平移”：只需要一个很小的阈值即可。
            // eps 取值对应世界坐标下的绝对误差（默认 1e-4），对拖动/旋转/缩放的判别足够稳定。
            return Vector2.DistanceSquared(a, b) <= eps * eps;
        }

        private bool TryDrawInkStroke(ID2D1DeviceContext2 ctx2, Stroke stroke)
        {
            if (stroke.Points.Count < 2)
            {
                return false;
            }

            int pointCount = stroke.Points.Count;
            Vector2 firstPos = stroke.Points[0].Position;
            Vector2 lastPos = stroke.Points[^1].Position;

            if (!_inkCache.TryGetValue(stroke, out StrokeInkCacheEntry? entry))
            {
                _inkCache[stroke] = StrokeInkCacheEntry.Create(ctx2, stroke);
            }
            else if (pointCount == entry.PointCount)
            {
                // 点数没变但坐标变了（例如选择工具拖动/缩放/旋转）时，Ink 缓存会过期，导致笔迹渲染停在旧位置，
                // 从而出现“笔迹与选择框错位”。这里做一次轻量更新：
                // - 若首尾点位移一致：认为是纯平移，复用 Ink，并记录 DrawOffsetWorld
                // - 否则：重建 Ink（旋转/缩放会改变相对位置，无法用平移修正）
                if (!TryUpdateInkDrawOffset(entry, firstPos, lastPos))
                {
                    RebuildInkCacheEntry(ctx2, stroke, entry);
                }
            }
            else if (pointCount < entry.PointCount)
            {
                // 点数减少：通常来自撤销/重做或替换点集，直接重建更安全。
                RebuildInkCacheEntry(ctx2, stroke, entry);
            }
            else if (entry.DrawOffsetWorld.LengthSquared() > 0.0000001f)
            {
                // 如果缓存处于“偏移绘制”状态，为避免增量追加导致坐标空间混乱，直接重建。
                RebuildInkCacheEntry(ctx2, stroke, entry);
            }
            else
            {
                // 点数增加且没有偏移：走增量追加，提高实时书写性能。
                entry.AppendSegments(stroke, entry.PointCount);
                entry.PointCount = pointCount;
                entry.LastPosition = lastPos;
            }

            return true;
        }

        private static bool TryUpdateInkDrawOffset(StrokeInkCacheEntry entry, Vector2 firstPos, Vector2 lastPos)
        {
            Vector2 expectedFirst = entry.FirstPosition + entry.DrawOffsetWorld;
            Vector2 expectedLast = entry.LastPosition + entry.DrawOffsetWorld;

            if (NearlyEqual(firstPos, expectedFirst) && NearlyEqual(lastPos, expectedLast))
            {
                return true;
            }

            Vector2 deltaFirst = firstPos - entry.FirstPosition;
            Vector2 deltaLast = lastPos - entry.LastPosition;
            if (NearlyEqual(deltaFirst, deltaLast))
            {
                entry.DrawOffsetWorld = deltaFirst;
                return true;
            }

            return false;
        }

        private void RebuildInkCacheEntry(ID2D1DeviceContext2 ctx2, Stroke stroke, StrokeInkCacheEntry entry)
        {
            // Ink 资源与对应的缓存条目需要成对更新，避免内存泄漏或引用旧对象导致的错位。
            entry.Dispose();
            _inkCache.Remove(stroke);
            _inkCache[stroke] = StrokeInkCacheEntry.Create(ctx2, stroke);
        }

        private sealed class StrokeInkCacheEntry : IDisposable
        {
            public StrokeInkCacheEntry(ID2D1Ink ink, int pointCount, Vector2 firstPosition, Vector2 lastPosition)
            {
                Ink = ink;
                PointCount = pointCount;
                FirstPosition = firstPosition;
                LastPosition = lastPosition;
            }

            public ID2D1Ink Ink { get; }

            public int PointCount { get; set; }

            public Vector2 FirstPosition { get; }

            public Vector2 LastPosition { get; set; }

            /// <summary>
            /// 用于“纯平移”场景的绘制偏移（世界坐标）。
            /// </summary>
            public Vector2 DrawOffsetWorld { get; set; }

            public static StrokeInkCacheEntry Create(ID2D1DeviceContext2 ctx2, Stroke stroke)
            {
                InkPoint start = CreateInkPoint(stroke, stroke.Points[0]);
                ID2D1Ink ink = ctx2.CreateInk(start);

                Vector2 firstPos = stroke.Points[0].Position;
                Vector2 lastPos = stroke.Points[^1].Position;
                var entry = new StrokeInkCacheEntry(ink, stroke.Points.Count, firstPos, lastPos);

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
            float widthFactor = stroke.EnablePressure ? BoardSceneMath.GetStrokeWidthFactor(point.Pressure) : 1.0f;
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
        }
    }
}
