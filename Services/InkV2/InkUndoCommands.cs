using System;
using System.Collections.Generic;
using WindBoard.Models.InkV2;

namespace WindBoard.Services.InkV2
{
    internal sealed class InsertStrokeCommand : IInkUndoableCommand
    {
        private readonly int _index;
        private readonly InkStroke _stroke;

        public InsertStrokeCommand(int index, InkStroke stroke)
        {
            _index = index;
            _stroke = stroke ?? throw new ArgumentNullException(nameof(stroke));
        }

        public void Undo(InkDocument document)
        {
            _ = document.Strokes.Remove(_stroke);
        }

        public void Redo(InkDocument document)
        {
            if (document.Strokes.Contains(_stroke))
            {
                return;
            }

            int index = Math.Clamp(_index, 0, document.Strokes.Count);
            document.Strokes.Insert(index, _stroke);
        }
    }

    internal sealed class RemoveStrokeCommand : IInkUndoableCommand
    {
        private readonly int _index;
        private readonly InkStroke _stroke;

        public RemoveStrokeCommand(int index, InkStroke stroke)
        {
            _index = index;
            _stroke = stroke ?? throw new ArgumentNullException(nameof(stroke));
        }

        public void Undo(InkDocument document)
        {
            if (document.Strokes.Contains(_stroke))
            {
                return;
            }

            int index = Math.Clamp(_index, 0, document.Strokes.Count);
            document.Strokes.Insert(index, _stroke);
        }

        public void Redo(InkDocument document)
        {
            _ = document.Strokes.Remove(_stroke);
        }
    }

    internal sealed class ReorderStrokesCommand : IInkUndoableCommand
    {
        private readonly List<InkStroke> _before;
        private readonly List<InkStroke> _after;

        public ReorderStrokesCommand(List<InkStroke> before, List<InkStroke> after)
        {
            _before = before ?? throw new ArgumentNullException(nameof(before));
            _after = after ?? throw new ArgumentNullException(nameof(after));
        }

        public void Undo(InkDocument document)
        {
            Apply(document, _before);
        }

        public void Redo(InkDocument document)
        {
            Apply(document, _after);
        }

        private static void Apply(InkDocument document, List<InkStroke> order)
        {
            document.Strokes.Clear();
            for (int i = 0; i < order.Count; i++)
            {
                document.Strokes.Add(order[i]);
            }
        }
    }

    internal sealed class ReplaceStrokeFragmentsCommand : IInkUndoableCommand
    {
        private readonly InkStroke _stroke;
        private readonly List<InkFragment> _before;
        private readonly List<InkFragment> _after;

        public ReplaceStrokeFragmentsCommand(InkStroke stroke, List<InkFragment> before, List<InkFragment> after)
        {
            _stroke = stroke ?? throw new ArgumentNullException(nameof(stroke));
            _before = before ?? throw new ArgumentNullException(nameof(before));
            _after = after ?? throw new ArgumentNullException(nameof(after));
        }

        public void Undo(InkDocument document)
        {
            Apply(_stroke, _before);
        }

        public void Redo(InkDocument document)
        {
            Apply(_stroke, _after);
        }

        private static void Apply(InkStroke stroke, List<InkFragment> fragments)
        {
            stroke.Fragments.Clear();
            for (int i = 0; i < fragments.Count; i++)
            {
                stroke.Fragments.Add(fragments[i]);
            }
        }
    }

    internal sealed class ReplaceFragmentPointsCommand : IInkUndoableCommand
    {
        private readonly InkFragment _fragment;
        private readonly InkPoint[] _before;
        private readonly InkPoint[] _after;

        public ReplaceFragmentPointsCommand(InkFragment fragment, InkPoint[] before, InkPoint[] after)
        {
            _fragment = fragment ?? throw new ArgumentNullException(nameof(fragment));
            _before = before ?? throw new ArgumentNullException(nameof(before));
            _after = after ?? throw new ArgumentNullException(nameof(after));
        }

        public void Undo(InkDocument document)
        {
            Apply(_fragment, _before);
        }

        public void Redo(InkDocument document)
        {
            Apply(_fragment, _after);
        }

        private static void Apply(InkFragment fragment, InkPoint[] points)
        {
            fragment.Points.Clear();
            for (int i = 0; i < points.Length; i++)
            {
                fragment.Points.Add(points[i]);
            }
            fragment.PointsVersion++;
        }
    }
}
