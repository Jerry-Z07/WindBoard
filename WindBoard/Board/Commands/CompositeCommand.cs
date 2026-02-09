using System;
using System.Collections.Generic;
using WindBoard.Board;

namespace WindBoard.Board.Commands
{
    /// <summary>
    /// 复合命令：把多个命令视为一次撤销记录。
    /// </summary>
    /// <remarks>
    /// 典型用途：
    /// - 框选多笔迹后，整体移动/缩放/旋转；
    /// - 批量置顶/复制/删除等操作（一次 Ctrl+Z 撤销整组）。
    /// </remarks>
    internal sealed class CompositeCommand : IBoardCommand
    {
        private readonly List<IBoardCommand> _commands;

        public CompositeCommand(IEnumerable<IBoardCommand> commands)
        {
            if (commands is null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            _commands = new List<IBoardCommand>();
            foreach (IBoardCommand cmd in commands)
            {
                if (cmd is not null)
                {
                    _commands.Add(cmd);
                }
            }
        }

        public void Do(BoardDocument document)
        {
            for (int i = 0; i < _commands.Count; i++)
            {
                _commands[i].Do(document);
            }
        }

        public void Undo(BoardDocument document)
        {
            // 撤销需反向执行，保证复合操作可恢复到原始状态。
            for (int i = _commands.Count - 1; i >= 0; i--)
            {
                _commands[i].Undo(document);
            }
        }
    }
}

