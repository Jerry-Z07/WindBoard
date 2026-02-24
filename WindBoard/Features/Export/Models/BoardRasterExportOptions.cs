using System.Numerics;
using Windows.UI;

namespace WindBoard.Features.Export.Models
{
    /// <summary>
    /// 位图导出参数（PNG/PDF 位图渲染共用）。
    /// </summary>
    internal sealed record BoardRasterExportOptions(
        int Dpi,
        float PaddingDip,
        Color BackgroundColor,
        Vector2 FallbackViewportSizeDip,
        int MaxEdgePixels = 16384,
        BoardRasterFixedFrame? FixedFrame = null);
}
