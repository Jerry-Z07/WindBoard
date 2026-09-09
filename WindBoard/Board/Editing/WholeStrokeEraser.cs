using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Items;

namespace WindBoard.Board.Editing
{
    /// <summary>
    /// 整笔擦除：只要橡皮擦轨迹命中某条笔迹，则直接删除该条目对象。
    /// </summary>
    internal sealed class WholeStrokeEraser : IBoardEraser
    {
        public bool Erase(BoardDocument document, Vector2 fromWorld, Vector2 toWorld, Vector2 radiusWorld)
        {
            if (document.InkItems.Count == 0)
            {
                return false;
            }

            bool changed = false;

            // 反向遍历，便于在命中时安全 RemoveAt。
            // 命中判断按条目类型单点分发（折线走线段距离算法，其它条目走 Bounds 通用路径）。
            for (int i = document.InkItems.Count - 1; i >= 0; i--)
            {
                IBoardInkItem item = document.InkItems[i];
                if (InkItemHitTest.IsInkItemHitByEraserSegment(item, fromWorld, toWorld, radiusWorld))
                {
                    document.InkItems.RemoveAt(i);
                    changed = true;
                }
            }

            return changed;
        }
    }
}
