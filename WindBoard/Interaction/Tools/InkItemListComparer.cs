using System.Collections.Generic;
using WindBoard.Board.Items;

namespace WindBoard.Interaction.Tools
{
    /// <summary>
    /// 绘制条目列表的引用级比较（用于快照前后判断"是否实际发生变化"）。
    /// </summary>
    /// <remarks>
    /// 原控制器 <c>IsSameStrokeList</c> 的共享化：擦除快照（<see cref="IBoardInkItem"/> 列表）与
    /// 选择集（<see cref="WindBoard.Board.Stroke"/> 列表）都经 IReadOnlyList 协变传入。
    /// </remarks>
    internal static class InkItemListComparer
    {
        internal static bool IsSameList(IReadOnlyList<IBoardInkItem> a, IReadOnlyList<IBoardInkItem> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (!ReferenceEquals(a[i], b[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
