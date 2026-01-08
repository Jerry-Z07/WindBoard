using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using WindBoard.Controls;
using WindBoard.Core.Ink.Adapters;
using WindBoard.Core.Ink.Backend;
using WindBoard.Models.Ink;

namespace WindBoard.Services.Ink
{
    public sealed class InkService
    {
        private readonly InkCanvas _canvas;
        private readonly InkSurface? _surface;
        private CustomInkBackend? _backend;
        private int _suppressChangeTrackingCount;

        public InkService(InkCanvas canvas)
            : this(canvas, surface: null)
        {
        }

        public InkService(InkCanvas canvas, InkSurface? surface)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _surface = surface;
        }

        public BoardPage? CurrentPage { get; private set; }

        public event EventHandler? InkChanged;

        public void SetBackend(IInkBackend backend)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            _backend = backend as CustomInkBackend
                ?? throw new ArgumentException("InkService requires CustomInkBackend after the legacy InkCanvas backend is removed.", nameof(backend));
        }

        public void BindPage(BoardPage? page)
        {
            DetachBackendEvents();

            CurrentPage = page;

            if (page == null)
            {
                _canvas.Strokes = new StrokeCollection();
                if (_surface != null) _surface.Visibility = Visibility.Collapsed;
                if (_backend != null)
                {
                    try { _backend.UnbindDocument(); } catch { }
                }
                return;
            }

            EnsureBackendConfigured();

            if (_surface != null)
            {
                _surface.Visibility = Visibility.Visible;
            }

            if (page.InkStrokes.Count == 0 && page.Strokes != null && page.Strokes.Count > 0)
            {
                // Migration path: legacy pages may still have ISF-backed StrokeCollection.
                // Convert once into the backend-agnostic model.
                double zoom = page.Zoom <= 0 ? 1.0 : page.Zoom;
                var converted = WpfStrokeAdapter.ToModelList(page.Strokes, currentZoom: zoom);
                page.InkStrokes.Clear();
                page.InkStrokes.AddRange(converted);

                // Drop legacy strokes to reduce memory now that InkCanvas backend is removed.
                page.Strokes = new StrokeCollection();
            }

            _canvas.Strokes = new StrokeCollection();
            _backend!.BindDocument(page.InkStrokes);
            _backend.StrokesChanged += Backend_StrokesChanged;
        }

        public void SaveCurrentPage()
        {
            // No-op: ink is stored in the page's model and bound directly to the custom backend.
        }

        public IDisposable SuppressChangeTracking()
        {
            _suppressChangeTrackingCount++;
            return new SuppressHandle(this);
        }

        private void EnsureBackendConfigured()
        {
            if (_backend == null)
            {
                throw new InvalidOperationException("InkService backend is not configured. Call SetBackend(...) before binding pages.");
            }
        }

        private void DetachBackendEvents()
        {
            if (_backend == null) return;
            _backend.StrokesChanged -= Backend_StrokesChanged;
        }

        private void Backend_StrokesChanged(object? sender, InkStrokeCollectionChangedEventArgs e)
        {
            var page = CurrentPage;
            page?.InkUndoHistory.Record(e.Added, e.Removed);

            if (_suppressChangeTrackingCount > 0) return;
            if (page == null) return;

            page.ContentVersion++;
            InkChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool CanUndo => CurrentPage?.InkUndoHistory.CanUndo == true;

        public bool CanRedo => CurrentPage?.InkUndoHistory.CanRedo == true;

        public bool IsInUndoTransaction => CurrentPage?.InkUndoHistory.IsInTransaction == true;

        public void BeginUndoTransaction()
        {
            var page = CurrentPage;
            if (page == null) return;
            page.InkUndoHistory.Begin();
        }

        public void EndUndoTransaction()
        {
            var page = CurrentPage;
            if (page == null) return;
            page.InkUndoHistory.End();
        }

        public void CancelUndoTransaction()
        {
            var page = CurrentPage;
            if (page == null) return;
            page.InkUndoHistory.Cancel();
        }

        public void Undo()
        {
            var page = CurrentPage;
            if (page == null) return;
            EnsureBackendConfigured();

            page.InkUndoHistory.Undo(page.InkStrokes);
            RebindBackendDocument(page);
            NotifyInkChanged(page);
        }

        public void Redo()
        {
            var page = CurrentPage;
            if (page == null) return;
            EnsureBackendConfigured();

            page.InkUndoHistory.Redo(page.InkStrokes);
            RebindBackendDocument(page);
            NotifyInkChanged(page);
        }

        public void ClearInk()
        {
            var page = CurrentPage;
            if (page == null) return;
            EnsureBackendConfigured();

            page.InkStrokes.Clear();
            RebindBackendDocument(page);
            NotifyInkChanged(page);
        }

        public void ShiftContent(double dx, double dy)
        {
            if (dx == 0 && dy == 0) return;

            var page = CurrentPage;
            if (page == null) return;

            for (int i = 0; i < page.Attachments.Count; i++)
            {
                var att = page.Attachments[i];
                att.X += dx;
                att.Y += dy;
            }

            EnsureBackendConfigured();

            ShiftInkModelPoints(page.InkStrokes, dx, dy);
            RebindBackendDocument(page);
            NotifyInkChanged(page);
        }

        private static void ShiftInkModelPoints(List<InkStrokeModel> strokes, double dx, double dy)
        {
            if (strokes == null || strokes.Count == 0) return;

            for (int i = 0; i < strokes.Count; i++)
            {
                var stroke = strokes[i];
                if (stroke == null) continue;
                var pts = stroke.Points;
                for (int j = 0; j < pts.Count; j++)
                {
                    var p = pts[j];
                    pts[j] = p with { X = p.X + dx, Y = p.Y + dy };
                }
            }
        }

        private void RebindBackendDocument(BoardPage page)
        {
            if (_backend == null) return;
            _backend.StrokesChanged -= Backend_StrokesChanged;
            _backend.BindDocument(page.InkStrokes);
            _backend.StrokesChanged += Backend_StrokesChanged;
        }

        private void NotifyInkChanged(BoardPage page)
        {
            if (_suppressChangeTrackingCount > 0) return;
            page.ContentVersion++;
            InkChanged?.Invoke(this, EventArgs.Empty);
        }

        private sealed class SuppressHandle : IDisposable
        {
            private InkService? _owner;

            public SuppressHandle(InkService owner) => _owner = owner;

            public void Dispose()
            {
                var o = _owner;
                if (o == null) return;
                _owner = null;

                o._suppressChangeTrackingCount--;
                if (o._suppressChangeTrackingCount < 0) o._suppressChangeTrackingCount = 0;
            }
        }
    }
}
