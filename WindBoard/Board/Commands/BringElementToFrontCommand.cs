using System;
using WindBoard.Board.Elements;

namespace WindBoard.Board.Commands
{
    /// <summary>
    /// 将指定元素置顶：
    /// - 若元素在“笔迹下层”，则移动到“笔迹上层”（跨层置顶）；
    /// - 若元素已在“笔迹上层”，则移动到列表末尾（同层置顶）。
    /// </summary>
    internal sealed class BringElementToFrontCommand(BoardElement element) : IBoardCommand
    {
        private readonly BoardElement _element = element ?? throw new ArgumentNullException(nameof(element));
        private bool? _fromAboveInk;
        private int? _fromIndex;

        public void Do(BoardDocument document)
        {
            // 首次 Do：定位并记录来源位置。
            if (_fromIndex is null || _fromAboveInk is null)
            {
                int idxAbove = document.ElementsAboveInk.IndexOf(_element);
                if (idxAbove >= 0)
                {
                    // 已经是同层最上层：不产生命令效果，也不记录 Undo 信息。
                    if (idxAbove == document.ElementsAboveInk.Count - 1)
                    {
                        return;
                    }

                    _fromAboveInk = true;
                    _fromIndex = idxAbove;
                    document.ElementsAboveInk.RemoveAt(idxAbove);
                    document.ElementsAboveInk.Add(_element);
                    return;
                }

                int idxBelow = document.ElementsBelowInk.IndexOf(_element);
                if (idxBelow < 0)
                {
                    return;
                }

                _fromAboveInk = false;
                _fromIndex = idxBelow;
                document.ElementsBelowInk.RemoveAt(idxBelow);
                document.ElementsAboveInk.Add(_element);
                return;
            }

            // 重做：不依赖当前所在层，直接移除后追加到“上层”末尾。
            document.ElementsAboveInk.Remove(_element);
            document.ElementsBelowInk.Remove(_element);
            document.ElementsAboveInk.Add(_element);
        }

        public void Undo(BoardDocument document)
        {
            if (_fromIndex is not int fromIndex || _fromAboveInk is not bool fromAboveInk)
            {
                return;
            }

            document.ElementsAboveInk.Remove(_element);
            document.ElementsBelowInk.Remove(_element);

            var target = fromAboveInk ? document.ElementsAboveInk : document.ElementsBelowInk;
            int insertIndex = Math.Clamp(fromIndex, 0, target.Count);
            target.Insert(insertIndex, _element);
        }
    }
}

