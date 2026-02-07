using System;
using System.Numerics;
using WindBoard.Board.Elements;

namespace WindBoard.Board.Commands
{
    /// <summary>
    /// 更新元素变换：
    /// - 平移：PositionWorld
    /// - 缩放：SizeWorld（可选）
    /// </summary>
    internal sealed class UpdateElementTransformCommand(
        BoardElement element,
        Vector2 beforePositionWorld,
        Vector2 afterPositionWorld,
        Vector2? beforeSizeWorld = null,
        Vector2? afterSizeWorld = null) : IBoardCommand
    {
        private readonly BoardElement _element = element ?? throw new ArgumentNullException(nameof(element));
        private readonly Vector2 _before = beforePositionWorld;
        private readonly Vector2 _after = afterPositionWorld;
        private readonly Vector2? _beforeSize = beforeSizeWorld;
        private readonly Vector2? _afterSize = afterSizeWorld;

        public void Do(BoardDocument document)
        {
            _element.PositionWorld = _after;

            if (_afterSize is Vector2 size)
            {
                _element.SizeWorld = size;
            }
        }

        public void Undo(BoardDocument document)
        {
            _element.PositionWorld = _before;

            if (_beforeSize is Vector2 size)
            {
                _element.SizeWorld = size;
            }
        }
    }
}
