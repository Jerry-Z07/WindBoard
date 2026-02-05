namespace WindBoard.Exporting
{
    /// <summary>
    /// 固定画面导出参数（用于把内容压入标准比例/分辨率的画布中）。
    /// </summary>
    /// <param name="PixelWidth">输出宽度（像素）。</param>
    /// <param name="PixelHeight">输出高度（像素）。</param>
    internal sealed record BoardRasterFixedFrame(int PixelWidth, int PixelHeight);
}

