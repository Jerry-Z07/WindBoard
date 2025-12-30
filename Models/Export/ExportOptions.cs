namespace WindBoard.Models.Export
{
    /// <summary>
    /// 导出范围
    /// </summary>
    public enum ExportScope
    {
        /// <summary>仅当前页</summary>
        CurrentPage,
        /// <summary>全部页面</summary>
        AllPages
    }

    /// <summary>
    /// 导出格式
    /// </summary>
    public enum ExportFormat
    {
        /// <summary>PNG 图片</summary>
        Png,
        /// <summary>JPG 图片</summary>
        Jpg,
        /// <summary>PDF 文档</summary>
        Pdf,
        /// <summary>WBI 自有格式</summary>
        Wbi
    }

    /// <summary>
    /// 导出进度报告
    /// </summary>
    public sealed class ExportProgress
    {
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public string? StatusMessage { get; set; }
        public double Percentage => TotalPages > 0 ? (double)CurrentPage / TotalPages * 100 : 0;
    }

    /// <summary>
    /// 导出选项基类
    /// </summary>
    public abstract class ExportOptionsBase
    {
        /// <summary>导出范围</summary>
        public ExportScope Scope { get; set; } = ExportScope.CurrentPage;
    }
}
