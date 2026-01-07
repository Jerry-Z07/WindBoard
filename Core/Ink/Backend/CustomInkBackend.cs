using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using WindBoard.Controls;
using WindBoard.Models.Ink;

namespace WindBoard.Core.Ink.Backend
{
    internal sealed class CustomInkBackend : IInkBackend
    {
        private const double TileSizeDip = 512.0;
        private const float DefaultPressure = 0.5f;
        private const double MinRemnantLengthDip = 96.0 / 25.4 * 2.0; // 2mm
        private const int MinRemnantPoints = 3;
        private const int MaxEraseFragmentsPerStroke = 24;
        private static readonly InkStrokeModel[] EmptyStrokeArray = Array.Empty<InkStrokeModel>();

        private readonly record struct SegmentEntry(Guid StrokeId, int SegmentStartIndex, Rect Bounds);

        private sealed class Session
        {
            public Session(InkStrokeStyle style, double zoomAtStart)
            {
                Style = style;
                ZoomAtStart = zoomAtStart;
            }

            public InkStrokeStyle Style { get; set; }
            public double ZoomAtStart { get; }
            public InkStrokeModel? CurrentStroke { get; set; }
            public List<InkStrokeModel> Segments { get; } = new List<InkStrokeModel>(4);
        }

        private sealed class Tile
        {
            public Tile(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X { get; }
            public int Y { get; }
            public bool Dirty { get; set; }
            public bool OrderDirty { get; set; }
            public List<InkStrokeModel> Strokes { get; } = new List<InkStrokeModel>(64);
        }

        private readonly InkSurface _surface;
        private readonly Dictionary<int, Session> _sessions = new Dictionary<int, Session>();
        private readonly Dictionary<long, Tile> _tiles = new Dictionary<long, Tile>();
        private readonly Dictionary<Guid, InkStrokeModel> _strokeById = new Dictionary<Guid, InkStrokeModel>();
        private readonly Dictionary<Guid, Geometry> _strokeGeometry = new Dictionary<Guid, Geometry>();
        private readonly Dictionary<Guid, Rect> _strokeBounds = new Dictionary<Guid, Rect>();
        private readonly Dictionary<Guid, List<long>> _strokeTiles = new Dictionary<Guid, List<long>>();
        private readonly Dictionary<long, List<SegmentEntry>> _segmentBuckets = new Dictionary<long, List<SegmentEntry>>();
        private readonly Dictionary<Guid, List<long>> _strokeSegmentBuckets = new Dictionary<Guid, List<long>>();
        private readonly HashSet<Guid> _activeStrokeIds = new HashSet<Guid>();
        private readonly HashSet<Guid> _selectedStrokeIds = new HashSet<Guid>();
        private readonly Dictionary<Guid, long> _strokeZOrder = new Dictionary<Guid, long>();
        private long _nextZOrder;
        private readonly Dictionary<uint, SolidColorBrush> _brushCache = new Dictionary<uint, SolidColorBrush>();
        private bool _disposed;

        private List<InkStrokeModel>? _documentStrokes;

        public CustomInkBackend(InkSurface surface)
        {
            _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        }

        public event EventHandler<InkStrokeCollectionChangedEventArgs>? StrokesChanged;

        public bool HasSelection => _selectedStrokeIds.Count > 0;

        public int SelectedStrokeCount => _selectedStrokeIds.Count;

        public Rect GetSelectionBounds()
        {
            if (_selectedStrokeIds.Count == 0) return Rect.Empty;
            if (_documentStrokes == null) return Rect.Empty;

            bool hasAny = false;
            var bounds = Rect.Empty;

            foreach (var id in _selectedStrokeIds.ToArray())
            {
                if (!_strokeById.TryGetValue(id, out var stroke) || stroke == null)
                {
                    _selectedStrokeIds.Remove(id);
                    continue;
                }

                Rect b = GetOrComputeBounds(stroke);
                if (b.IsEmpty) continue;

                if (!hasAny)
                {
                    bounds = b;
                    hasAny = true;
                }
                else
                {
                    bounds.Union(b);
                }
            }

            return hasAny ? bounds : Rect.Empty;
        }

        public void ClearSelection()
        {
            _selectedStrokeIds.Clear();
        }

        public bool SelectAtPoint(Point canvasPoint, bool toggle)
        {
            if (_disposed) return false;
            EnsureDocumentBound();

            if (!TryHitTestStroke(canvasPoint, out var hit))
            {
                if (_selectedStrokeIds.Count == 0) return false;
                _selectedStrokeIds.Clear();
                return true;
            }

            if (!toggle)
            {
                bool changed = _selectedStrokeIds.Count != 1 || !_selectedStrokeIds.Contains(hit.Id);
                _selectedStrokeIds.Clear();
                _selectedStrokeIds.Add(hit.Id);
                return changed;
            }

            if (_selectedStrokeIds.Contains(hit.Id))
            {
                _selectedStrokeIds.Remove(hit.Id);
                return true;
            }

            _selectedStrokeIds.Add(hit.Id);
            return true;
        }

        public int SelectInRect(Rect selectionRect, bool additive)
        {
            if (_disposed) return 0;
            EnsureDocumentBound();
            if (selectionRect.IsEmpty) return 0;

            var candidates = GetStrokesIntersectingRect(selectionRect);
            if (!additive)
            {
                _selectedStrokeIds.Clear();
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                var s = candidates[i];
                if (s == null) continue;
                _selectedStrokeIds.Add(s.Id);
            }

            return _selectedStrokeIds.Count;
        }

        public bool DeleteSelection()
        {
            if (_disposed) return false;
            EnsureDocumentBound();
            if (_selectedStrokeIds.Count == 0) return false;

            var removed = new List<InkStrokeModel>(_selectedStrokeIds.Count);
            foreach (var id in _selectedStrokeIds)
            {
                if (_strokeById.TryGetValue(id, out var stroke) && stroke != null)
                {
                    removed.Add(stroke);
                }
            }

            if (removed.Count == 0)
            {
                _selectedStrokeIds.Clear();
                return false;
            }

            for (int i = 0; i < removed.Count; i++)
            {
                RemoveStrokeInternal(removed[i], removeFromDocument: true, removeZOrder: true, raiseEvent: false);
            }

            _selectedStrokeIds.Clear();
            RaiseStrokesChanged(added: null, removed: removed);
            UpdateDirtyTiles();
            return true;
        }

        public bool CopySelection(double dx, double dy, bool replaceSelection)
        {
            if (_disposed) return false;
            EnsureDocumentBound();
            if (_selectedStrokeIds.Count == 0) return false;

            var selected = GetSelectedStrokesInZOrder();
            if (selected.Count == 0) return false;

            var added = new List<InkStrokeModel>(selected.Count);
            for (int i = 0; i < selected.Count; i++)
            {
                var src = selected[i];
                if (src == null) continue;
                var clone = CloneStrokeWithTransform(src, id: Guid.NewGuid(), dx, dy, scaleFrom: null, scaleTo: null);
                added.Add(clone);
            }

            if (added.Count == 0) return false;

            for (int i = 0; i < added.Count; i++)
            {
                AddFinalizedStroke(added[i], raiseEvent: false);
            }

            RaiseStrokesChanged(added, removed: null);

            if (replaceSelection)
            {
                _selectedStrokeIds.Clear();
                for (int i = 0; i < added.Count; i++)
                {
                    _selectedStrokeIds.Add(added[i].Id);
                }
            }

            UpdateDirtyTiles();
            return true;
        }

        public bool BringSelectionToFront()
        {
            if (_disposed) return false;
            EnsureDocumentBound();
            if (_selectedStrokeIds.Count == 0) return false;

            var selected = GetSelectedStrokesInZOrder();
            if (selected.Count == 0) return false;

            for (int i = 0; i < selected.Count; i++)
            {
                var s = selected[i];
                if (s == null) continue;
                _strokeZOrder[s.Id] = ++_nextZOrder;
            }

            SortAllTilesByZOrder();
            MarkAllTilesDirty();
            UpdateDirtyTiles();
            RaiseStrokesChanged(added: null, removed: null);
            return true;
        }

        public bool MoveSelection(double dx, double dy)
        {
            if (_disposed) return false;
            EnsureDocumentBound();
            if (_selectedStrokeIds.Count == 0) return false;
            if (dx == 0 && dy == 0) return false;

            var selected = GetSelectedStrokesInZOrder();
            if (selected.Count == 0) return false;

            var removed = new List<InkStrokeModel>(selected.Count);
            var added = new List<InkStrokeModel>(selected.Count);

            for (int i = 0; i < selected.Count; i++)
            {
                var src = selected[i];
                if (src == null) continue;
                var moved = CloneStrokeWithTransform(src, id: src.Id, dx, dy, scaleFrom: null, scaleTo: null);
                ReplaceStroke(src, moved, raiseEvent: false);
                removed.Add(src);
                added.Add(moved);
            }

            if (added.Count == 0) return false;

            RaiseStrokesChanged(added, removed);
            UpdateDirtyTiles();
            return true;
        }

        public bool ScaleSelection(Rect fromBounds, Rect toBounds)
        {
            if (_disposed) return false;
            EnsureDocumentBound();
            if (_selectedStrokeIds.Count == 0) return false;
            if (fromBounds.IsEmpty || toBounds.IsEmpty) return false;

            var selected = GetSelectedStrokesInZOrder();
            if (selected.Count == 0) return false;

            double fromW = fromBounds.Width;
            double fromH = fromBounds.Height;
            if (fromW <= 0) fromW = 1;
            if (fromH <= 0) fromH = 1;

            double sx = toBounds.Width / fromW;
            double sy = toBounds.Height / fromH;
            if (double.IsNaN(sx) || double.IsInfinity(sx) || sx <= 0) return false;
            if (double.IsNaN(sy) || double.IsInfinity(sy) || sy <= 0) return false;

            var removed = new List<InkStrokeModel>(selected.Count);
            var added = new List<InkStrokeModel>(selected.Count);

            for (int i = 0; i < selected.Count; i++)
            {
                var src = selected[i];
                if (src == null) continue;
                var scaled = CloneStrokeWithTransform(src, id: src.Id, dx: 0, dy: 0, scaleFrom: fromBounds, scaleTo: toBounds);
                ReplaceStroke(src, scaled, raiseEvent: false);
                removed.Add(src);
                added.Add(scaled);
            }

            if (added.Count == 0) return false;

            RaiseStrokesChanged(added, removed);
            UpdateDirtyTiles();
            return true;
        }

        public bool Erase(Rect eraserRect)
        {
            if (_disposed) return false;
            EnsureDocumentBound();
            if (eraserRect.IsEmpty) return false;

            if (_selectedStrokeIds.Count > 0)
            {
                _selectedStrokeIds.Clear();
            }

            var candidates = GetStrokesIntersectingRect(eraserRect);
            if (candidates.Count == 0) return false;

            var removed = new List<InkStrokeModel>();
            var added = new List<InkStrokeModel>();

            for (int i = 0; i < candidates.Count; i++)
            {
                var stroke = candidates[i];
                if (stroke == null) continue;
                _ = EraseStrokeByRect(stroke, eraserRect, removed, added);
            }

            if (removed.Count == 0 && added.Count == 0) return false;

            RaiseStrokesChanged(added.Count == 0 ? null : added, removed.Count == 0 ? null : removed);
            UpdateDirtyTiles();
            return true;
        }

        public void BindDocument(List<InkStrokeModel> strokes)
        {
            if (_disposed) return;

            CancelAllStrokes();
            _documentStrokes = strokes ?? throw new ArgumentNullException(nameof(strokes));

            RebuildAllCaches();
        }

        public void UnbindDocument()
        {
            if (_disposed) return;
            CancelAllStrokes();
            _documentStrokes = null;
            ClearAllCachesAndVisuals();
        }

        public void BeginStroke(int pointerId, InkStrokeStyle style, InkPoint startPoint, double zoomAtStart)
        {
            if (_disposed) return;
            EnsureDocumentBound();

            if (_sessions.ContainsKey(pointerId))
            {
                CancelStroke(pointerId);
            }

            var session = new Session(style, zoomAtStart);
            _sessions[pointerId] = session;

            CreateNewSegment(session, startPoint);
            UpdateDynamicVisual();
        }

        public void AppendPoints(int pointerId, ReadOnlySpan<InkPoint> points)
        {
            if (_disposed) return;
            if (points.Length == 0) return;

            EnsureDocumentBound();

            if (!_sessions.TryGetValue(pointerId, out var session)) return;
            var stroke = session.CurrentStroke;
            if (stroke == null) return;

            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                stroke.Points.Add(SanitizePoint(p));
            }

            UpdateDynamicVisual();
        }

        public void StartNewSegment(int pointerId, InkPoint startPoint)
        {
            if (_disposed) return;
            EnsureDocumentBound();

            if (!_sessions.TryGetValue(pointerId, out var session)) return;
            FinalizeCurrentStroke(session);
            CreateNewSegment(session, startPoint);
            UpdateDynamicVisual();
            UpdateDirtyTiles();
        }

        public void UpdateStrokeStyle(int pointerId, InkStrokeStyle style, double zoomAtStart)
        {
            if (_disposed) return;
            EnsureDocumentBound();

            if (!_sessions.TryGetValue(pointerId, out var session)) return;

            session.Style = style;

            for (int i = 0; i < session.Segments.Count; i++)
            {
                var s = session.Segments[i];
                s.Style = style;
            }

            InvalidateStrokes(session.Segments);
            UpdateDynamicVisual();
            UpdateDirtyTiles();
        }

        public void EndStroke(int pointerId)
        {
            if (_disposed) return;
            if (!_sessions.TryGetValue(pointerId, out var session)) return;

            FinalizeCurrentStroke(session);
            _sessions.Remove(pointerId);
            UpdateDynamicVisual();
            UpdateDirtyTiles();
        }

        public void CancelStroke(int pointerId)
        {
            if (_disposed) return;
            if (!_sessions.TryGetValue(pointerId, out var session)) return;

            for (int i = 0; i < session.Segments.Count; i++)
            {
                RemoveStroke(session.Segments[i]);
            }

            _sessions.Remove(pointerId);
            UpdateDynamicVisual();
            UpdateDirtyTiles();
        }

        public void CancelAllStrokes()
        {
            if (_disposed) return;
            if (_sessions.Count == 0) return;

            foreach (var id in _sessions.Keys.ToList())
            {
                CancelStroke(id);
            }
        }

        private void EnsureDocumentBound()
        {
            if (_documentStrokes == null)
            {
                throw new InvalidOperationException("CustomInkBackend requires a bound document.");
            }
        }

        private void CreateNewSegment(Session session, InkPoint startPoint)
        {
            var stroke = new InkStrokeModel
            {
                Id = Guid.NewGuid(),
                ZoomAtCreation = session.ZoomAtStart,
                Style = session.Style
            };
            stroke.Points.Add(SanitizePoint(startPoint));

            _documentStrokes!.Add(stroke);
            _strokeById[stroke.Id] = stroke;
            _strokeZOrder[stroke.Id] = ++_nextZOrder;
            RaiseStrokesChanged(added: new[] { stroke }, removed: null);

            session.CurrentStroke = stroke;
            session.Segments.Add(stroke);
            _activeStrokeIds.Add(stroke.Id);
        }

        private void FinalizeCurrentStroke(Session session)
        {
            var stroke = session.CurrentStroke;
            if (stroke == null) return;

            session.CurrentStroke = null;
            _activeStrokeIds.Remove(stroke.Id);

            BakeStrokeIntoTiles(stroke);
        }

        private void BakeStrokeIntoTiles(InkStrokeModel stroke)
        {
            InvalidateStroke(stroke);

            Rect bounds = GetOrComputeBounds(stroke);
            if (bounds.IsEmpty)
            {
                return;
            }

            var tileKeys = GetTilesForBounds(bounds);
            _strokeTiles[stroke.Id] = tileKeys;

            for (int i = 0; i < tileKeys.Count; i++)
            {
                long key = tileKeys[i];
                var tile = GetOrCreateTile(key);
                if (tile.Strokes.Count > 0)
                {
                    var last = tile.Strokes[tile.Strokes.Count - 1];
                    long lastZ = last == null ? 0 : GetZOrderOrDefault(last.Id);
                    long newZ = GetZOrderOrDefault(stroke.Id);
                    if (newZ < lastZ)
                    {
                        tile.OrderDirty = true;
                    }
                }
                tile.Strokes.Add(stroke);
                tile.Dirty = true;
            }

            IndexStrokeSegments(stroke);
        }

        private void RemoveStroke(InkStrokeModel stroke)
        {
            RemoveStrokeInternal(stroke, removeFromDocument: true, removeZOrder: true, raiseEvent: true);
        }

        private void RaiseStrokesChanged(IReadOnlyList<InkStrokeModel>? added, IReadOnlyList<InkStrokeModel>? removed)
        {
            var handler = StrokesChanged;
            if (handler == null) return;

            handler(this, new InkStrokeCollectionChangedEventArgs(
                added ?? EmptyStrokeArray,
                removed ?? EmptyStrokeArray));
        }

        private void RebuildAllCaches()
        {
            ClearAllCachesAndVisuals();

            if (_documentStrokes == null) return;

            _nextZOrder = 0;
            for (int i = 0; i < _documentStrokes.Count; i++)
            {
                var s = _documentStrokes[i];
                if (s == null) continue;
                _strokeById[s.Id] = s;
                _strokeZOrder[s.Id] = ++_nextZOrder;
                BakeStrokeIntoTiles(s);
            }

            UpdateDynamicVisual();
            UpdateDirtyTiles();
        }

        private void ClearAllCachesAndVisuals()
        {
            _sessions.Clear();
            _tiles.Clear();
            _strokeById.Clear();
            _strokeGeometry.Clear();
            _strokeBounds.Clear();
            _strokeTiles.Clear();
            _segmentBuckets.Clear();
            _strokeSegmentBuckets.Clear();
            _activeStrokeIds.Clear();
            _selectedStrokeIds.Clear();
            _strokeZOrder.Clear();
            _nextZOrder = 0;

            _surface.ClearTiles();
            using (_surface.GetDynamicVisual().RenderOpen())
            {
            }
        }

        private void UpdateDirtyTiles()
        {
            if (_tiles.Count == 0) return;

            foreach (var tile in _tiles.Values)
            {
                if (!tile.Dirty) continue;
                RenderTile(tile);
                tile.Dirty = false;
            }
        }

        private void RenderTile(Tile tile)
        {
            var dv = _surface.GetOrCreateTileVisual(tile.X, tile.Y);
            using var dc = dv.RenderOpen();

            var tileRect = new Rect(tile.X * TileSizeDip, tile.Y * TileSizeDip, TileSizeDip, TileSizeDip);
            dc.PushClip(new RectangleGeometry(tileRect));

            if (tile.OrderDirty)
            {
                tile.OrderDirty = false;
                if (tile.Strokes.Count > 1)
                {
                    tile.Strokes.Sort((a, b) =>
                    {
                        if (ReferenceEquals(a, b)) return 0;
                        if (a == null) return -1;
                        if (b == null) return 1;
                        return GetZOrderOrDefault(a.Id).CompareTo(GetZOrderOrDefault(b.Id));
                    });
                }
            }

            for (int i = 0; i < tile.Strokes.Count; i++)
            {
                var s = tile.Strokes[i];
                if (s == null) continue;
                if (_activeStrokeIds.Contains(s.Id)) continue;

                Geometry? geom = GetOrComputeGeometry(s);
                if (geom == null) continue;

                dc.DrawGeometry(GetBrush(s.Style.Color), null, geom);
            }

            dc.Pop();
        }

        private void UpdateDynamicVisual()
        {
            var dv = _surface.GetDynamicVisual();
            using var dc = dv.RenderOpen();

            if (_activeStrokeIds.Count == 0) return;
            if (_documentStrokes == null) return;

            for (int i = 0; i < _documentStrokes.Count; i++)
            {
                var s = _documentStrokes[i];
                if (s == null) continue;
                if (!_activeStrokeIds.Contains(s.Id)) continue;

                Geometry geom = BuildStrokeGeometry(s);
                if (geom == null) continue;
                dc.DrawGeometry(GetBrush(s.Style.Color), null, geom);
            }
        }

        private Tile GetOrCreateTile(long key)
        {
            if (_tiles.TryGetValue(key, out var existing))
            {
                return existing;
            }

            UnpackTile(key, out int x, out int y);
            var tile = new Tile(x, y) { Dirty = true };
            _tiles[key] = tile;
            return tile;
        }

        private static List<long> GetTilesForBounds(Rect bounds)
        {
            int minX = (int)Math.Floor(bounds.X / TileSizeDip);
            int minY = (int)Math.Floor(bounds.Y / TileSizeDip);
            int maxX = (int)Math.Floor(bounds.Right / TileSizeDip);
            int maxY = (int)Math.Floor(bounds.Bottom / TileSizeDip);

            if (maxX < minX) maxX = minX;
            if (maxY < minY) maxY = minY;

            int count = (maxX - minX + 1) * (maxY - minY + 1);
            var list = new List<long>(Math.Max(4, count));

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    list.Add(PackTile(x, y));
                }
            }

            return list;
        }

        private Rect GetOrComputeBounds(InkStrokeModel stroke)
        {
            if (_strokeBounds.TryGetValue(stroke.Id, out var b))
            {
                return b;
            }

            Rect bounds = ComputeStrokeBounds(stroke);
            _strokeBounds[stroke.Id] = bounds;
            return bounds;
        }

        private Geometry? GetOrComputeGeometry(InkStrokeModel stroke)
        {
            if (_strokeGeometry.TryGetValue(stroke.Id, out var g))
            {
                return g;
            }

            Geometry? geom = BuildStrokeGeometry(stroke);
            if (geom != null)
            {
                geom.Freeze();
                _strokeGeometry[stroke.Id] = geom;
            }

            return geom;
        }

        private void InvalidateStrokes(List<InkStrokeModel> strokes)
        {
            for (int i = 0; i < strokes.Count; i++)
            {
                InvalidateStroke(strokes[i]);
            }

            for (int i = 0; i < strokes.Count; i++)
            {
                MarkStrokeTilesDirty(strokes[i]);
            }
        }

        private void InvalidateStroke(InkStrokeModel stroke)
        {
            _strokeGeometry.Remove(stroke.Id);
            _strokeBounds.Remove(stroke.Id);
        }

        private void MarkStrokeTilesDirty(InkStrokeModel stroke)
        {
            if (!_strokeTiles.TryGetValue(stroke.Id, out var keys)) return;
            for (int i = 0; i < keys.Count; i++)
            {
                if (_tiles.TryGetValue(keys[i], out var tile))
                {
                    tile.Dirty = true;
                }
            }
        }

        private void MarkAllTilesDirty()
        {
            foreach (var tile in _tiles.Values)
            {
                tile.Dirty = true;
            }
        }

        private void SortAllTilesByZOrder()
        {
            if (_tiles.Count == 0) return;

            foreach (var tile in _tiles.Values)
            {
                if (tile.Strokes.Count <= 1) continue;
                tile.OrderDirty = false;
                tile.Strokes.Sort((a, b) =>
                {
                    if (ReferenceEquals(a, b)) return 0;
                    if (a == null) return -1;
                    if (b == null) return 1;
                    long za = GetZOrderOrDefault(a.Id);
                    long zb = GetZOrderOrDefault(b.Id);
                    return za.CompareTo(zb);
                });
            }
        }

        private long GetZOrderOrDefault(Guid id)
        {
            return _strokeZOrder.TryGetValue(id, out long z) ? z : 0;
        }

        private void AddFinalizedStroke(InkStrokeModel stroke, bool raiseEvent)
        {
            _documentStrokes!.Add(stroke);
            _strokeById[stroke.Id] = stroke;
            _strokeZOrder[stroke.Id] = ++_nextZOrder;

            if (raiseEvent)
            {
                RaiseStrokesChanged(added: new[] { stroke }, removed: null);
            }

            BakeStrokeIntoTiles(stroke);
        }

        private void ReplaceStroke(InkStrokeModel oldStroke, InkStrokeModel newStroke, bool raiseEvent)
        {
            if (_documentStrokes == null) return;
            if (oldStroke == null || newStroke == null) return;
            if (oldStroke.Id != newStroke.Id)
            {
                throw new InvalidOperationException("ReplaceStroke requires the same stroke Id.");
            }

            RemoveStrokeFromTiles(oldStroke);
            RemoveStrokeFromSegmentIndex(oldStroke.Id);
            RemoveStrokeVisualCaches(oldStroke);

            int index = _documentStrokes.IndexOf(oldStroke);
            if (index >= 0)
            {
                _documentStrokes[index] = newStroke;
            }
            else
            {
                _documentStrokes.Add(newStroke);
            }

            _strokeById[newStroke.Id] = newStroke;

            InvalidateStroke(newStroke);
            BakeStrokeIntoTiles(newStroke);

            if (raiseEvent)
            {
                RaiseStrokesChanged(added: new[] { newStroke }, removed: new[] { oldStroke });
            }
        }

        private void RemoveStrokeInternal(InkStrokeModel stroke, bool removeFromDocument, bool removeZOrder, bool raiseEvent)
        {
            if (removeFromDocument && _documentStrokes != null)
            {
                _documentStrokes.Remove(stroke);
            }

            _activeStrokeIds.Remove(stroke.Id);
            _strokeById.Remove(stroke.Id);
            RemoveStrokeVisualCaches(stroke);
            RemoveStrokeFromTiles(stroke);
            RemoveStrokeFromSegmentIndex(stroke.Id);

            _selectedStrokeIds.Remove(stroke.Id);
            if (removeZOrder)
            {
                _strokeZOrder.Remove(stroke.Id);
            }

            if (raiseEvent)
            {
                RaiseStrokesChanged(added: null, removed: new[] { stroke });
            }
        }

        private void RemoveStrokeVisualCaches(InkStrokeModel stroke)
        {
            _strokeGeometry.Remove(stroke.Id);
            _strokeBounds.Remove(stroke.Id);
        }

        private void RemoveStrokeFromTiles(InkStrokeModel stroke)
        {
            if (!_strokeTiles.TryGetValue(stroke.Id, out var keys))
            {
                return;
            }

            _strokeTiles.Remove(stroke.Id);
            for (int i = 0; i < keys.Count; i++)
            {
                if (!_tiles.TryGetValue(keys[i], out var tile)) continue;
                tile.Strokes.Remove(stroke);
                tile.Dirty = true;
            }
        }

        private void IndexStrokeSegments(InkStrokeModel stroke)
        {
            if (stroke == null) return;
            RemoveStrokeFromSegmentIndex(stroke.Id);

            var pts = stroke.Points;
            if (pts.Count == 0) return;

            double maxRadius = ComputeMaxStrokeRadius(stroke);
            var usedKeys = new HashSet<long>();

            if (pts.Count == 1)
            {
                var p = pts[0];
                var bounds = new Rect(p.X - maxRadius, p.Y - maxRadius, maxRadius * 2, maxRadius * 2);
                AddSegmentToBuckets(stroke.Id, segmentStartIndex: 0, bounds, usedKeys);
            }
            else
            {
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var a = pts[i];
                    var b = pts[i + 1];
                    var segBounds = SegmentBounds(a, b, maxRadius);
                    AddSegmentToBuckets(stroke.Id, i, segBounds, usedKeys);
                }
            }

            if (usedKeys.Count > 0)
            {
                _strokeSegmentBuckets[stroke.Id] = usedKeys.ToList();
            }
        }

        private void AddSegmentToBuckets(Guid strokeId, int segmentStartIndex, Rect bounds, HashSet<long> usedKeys)
        {
            var keys = GetTilesForBounds(bounds);
            for (int i = 0; i < keys.Count; i++)
            {
                long key = keys[i];
                if (!_segmentBuckets.TryGetValue(key, out var list))
                {
                    list = new List<SegmentEntry>(64);
                    _segmentBuckets[key] = list;
                }

                list.Add(new SegmentEntry(strokeId, segmentStartIndex, bounds));
                usedKeys.Add(key);
            }
        }

        private void RemoveStrokeFromSegmentIndex(Guid strokeId)
        {
            if (!_strokeSegmentBuckets.TryGetValue(strokeId, out var keys))
            {
                return;
            }

            _strokeSegmentBuckets.Remove(strokeId);

            for (int i = 0; i < keys.Count; i++)
            {
                long key = keys[i];
                if (!_segmentBuckets.TryGetValue(key, out var list)) continue;
                list.RemoveAll(e => e.StrokeId == strokeId);
                if (list.Count == 0)
                {
                    _segmentBuckets.Remove(key);
                }
            }
        }

        private bool TryHitTestStroke(Point canvasPoint, out InkStrokeModel hit)
        {
            hit = null!;
            if (_documentStrokes == null) return false;

            long key = PackTile((int)Math.Floor(canvasPoint.X / TileSizeDip), (int)Math.Floor(canvasPoint.Y / TileSizeDip));
            if (!_segmentBuckets.TryGetValue(key, out var segments) || segments.Count == 0) return false;

            InkStrokeModel? best = null;
            long bestZ = long.MinValue;
            double bestDist2 = double.MaxValue;

            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                if (!seg.Bounds.Contains(canvasPoint)) continue;

                if (!_strokeById.TryGetValue(seg.StrokeId, out var stroke) || stroke == null) continue;

                double radius = ComputeMaxStrokeRadius(stroke);
                double threshold2 = radius * radius;

                double dist2;
                var pts = stroke.Points;
                if (pts.Count == 0) continue;

                if (pts.Count == 1)
                {
                    var p = pts[0];
                    double dx = canvasPoint.X - p.X;
                    double dy = canvasPoint.Y - p.Y;
                    dist2 = dx * dx + dy * dy;
                }
                else
                {
                    int idx = seg.SegmentStartIndex;
                    idx = Math.Clamp(idx, 0, pts.Count - 2);
                    var a = pts[idx];
                    var b = pts[idx + 1];
                    dist2 = DistanceSquaredPointToSegment(canvasPoint.X, canvasPoint.Y, a.X, a.Y, b.X, b.Y);
                }

                if (dist2 > threshold2) continue;

                long z = GetZOrderOrDefault(stroke.Id);
                if (best == null || z > bestZ || (z == bestZ && dist2 < bestDist2))
                {
                    best = stroke;
                    bestZ = z;
                    bestDist2 = dist2;
                }
            }

            if (best == null) return false;
            hit = best;
            return true;
        }

        private List<InkStrokeModel> GetStrokesIntersectingRect(Rect rect)
        {
            var results = new List<InkStrokeModel>();
            if (_documentStrokes == null) return results;

            var candidateIds = new HashSet<Guid>();
            var keys = GetTilesForBounds(rect);
            for (int i = 0; i < keys.Count; i++)
            {
                if (!_segmentBuckets.TryGetValue(keys[i], out var segs)) continue;
                for (int j = 0; j < segs.Count; j++)
                {
                    candidateIds.Add(segs[j].StrokeId);
                }
            }

            foreach (var id in candidateIds)
            {
                if (_activeStrokeIds.Contains(id)) continue;
                if (!_strokeById.TryGetValue(id, out var stroke) || stroke == null) continue;
                var b = GetOrComputeBounds(stroke);
                if (b.IsEmpty) continue;
                if (b.IntersectsWith(rect))
                {
                    results.Add(stroke);
                }
            }

            return results;
        }

        private List<InkStrokeModel> GetSelectedStrokesInZOrder()
        {
            var list = new List<InkStrokeModel>(_selectedStrokeIds.Count);
            foreach (var id in _selectedStrokeIds)
            {
                if (_strokeById.TryGetValue(id, out var s) && s != null)
                {
                    list.Add(s);
                }
            }

            list.Sort((a, b) =>
            {
                if (ReferenceEquals(a, b)) return 0;
                if (a == null) return -1;
                if (b == null) return 1;
                return GetZOrderOrDefault(a.Id).CompareTo(GetZOrderOrDefault(b.Id));
            });

            return list;
        }

        private bool EraseStrokeByRect(InkStrokeModel stroke, Rect eraserRect, List<InkStrokeModel> removed, List<InkStrokeModel> added)
        {
            if (stroke.Points.Count == 0) return false;

            var pts = stroke.Points;
            if (pts.Count == 1)
            {
                var p = pts[0];
                if (!eraserRect.Contains(new Point(p.X, p.Y)))
                {
                    return false;
                }

                removed.Add(stroke);
                RemoveStrokeInternal(stroke, removeFromDocument: true, removeZOrder: true, raiseEvent: false);
                return true;
            }

            double radius = ComputeMaxStrokeRadius(stroke);
            var eraseSeg = new bool[Math.Max(0, pts.Count - 1)];
            bool any = false;

            for (int i = 0; i < eraseSeg.Length; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                var segBounds = SegmentBounds(a, b, radius);
                bool hit = segBounds.IntersectsWith(eraserRect);
                eraseSeg[i] = hit;
                if (hit) any = true;
            }

            if (!any) return false;

            var fragments = SplitStrokeByEraseMask(stroke, eraserRect, eraseSeg);

            removed.Add(stroke);
            RemoveStrokeInternal(stroke, removeFromDocument: true, removeZOrder: true, raiseEvent: false);

            for (int i = 0; i < fragments.Count; i++)
            {
                var frag = fragments[i];
                if (frag == null) continue;
                added.Add(frag);
                AddFinalizedStroke(frag, raiseEvent: false);
            }

            return true;
        }

        private List<InkStrokeModel> SplitStrokeByEraseMask(InkStrokeModel stroke, Rect eraserRect, bool[] eraseSegments)
        {
            var fragments = new List<InkStrokeModel>();
            var pts = stroke.Points;
            if (pts.Count == 0) return fragments;

            var current = new List<InkPoint>(Math.Min(pts.Count, 256));

            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                bool pointErased = eraserRect.Contains(new Point(p.X, p.Y));
                if (pointErased)
                {
                    FlushFragment();
                    continue;
                }

                current.Add(p);

                bool breakAfter = i < eraseSegments.Length && eraseSegments[i];
                if (breakAfter)
                {
                    FlushFragment();
                }
            }

            FlushFragment();

            if (fragments.Count > MaxEraseFragmentsPerStroke)
            {
                fragments.Clear();
            }

            return fragments;

            void FlushFragment()
            {
                if (current.Count == 0) return;

                if (ShouldDropRemnant(current))
                {
                    current.Clear();
                    return;
                }

                var frag = new InkStrokeModel
                {
                    Id = Guid.NewGuid(),
                    ZoomAtCreation = stroke.ZoomAtCreation,
                    Style = stroke.Style
                };
                frag.Points.AddRange(current);
                fragments.Add(frag);
                current.Clear();
            }
        }

        private static bool ShouldDropRemnant(List<InkPoint> points)
        {
            if (points.Count < MinRemnantPoints) return true;

            double len = 0;
            for (int i = 1; i < points.Count; i++)
            {
                double dx = points[i].X - points[i - 1].X;
                double dy = points[i].Y - points[i - 1].Y;
                len += Math.Sqrt(dx * dx + dy * dy);
                if (len >= MinRemnantLengthDip)
                {
                    return false;
                }
            }

            return true;
        }

        private static Rect SegmentBounds(InkPoint a, InkPoint b, double radius)
        {
            double minX = Math.Min(a.X, b.X) - radius;
            double minY = Math.Min(a.Y, b.Y) - radius;
            double maxX = Math.Max(a.X, b.X) + radius;
            double maxY = Math.Max(a.Y, b.Y) + radius;
            return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        }

        private static double ComputeMaxStrokeRadius(InkStrokeModel stroke)
        {
            double zoomAtCreation = stroke.ZoomAtCreation;
            if (double.IsNaN(zoomAtCreation) || double.IsInfinity(zoomAtCreation) || zoomAtCreation <= 0)
            {
                zoomAtCreation = 1.0;
            }

            double logical = stroke.Style.LogicalThicknessDip;
            if (double.IsNaN(logical) || double.IsInfinity(logical) || logical <= 0)
            {
                logical = 1.0;
            }

            double renderThickness = logical / zoomAtCreation;
            if (double.IsNaN(renderThickness) || double.IsInfinity(renderThickness) || renderThickness <= 0)
            {
                renderThickness = 1.0;
            }

            return Math.Max(0.5, renderThickness * 0.5);
        }

        private static double DistanceSquaredPointToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            double abx = bx - ax;
            double aby = by - ay;
            double apx = px - ax;
            double apy = py - ay;

            double denom = abx * abx + aby * aby;
            if (denom <= 1e-12)
            {
                return apx * apx + apy * apy;
            }

            double t = (apx * abx + apy * aby) / denom;
            if (t < 0) t = 0;
            else if (t > 1) t = 1;

            double cx = ax + abx * t;
            double cy = ay + aby * t;
            double dx = px - cx;
            double dy = py - cy;
            return dx * dx + dy * dy;
        }

        private static InkStrokeModel CloneStrokeWithTransform(
            InkStrokeModel src,
            Guid id,
            double dx,
            double dy,
            Rect? scaleFrom,
            Rect? scaleTo)
        {
            var clone = new InkStrokeModel
            {
                Id = id,
                ZoomAtCreation = src.ZoomAtCreation,
                Style = src.Style
            };

            if (scaleFrom == null || scaleTo == null)
            {
                var pts = src.Points;
                for (int i = 0; i < pts.Count; i++)
                {
                    var p = pts[i];
                    clone.Points.Add(p with { X = p.X + dx, Y = p.Y + dy });
                }

                return clone;
            }

            var from = scaleFrom.Value;
            var to = scaleTo.Value;

            double fromW = from.Width;
            double fromH = from.Height;
            if (fromW <= 0) fromW = 1;
            if (fromH <= 0) fromH = 1;

            double sx = to.Width / fromW;
            double sy = to.Height / fromH;

            var srcPts = src.Points;
            for (int i = 0; i < srcPts.Count; i++)
            {
                var p = srcPts[i];
                double x = to.X + (p.X - from.X) * sx + dx;
                double y = to.Y + (p.Y - from.Y) * sy + dy;
                clone.Points.Add(p with { X = x, Y = y });
            }

            return clone;
        }

        private static Rect ComputeStrokeBounds(InkStrokeModel stroke)
        {
            if (stroke.Points.Count == 0) return Rect.Empty;

            double minX = stroke.Points[0].X;
            double maxX = stroke.Points[0].X;
            double minY = stroke.Points[0].Y;
            double maxY = stroke.Points[0].Y;

            for (int i = 1; i < stroke.Points.Count; i++)
            {
                var p = stroke.Points[i];
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
            }

            double zoomAtCreation = stroke.ZoomAtCreation;
            if (double.IsNaN(zoomAtCreation) || double.IsInfinity(zoomAtCreation) || zoomAtCreation <= 0)
            {
                zoomAtCreation = 1.0;
            }

            double logical = stroke.Style.LogicalThicknessDip;
            if (double.IsNaN(logical) || double.IsInfinity(logical) || logical <= 0)
            {
                logical = 1.0;
            }

            double renderThickness = logical / zoomAtCreation;
            if (double.IsNaN(renderThickness) || double.IsInfinity(renderThickness) || renderThickness <= 0)
            {
                renderThickness = 1.0;
            }

            double radius = renderThickness * 0.5;
            if (radius < 0.5) radius = 0.5;

            return new Rect(
                minX - radius,
                minY - radius,
                (maxX - minX) + 2 * radius,
                (maxY - minY) + 2 * radius);
        }

        private static Geometry BuildStrokeGeometry(InkStrokeModel stroke)
        {
            int count = stroke.Points.Count;
            if (count == 0) return Geometry.Empty;

            double zoomAtCreation = stroke.ZoomAtCreation;
            if (double.IsNaN(zoomAtCreation) || double.IsInfinity(zoomAtCreation) || zoomAtCreation <= 0)
            {
                zoomAtCreation = 1.0;
            }

            double logical = stroke.Style.LogicalThicknessDip;
            if (double.IsNaN(logical) || double.IsInfinity(logical) || logical <= 0)
            {
                logical = 1.0;
            }

            double renderThickness = logical / zoomAtCreation;
            if (double.IsNaN(renderThickness) || double.IsInfinity(renderThickness) || renderThickness <= 0)
            {
                renderThickness = 1.0;
            }

            bool usesPressure = stroke.Style.UsesPressure;

            var points = stroke.Points;
            if (count == 1)
            {
                float p = usesPressure ? ClampPressure(points[0].Pressure) : 1.0f;
                double r = Math.Max(0.5, renderThickness * p * 0.5);
                return new EllipseGeometry(new Point(points[0].X, points[0].Y), r, r);
            }

            Point[] left = ArrayPool<Point>.Shared.Rent(count);
            Point[] right = ArrayPool<Point>.Shared.Rent(count);

            try
            {
                for (int i = 0; i < count; i++)
                {
                    var p = points[i];

                    Vector dirPrev = default;
                    Vector dirNext = default;

                    if (i > 0)
                    {
                        var prev = points[i - 1];
                        dirPrev = new Vector(p.X - prev.X, p.Y - prev.Y);
                    }
                    if (i < count - 1)
                    {
                        var next = points[i + 1];
                        dirNext = new Vector(next.X - p.X, next.Y - p.Y);
                    }

                    Vector dir = dirPrev + dirNext;
                    if (dir.LengthSquared < 1e-8)
                    {
                        dir = dirNext.LengthSquared >= 1e-8 ? dirNext : dirPrev;
                    }

                    if (dir.LengthSquared < 1e-8)
                    {
                        dir = new Vector(1, 0);
                    }

                    dir.Normalize();
                    var normal = new Vector(-dir.Y, dir.X);

                    float pressure = usesPressure ? ClampPressure(p.Pressure) : 1.0f;
                    double radius = Math.Max(0.5, renderThickness * pressure * 0.5);

                    left[i] = new Point(p.X + normal.X * radius, p.Y + normal.Y * radius);
                    right[i] = new Point(p.X - normal.X * radius, p.Y - normal.Y * radius);
                }

                var geom = new StreamGeometry
                {
                    FillRule = FillRule.Nonzero
                };

                using (var ctx = geom.Open())
                {
                    ctx.BeginFigure(left[0], isFilled: true, isClosed: true);
                    for (int i = 1; i < count; i++)
                    {
                        ctx.LineTo(left[i], isStroked: true, isSmoothJoin: false);
                    }
                    for (int i = count - 1; i >= 0; i--)
                    {
                        ctx.LineTo(right[i], isStroked: true, isSmoothJoin: false);
                    }
                }

                return geom;
            }
            finally
            {
                ArrayPool<Point>.Shared.Return(left, clearArray: false);
                ArrayPool<Point>.Shared.Return(right, clearArray: false);
            }
        }

        private SolidColorBrush GetBrush(Color color)
        {
            uint argb = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
            if (_brushCache.TryGetValue(argb, out var cached))
            {
                return cached;
            }

            var b = new SolidColorBrush(color);
            b.Freeze();
            _brushCache[argb] = b;
            return b;
        }

        private static InkPoint SanitizePoint(InkPoint p)
        {
            float pressure = ClampPressure(p.Pressure);
            return p with { Pressure = pressure };
        }

        private static float ClampPressure(float pressure)
        {
            if (float.IsNaN(pressure) || float.IsInfinity(pressure))
            {
                return DefaultPressure;
            }

            if (pressure < 0) return 0;
            if (pressure > 1) return 1;
            return pressure;
        }

        private static long PackTile(int x, int y)
        {
            unchecked
            {
                return ((long)x << 32) | (uint)y;
            }
        }

        private static void UnpackTile(long key, out int x, out int y)
        {
            unchecked
            {
                x = (int)(key >> 32);
                y = (int)(uint)key;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ClearAllCachesAndVisuals();
            _documentStrokes = null;
        }
    }
}
