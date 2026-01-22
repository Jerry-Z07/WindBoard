using System.Collections.Generic;
using System.Windows.Ink;

namespace WindBoard.Services
{
    internal sealed class StrokeUndoHistory
    {
        private sealed class Delta
        {
            public HashSet<Stroke> Added { get; } = new HashSet<Stroke>();
            public HashSet<Stroke> Removed { get; } = new HashSet<Stroke>();

            public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;

            public void Record(StrokeCollectionChangedEventArgs e)
            {
                if (e.Added != null)
                {
                    foreach (var s in e.Added)
                    {
                        if (!Removed.Remove(s))
                        {
                            Added.Add(s);
                        }
                    }
                }

                if (e.Removed != null)
                {
                    foreach (var s in e.Removed)
                    {
                        if (!Added.Remove(s))
                        {
                            Removed.Add(s);
                        }
                    }
                }
            }

            public void Undo(StrokeCollection strokes)
            {
                foreach (var s in Added)
                {
                    if (strokes.Contains(s))
                    {
                        strokes.Remove(s);
                    }
                }

                foreach (var s in Removed)
                {
                    if (!strokes.Contains(s))
                    {
                        strokes.Add(s);
                    }
                }
            }

            public void Redo(StrokeCollection strokes)
            {
                foreach (var s in Removed)
                {
                    if (strokes.Contains(s))
                    {
                        strokes.Remove(s);
                    }
                }

                foreach (var s in Added)
                {
                    if (!strokes.Contains(s))
                    {
                        strokes.Add(s);
                    }
                }
            }
        }

        private readonly Stack<Delta> _undo = new Stack<Delta>();
        private readonly Stack<Delta> _redo = new Stack<Delta>();
        private Delta? _current;
        private int _suspendCount;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public bool IsInTransaction => _current != null;

        public void Begin()
        {
            if (_suspendCount > 0) return;
            _current ??= new Delta();
        }

        public void End()
        {
            if (_suspendCount > 0)
            {
                _current = null;
                return;
            }

            if (_current == null) return;

            if (!_current.IsEmpty)
            {
                _undo.Push(_current);
                _redo.Clear();
            }

            _current = null;
        }

        public void Cancel()
        {
            _current = null;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            _current = null;
        }

        public void Record(StrokeCollectionChangedEventArgs e)
        {
            if (_suspendCount > 0) return;
            if (_current == null) return;
            _current.Record(e);
        }

        public void Undo(StrokeCollection strokes)
        {
            if (_suspendCount > 0) return;
            if (_undo.Count == 0) return;

            var d = _undo.Pop();
            using (SuspendRecording())
            {
                d.Undo(strokes);
            }
            _redo.Push(d);
        }

        public void Redo(StrokeCollection strokes)
        {
            if (_suspendCount > 0) return;
            if (_redo.Count == 0) return;

            var d = _redo.Pop();
            using (SuspendRecording())
            {
                d.Redo(strokes);
            }
            _undo.Push(d);
        }

        public System.IDisposable SuspendRecording()
        {
            _suspendCount++;
            return new SuspendHandle(this);
        }

        private sealed class SuspendHandle : System.IDisposable
        {
            private StrokeUndoHistory? _owner;

            public SuspendHandle(StrokeUndoHistory owner) => _owner = owner;

            public void Dispose()
            {
                var o = _owner;
                if (o == null) return;
                _owner = null;
                o._suspendCount--;
                if (o._suspendCount < 0) o._suspendCount = 0;
            }
        }
    }
}
