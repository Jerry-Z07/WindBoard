using System;
using System.Collections.Generic;
using WindBoard.Board.Elements;

namespace WindBoard.Board.Commands
{
    /// <summary>
    /// 删除指定元素（可撤销）。
    /// </summary>
    internal sealed class RemoveElementCommand(BoardElement element) : IBoardCommand
    {
        private readonly BoardElement _element = element ?? throw new ArgumentNullException(nameof(element));
        private bool? _fromAboveInk;
        private int? _index;

        public void Do(BoardDocument document)
        {
            // 首次 Do：记录元素所在层与索引。
            if (_index is null || _fromAboveInk is null)
            {
                if (TryRemoveFromList(document.ElementsAboveInk, out int aboveIndex))
                {
                    _fromAboveInk = true;
                    _index = aboveIndex;
                    return;
                }

                if (TryRemoveFromList(document.ElementsBelowInk, out int belowIndex))
                {
                    _fromAboveInk = false;
                    _index = belowIndex;
                    return;
                }

                return;
            }

            // 重做：按记录尽量走索引路径；否则兜底按引用 Remove。
            List<BoardElement> list = _fromAboveInk.Value ? document.ElementsAboveInk : document.ElementsBelowInk;
            int recorded = _index.Value;
            if (recorded >= 0 && recorded < list.Count && ReferenceEquals(list[recorded], _element))
            {
                list.RemoveAt(recorded);
                return;
            }

            document.ElementsAboveInk.Remove(_element);
            document.ElementsBelowInk.Remove(_element);
        }

        public void Undo(BoardDocument document)
        {
            if (_index is not int index || _fromAboveInk is not bool fromAboveInk)
            {
                return;
            }

            if (document.ElementsAboveInk.Contains(_element) || document.ElementsBelowInk.Contains(_element))
            {
                return;
            }

            List<BoardElement> list = fromAboveInk ? document.ElementsAboveInk : document.ElementsBelowInk;
            int insertIndex = Math.Clamp(index, 0, list.Count);
            list.Insert(insertIndex, _element);
        }

        private bool TryRemoveFromList(List<BoardElement> list, out int index)
        {
            index = list.IndexOf(_element);
            if (index < 0)
            {
                return false;
            }

            list.RemoveAt(index);
            return true;
        }
    }
}

