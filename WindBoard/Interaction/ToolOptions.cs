using Vortice.Mathematics;

namespace WindBoard.Interaction
{
    /// <summary>
    /// 绘制参数值对象（design C 节，参数链收敛）：承载"当前工具/颜色/粗细/压感"，
    /// 替代 <c>MainWindow → BoardCanvasControl → BoardInputController → Stroke</c> 的逐跳属性复制链。
    /// </summary>
    /// <remarks>
    /// - 语义约定：参数只在 <see cref="WindBoard.Interaction.Tools.IBoardTool.Begin"/> 读取一次
    ///   （"仅影响后续新建笔迹"），Begin 时值拷贝进新建条目，Move 不重读参数；
    /// - 相等语义：record struct 的默认相等含 <see cref="Color4"/> 的浮点位级比较，
    ///   调用方不应依赖整体相等做副作用判定（例如"参数未变就跳过同步"）；
    ///   需要短路时基于具体分量判定（如 <see cref="BoardTool"/> 枚举）；
    /// - "当前选中值不持久化"现状不变（见父 PRD R4 边界）。
    /// </remarks>
    internal readonly record struct ToolOptions(
        BoardTool Tool,
        Color4 PenColor,
        float PenBaseSize,
        bool PenEnablePressure);
}
