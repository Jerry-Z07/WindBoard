using Vortice.Mathematics;

namespace WindBoard.Interaction
{
    /// <summary>
    /// 绘制参数值对象（design C 节，参数链收敛）：承载"当前工具/颜色/粗细/压感"，
    /// 替代 <c>MainWindow → BoardCanvasControl → BoardInputController → Stroke</c> 的逐跳属性复制链。
    /// </summary>
    /// <remarks>
    /// - 参数只在 <see cref="WindBoard.Interaction.Tools.IBoardTool.Begin"/> 读取一次，值拷贝进新建条目，Move 不重读，因此只影响后续新建笔迹。
    /// - record struct 的默认相等含 <see cref="Color4"/> 的浮点位级比较，调用方不应依赖整体相等做副作用判定，需要短路时按具体分量判断（如 <see cref="BoardTool"/> 枚举）。
    /// - 当前选中值不持久化。
    /// </remarks>
    internal readonly record struct ToolOptions(
        BoardTool Tool,
        Color4 PenColor,
        float PenBaseSize,
        bool PenEnablePressure);
}
