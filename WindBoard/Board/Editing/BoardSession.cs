using System;
using System.Collections.Generic;
using WindBoard.Board.Commands;

namespace WindBoard.Board.Editing
{
    internal sealed class BoardSession
    {
        private readonly Stack<IBoardCommand> _undoStack = new();
        private readonly Stack<IBoardCommand> _redoStack = new();

        public BoardDocument Document { get; } = new();

        public event Action? StateChanged;

        public bool CanUndo => _undoStack.Count > 0;

        public bool CanRedo => _redoStack.Count > 0;

        public bool HasStrokes => Document.Strokes.Count > 0;

        /// <summary>
        /// 判断指定命令是否位于撤销栈栈顶。
        /// </summary>
        /// <remarks>
        /// 用途示例：在 UI 层将“置顶”按钮做成可再次点击撤销的“切换式”交互，但仅允许撤销最近一次操作，避免误撤销其它命令。
        /// </remarks>
        internal bool IsUndoStackTop(IBoardCommand command)
        {
            if (command is null || _undoStack.Count == 0)
            {
                return false;
            }

            return ReferenceEquals(_undoStack.Peek(), command);
        }

        public void Execute(IBoardCommand command)
        {
            command.Do(Document);
            _undoStack.Push(command);
            _redoStack.Clear();
            StateChanged?.Invoke();
        }

        public void Undo()
        {
            if (!_undoStack.TryPop(out IBoardCommand? command))
            {
                return;
            }

            command.Undo(Document);
            _redoStack.Push(command);
            StateChanged?.Invoke();
        }

        public void Redo()
        {
            if (!_redoStack.TryPop(out IBoardCommand? command))
            {
                return;
            }

            command.Do(Document);
            _undoStack.Push(command);
            StateChanged?.Invoke();
        }

        public void ClearAll()
        {
            if (Document.Strokes.Count == 0)
            {
                return;
            }

            Execute(new ClearCommand(new List<Stroke>(Document.Strokes)));
        }
    }
}
