using System;
using System.Collections.Generic;
using WindBoard.Models.InkV2;

namespace WindBoard.Services.InkV2
{
    internal sealed class InkUndoHistory
    {
        private sealed class Batch
        {
            public List<IInkUndoableCommand> Commands { get; } = new List<IInkUndoableCommand>(8);

            public bool IsEmpty => Commands.Count == 0;

            public void Undo(InkDocument document)
            {
                for (int i = Commands.Count - 1; i >= 0; i--)
                {
                    Commands[i].Undo(document);
                }
            }

            public void Redo(InkDocument document)
            {
                for (int i = 0; i < Commands.Count; i++)
                {
                    Commands[i].Redo(document);
                }
            }
        }

        private readonly Stack<Batch> _undo = new Stack<Batch>();
        private readonly Stack<Batch> _redo = new Stack<Batch>();
        private Batch? _current;
        private int _suspendCount;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public bool IsInTransaction => _current != null;

        public void Begin()
        {
            if (_suspendCount > 0) return;
            _current ??= new Batch();
        }

        public void End()
        {
            if (_suspendCount > 0)
            {
                _current = null;
                return;
            }

            Batch? batch = _current;
            if (batch == null) return;

            if (!batch.IsEmpty)
            {
                _undo.Push(batch);
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

        public void Record(IInkUndoableCommand command)
        {
            if (_suspendCount > 0) return;
            if (_current == null) return;
            if (command == null) throw new ArgumentNullException(nameof(command));

            _current.Commands.Add(command);
        }

        public void Undo(InkDocument document)
        {
            if (_suspendCount > 0) return;
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (_undo.Count == 0) return;

            Batch batch = _undo.Pop();
            using (SuspendRecording())
            {
                batch.Undo(document);
            }
            _redo.Push(batch);
        }

        public void Redo(InkDocument document)
        {
            if (_suspendCount > 0) return;
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (_redo.Count == 0) return;

            Batch batch = _redo.Pop();
            using (SuspendRecording())
            {
                batch.Redo(document);
            }
            _undo.Push(batch);
        }

        public IDisposable SuspendRecording()
        {
            _suspendCount++;
            return new SuspendHandle(this);
        }

        private sealed class SuspendHandle : IDisposable
        {
            private InkUndoHistory? _owner;

            public SuspendHandle(InkUndoHistory owner) => _owner = owner;

            public void Dispose()
            {
                InkUndoHistory? owner = _owner;
                if (owner == null) return;
                _owner = null;

                owner._suspendCount--;
                if (owner._suspendCount < 0) owner._suspendCount = 0;
            }
        }
    }
}

