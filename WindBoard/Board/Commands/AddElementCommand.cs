using System;
using System.Collections.Generic;
using WindBoard.Board.Elements;

namespace WindBoard.Board.Commands
{
    /// <summary>
    /// 添加元素（可撤销）。
    /// </summary>
    internal sealed class AddElementCommand(BoardElement element, bool aboveInk) : IBoardCommand
    {
        private readonly BoardElement _element = element ?? throw new ArgumentNullException(nameof(element));
        private readonly bool _aboveInk = aboveInk;
        private int? _index;

        public void Do(BoardDocument document)
        {
            List<BoardElement> list = GetTargetList(document);
            _index ??= list.Count;
            list.Insert(_index.Value, _element);
        }

        public void Undo(BoardDocument document)
        {
            List<BoardElement> list = GetTargetList(document);

            if (_index is int index && index >= 0 && index < list.Count && ReferenceEquals(list[index], _element))
            {
                list.RemoveAt(index);
                return;
            }

            list.Remove(_element);
        }

        private List<BoardElement> GetTargetList(BoardDocument document)
        {
            return _aboveInk ? document.ElementsAboveInk : document.ElementsBelowInk;
        }
    }
}

