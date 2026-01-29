using System.Numerics;
using WindBoard.Board;

namespace WindBoard.Board.Editing
{
    /// <summary>
    /// 整笔擦除：只要橡皮擦轨迹命中某条笔迹，则直接删除该笔迹对象。
    /// </summary>
    internal sealed class WholeStrokeEraser : IBoardEraser
    {
        public bool Erase(BoardDocument document, Vector2 fromWorld, Vector2 toWorld, Vector2 radiusWorld)
        {
            if (document.Strokes.Count == 0)
            {
                return false;
            }

            bool changed = false;

            // 反向遍历，便于在命中时安全 RemoveAt。
            for (int i = document.Strokes.Count - 1; i >= 0; i--)
            {
                Stroke stroke = document.Strokes[i];
                if (StrokeHitTest.IsStrokeHitByEraserSegment(stroke, fromWorld, toWorld, radiusWorld))
                {
                    document.Strokes.RemoveAt(i);
                    changed = true;
                }
            }

            return changed;
        }
    }
}
