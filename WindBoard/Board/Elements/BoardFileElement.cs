namespace WindBoard.Board.Elements
{
    /// <summary>
    /// 文件元素：用于导入常见文档/未知类型文件。
    /// 
    /// 当前行为：
    /// - 以“占位卡片”形式显示；
    /// - 在选择工具下单击可选中；
    /// - 双击可调用系统默认应用外部打开（见交互层实现）。
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

