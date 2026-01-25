namespace WindBoard.Interaction
{
    /// <summary>
    /// 画板当前使用的工具。
    /// </summary>
    internal enum BoardTool
    {
        /// <summary>
        /// 选择/浏览模式（当前仅用于占位：暂不支持笔迹选择，默认提供“拖拽平移”等基础浏览能力）。
        /// </summary>
        Select,

        Pen,
        Eraser,
    }
}
