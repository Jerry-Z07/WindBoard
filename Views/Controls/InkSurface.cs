using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace WindBoard.Controls
{
    public sealed class InkSurface : FrameworkElement
    {
        private readonly VisualCollection _visuals;
        private readonly DrawingVisual _dynamicVisual = new DrawingVisual();
        private readonly Dictionary<long, DrawingVisual> _tileVisuals = new Dictionary<long, DrawingVisual>();

        public InkSurface()
        {
            _visuals = new VisualCollection(this)
            {
                _dynamicVisual
            };
        }

        internal DrawingVisual GetDynamicVisual() => _dynamicVisual;

        internal DrawingVisual GetOrCreateTileVisual(int tileX, int tileY)
        {
            long key = PackTile(tileX, tileY);
            if (_tileVisuals.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var dv = new DrawingVisual();

            int dynamicIndex = _visuals.Count - 1;
            if (dynamicIndex < 0) dynamicIndex = 0;
            _visuals.Insert(dynamicIndex, dv);
            _tileVisuals[key] = dv;
            return dv;
        }

        internal bool TryRemoveTileVisual(int tileX, int tileY)
        {
            long key = PackTile(tileX, tileY);
            if (!_tileVisuals.TryGetValue(key, out var dv))
            {
                return false;
            }

            _tileVisuals.Remove(key);
            _visuals.Remove(dv);
            return true;
        }

        internal void ClearTiles()
        {
            if (_tileVisuals.Count == 0) return;

            foreach (var dv in _tileVisuals.Values)
            {
                _visuals.Remove(dv);
            }
            _tileVisuals.Clear();
        }

        internal int TileVisualCount => _tileVisuals.Count;

        protected override int VisualChildrenCount => _visuals.Count;

        protected override Visual GetVisualChild(int index)
        {
            if (index < 0 || index >= _visuals.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _visuals[index];
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (!double.IsNaN(Width) && !double.IsNaN(Height) && Width > 0 && Height > 0)
            {
                return new Size(Width, Height);
            }

            return base.MeasureOverride(availableSize);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            return finalSize;
        }

        private static long PackTile(int x, int y)
        {
            unchecked
            {
                return ((long)x << 32) | (uint)y;
            }
        }
    }
}
