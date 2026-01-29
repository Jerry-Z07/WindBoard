using System.Numerics;
using WindBoard.Board;

namespace WindBoard.Board.Editing
{
    /// <summary>
    /// 擦除策略接口：输入为世界坐标下的“橡皮擦轨迹线段 + 半径（支持 X/Y 不同）”，实现可对文档执行整笔擦除或局部擦除（分段）等逻辑。
    /// </summary>
    internal interface IBoardEraser
    {
        /// <summary>
        /// 对文档执行一次擦除。
        /// </summary>
        /// <param name="document">要修改的文档。</param>
        /// <param name="fromWorld">橡皮擦轨迹起点（世界坐标）。</param>
        /// <param name="toWorld">橡皮擦轨迹终点（世界坐标）。</param>
        /// <param name="radiusWorld">橡皮擦半径（世界坐标，X/Y 分量分别表示水平/垂直半径）。</param>
        /// <returns>若文档发生变化（删除/分割/替换笔迹）则返回 true。</returns>
        bool Erase(BoardDocument document, Vector2 fromWorld, Vector2 toWorld, Vector2 radiusWorld);
    }
}
