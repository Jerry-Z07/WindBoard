namespace WindBoard.Models.Export
{
    /// <summary>
    /// PDF 纸张方向
    /// </summary>
    public enum PdfOrientation
    {
        /// <summary>自动（根据内容决定）</summary>
        Auto,
        /// <summary>横向</summary>
        Landscape,
        /// <summary>纵向</summary>
        Portrait
    }

    /// <summary>
    /// PDF 缩放模式
    /// </summary>
    public enum PdfScaleMode
    {
        /// <summary>适合页面（保持比例，留白）</summary>
        FitPage,
        /// <summary>填满页面（可能裁剪）</summary>
        FillPage
    }

    /// <summary>
    /// PDF 导出选项
    /// </summary>
    public sealed class PdfExportOptions : ExportOptionsBase
    {
        /// <summary>纸张方向</summary>
        public PdfOrientation Orientation { get; set; } = PdfOrientation.Auto;

        /// <summary>缩放模式</summary>
        public PdfScaleMode ScaleMode { get; set; } = PdfScaleMode.FitPage;

        /// <summary>页面宽度（点，1点=1/72英寸）</summary>
        public double PageWidth { get; set; } = 842; // A4 横向

        /// <summary>页面高度（点）</summary>
        public double PageHeight { get; set; } = 595; // A4 横向

        /// <summary>页边距（点）</summary>
        public double Margin { get; set; } = 36; // 0.5 英寸

        /// <summary>DPI</summary>
        public int Dpi { get; set; } = 150;
    }
}
