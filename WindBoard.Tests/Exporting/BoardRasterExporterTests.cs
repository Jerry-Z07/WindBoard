using System;
using System.Numerics;
using WindBoard.Board.Persistence;
using WindBoard.Exporting;
using Windows.UI;
using Xunit;

namespace WindBoard.Tests.Exporting;

public sealed class BoardRasterExporterTests
{
    [Fact]
    public void RenderRgbPage_RendersStrokePixels()
    {
        // 该测试用于防止“导出全白/全透明”的回归。
        // 选择白底 + 黑色粗线：只要渲染成功，输出像素中必然存在非白色像素。
        var page = new BoardPageSnapshot(
            Id: Guid.NewGuid(),
            Strokes:
            [
                new StrokeSnapshot(
                    Points:
                    [
                        new StrokePointSnapshot(new Vector2(0.0f, 0.0f), 1.0f),
                        new StrokePointSnapshot(new Vector2(200.0f, 0.0f), 1.0f),
                    ],
                    ColorRgba: new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                    BaseSize: 12.0f,
                    EnablePressure: false),
            ]);

        var options = new BoardRasterExportOptions(
            Dpi: 96,
            PaddingDip: 24.0f,
            BackgroundColor: Color.FromArgb(255, 255, 255, 255),
            FallbackViewportSizeDip: new Vector2(800.0f, 600.0f),
            FixedFrame: new BoardRasterFixedFrame(1080, 1080));

        using var exporter = new BoardRasterExporter();
        RasterizedRgbPage rgbPage = exporter.RenderRgbPage(page, options);

        byte[] rgb = rgbPage.RgbBytes;
        bool hasNonWhite = false;
        for (int i = 0; i < rgb.Length; i += 3)
        {
            byte r = rgb[i + 0];
            byte g = rgb[i + 1];
            byte b = rgb[i + 2];

            // 抗锯齿边缘可能不是纯黑，但一定会明显偏离白色。
            if (r < 250 || g < 250 || b < 250)
            {
                hasNonWhite = true;
                break;
            }
        }

        Assert.True(hasNonWhite, "导出渲染结果不应全为背景色。");
    }

    [Fact]
    public void RenderRgbPage_FixedFrame_ScalesContentToFillFrame()
    {
        // 该测试用于防止“固定画面导出时缩放被限制”的回归。
        // 选择非常小的内容包围盒，如果缩放被错误地夹在交互上限（例如 32x），
        // 那么笔迹在 1080×1080 画布中会非常小，无法达到“压入标准分辨率画面”的预期效果。
        var page = new BoardPageSnapshot(
            Id: Guid.NewGuid(),
            Strokes:
            [
                new StrokeSnapshot(
                    Points:
                    [
                        new StrokePointSnapshot(new Vector2(0.0f, 0.0f), 1.0f),
                        new StrokePointSnapshot(new Vector2(10.0f, 0.0f), 1.0f),
                    ],
                    ColorRgba: new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                    BaseSize: 2.0f,
                    EnablePressure: false),
            ]);

        var options = new BoardRasterExportOptions(
            Dpi: 96,
            PaddingDip: 24.0f,
            BackgroundColor: Color.FromArgb(255, 255, 255, 255),
            FallbackViewportSizeDip: new Vector2(800.0f, 600.0f),
            FixedFrame: new BoardRasterFixedFrame(1080, 1080));

        using var exporter = new BoardRasterExporter();
        RasterizedRgbPage rgbPage = exporter.RenderRgbPage(page, options);

        int w = rgbPage.PixelWidth;
        int h = rgbPage.PixelHeight;
        byte[] rgb = rgbPage.RgbBytes;

        int minX = w;
        int maxX = -1;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 3;
                byte r = rgb[i + 0];
                byte g = rgb[i + 1];
                byte b = rgb[i + 2];

                if (r < 250 || g < 250 || b < 250)
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                }
            }
        }

        Assert.True(maxX >= 0, "导出渲染结果不应全为背景色。");

        int span = maxX - minX + 1;

        // 预期：笔迹应被显著放大并填充画面宽度（扣除留白后约 1032px）。
        // 这里取一个保守阈值，避免抗锯齿/笔迹边界计算差异导致的偶发失败。
        Assert.True(span >= 700, $"固定画面导出应缩放内容以填充画面（span={span}px）。");
    }
}
