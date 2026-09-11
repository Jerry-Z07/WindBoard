namespace WindBoard.Interaction
{
    /// <summary>
    /// 画板当前使用的工具。
    /// </summary>
    /// <remarks>
    /// 形状工具（阶段二 R5）复用 Pen 的颜色/粗细参数（<see cref="ToolOptions"/> 不扩展），
    /// 工具身份由本枚举承载；形状种类与工具 id 的绑定在 <c>ShapeTool</c> 构造处单点映射。
    /// </remarks>
    internal enum BoardTool
    {
        /// <summary>
        /// 选择/浏览模式（当前仅用于占位：暂不支持笔迹选择，默认提供“拖拽平移”等基础浏览能力）。
        /// </summary>
        Select,

        Pen,
        Eraser,

        /// <summary>直线形状工具（两点式：按下定起点、拖动定终点）。</summary>
        Line,

        /// <summary>矩形形状工具。</summary>
        Rectangle,

        /// <summary>椭圆形状工具（宽高相等即圆）。</summary>
        Ellipse,

        /// <summary>箭头形状工具（直线 + End 端点箭头头部）。</summary>
        Arrow,
    }
}

