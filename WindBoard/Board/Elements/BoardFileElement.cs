namespace WindBoard.Board.Elements
{
    /// <summary>
    /// 文件元素：承载导入的常见文档与未知类型文件，以占位卡片显示。
    /// 选择工具下单击可选中，双击经系统默认应用外部打开。
    /// </summary>
    internal sealed class BoardFileElement : BoardElement
    {
        /// <summary>
        /// 源文件路径（用于双击外部打开）。
        /// </summary>
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>
        /// 展示名称（默认取文件名）。
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;
    }
}

