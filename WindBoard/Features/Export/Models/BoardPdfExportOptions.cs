namespace WindBoard.Features.Export.Models
{
    /// <summary>
    /// PDF 导出参数。
    /// </summary>
    internal sealed record BoardPdfExportOptions(BoardRasterExportOptions RasterOptions);
}
