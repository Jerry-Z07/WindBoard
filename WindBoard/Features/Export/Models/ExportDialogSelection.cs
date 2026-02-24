namespace WindBoard.Features.Export.Models
{
    /// <summary>
    /// 导出格式。
    /// </summary>
    internal enum ExportFormat
    {
        Png,
        Pdf,
        Wbix,
    }

    /// <summary>
    /// 导出页范围来源。
    /// </summary>
    internal enum ExportPageScope
    {
        Current,
        All,
        Range,
    }

    /// <summary>
    /// 导出对话框的用户选择结果。
    /// </summary>
    /// <param name="Format">导出格式。</param>
    /// <param name="PageScope">页范围来源。</param>
    /// <param name="PageRangeText">页范围文本（仅当 <see cref="ExportPageScope.Range"/> 时有效）。</param>
    /// <param name="Dpi">DPI（主要用于 PDF 位图渲染）。</param>
    /// <param name="PaddingDip">留白（DIP）。</param>
    /// <param name="PngFixedFrame">PNG 固定画面参数（仅当 <see cref="ExportFormat.Png"/> 时有效）。</param>
    internal sealed record ExportDialogSelection(
        ExportFormat Format,
        ExportPageScope PageScope,
        string PageRangeText,
        int Dpi,
        float PaddingDip,
        BoardRasterFixedFrame? PngFixedFrame);
}

