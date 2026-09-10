using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;

namespace WindBoard.Tests.Rendering.Snapshot;

/// <summary>
/// 逐像素比对结果。
/// </summary>
internal sealed class SnapshotCompareResult
{
    private SnapshotCompareResult(int diffPixelCount, int totalPixels, byte[] diffBgra)
    {
        DiffPixelCount = diffPixelCount;
        TotalPixels = totalPixels;
        DiffBgra = diffBgra;
    }

    public static SnapshotCompareResult Identical(int totalPixels, byte[] diffBgra) =>
        new(0, totalPixels, diffBgra);

    public static SnapshotCompareResult Different(int diffPixelCount, int totalPixels, byte[] diffBgra) =>
        new(diffPixelCount, totalPixels, diffBgra);

    /// <summary>差异像素数（任一通道超出容差即计为差异）。</summary>
    public int DiffPixelCount { get; }

    /// <summary>总像素数。</summary>
    public int TotalPixels { get; }

    /// <summary>差异像素占比（0~1）。</summary>
    public double DiffPixelRatio => TotalPixels == 0 ? 0 : (double)DiffPixelCount / TotalPixels;

    /// <summary>差异可视化缓冲（差异像素标红、其余置灰），仅失败诊断用。</summary>
    public byte[] DiffBgra { get; }

    public bool IsMatch => DiffPixelCount == 0;
}

/// <summary>
/// 快照逐像素比对器：BGRA 缓冲按每通道容差比较，并生成差异高亮图与 PNG 导出。
/// </summary>
/// <remarks>
/// PNG 编解码使用 System.Drawing.Common（主工程既有依赖，仅 Windows 可用，测试环境恒为 Windows）。
/// 设计稿原定 WIC 编码，但 WIC 需引入 Vortice.WIC 新包，按“优先使用已有依赖”约定改用本方案。
/// </remarks>
internal static class SnapshotComparer
{
    /// <summary>每通道容差（0-255）：吸收抗锯齿/文本渲染的合法抖动，可按场景覆写。</summary>
    internal const int DefaultTolerance = 3;

    /// <summary>差异像素置红（BGRA）。</summary>
    private static readonly byte[] DiffHighlight = [0x00, 0x00, 0xFF, 0xFF];

    /// <summary>无差异像素置深灰（BGRA），便于目视定位差异区域。</summary>
    private static readonly byte[] DiffNeutral = [0x30, 0x30, 0x30, 0xFF];

    /// <summary>
    /// 逐像素比较两张 BGRA 图。
    /// </summary>
    /// <param name="expected">期望像素。</param>
    /// <param name="actual">实际像素。</param>
    /// <param name="width">图宽（像素）。</param>
    /// <param name="height">图高（像素）。</param>
    /// <param name="tolerance">每通道容差。</param>
    public static SnapshotCompareResult Compare(byte[] expected, byte[] actual, int width, int height, int tolerance)
    {
        int expectedLength = width * height * 4;
        if (expected.Length != expectedLength)
        {
            throw new ArgumentException(
                string.Format(CultureInfo.InvariantCulture, "expected 长度 {0} 与尺寸不符（应为 {1}）", expected.Length, expectedLength),
                nameof(expected));
        }

        if (actual.Length != expectedLength)
        {
            throw new ArgumentException(
                string.Format(CultureInfo.InvariantCulture, "actual 长度 {0} 与尺寸不符（应为 {1}）", actual.Length, expectedLength),
                nameof(actual));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);

        byte[] diff = new byte[expectedLength];
        int diffPixelCount = 0;
        int totalPixels = width * height;

        for (int pixel = 0; pixel < totalPixels; pixel++)
        {
            int offset = pixel * 4;
            bool differs =
                Math.Abs(expected[offset] - actual[offset]) > tolerance
                || Math.Abs(expected[offset + 1] - actual[offset + 1]) > tolerance
                || Math.Abs(expected[offset + 2] - actual[offset + 2]) > tolerance
                || Math.Abs(expected[offset + 3] - actual[offset + 3]) > tolerance;

            if (differs)
            {
                diffPixelCount++;
                DiffHighlight.CopyTo(diff, offset);
            }
            else
            {
                DiffNeutral.CopyTo(diff, offset);
            }
        }

        return diffPixelCount == 0
            ? SnapshotCompareResult.Identical(totalPixels, diff)
            : SnapshotCompareResult.Different(diffPixelCount, totalPixels, diff);
    }

    /// <summary>将 BGRA 缓冲保存为 PNG。</summary>
    public static void SavePng(string path, byte[] bgra, int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData? locked = null;
        try
        {
            // Format32bppArgb 的内存布局为 B,G,R,A（小端），与 BGRA 缓冲逐字节对应；
            // Format32bppArgb 行距恒等于 width*4（4 字节对齐天然满足），可整块拷贝。
            locked = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);
            System.Runtime.InteropServices.Marshal.Copy(bgra, 0, locked.Scan0, bgra.Length);
        }
        finally
        {
            if (locked is not null)
            {
                bitmap.UnlockBits(locked);
            }
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    /// <summary>加载 PNG 为 BGRA 缓冲。</summary>
    public static byte[] LoadPng(string path, int width, int height)
    {
        using var bitmap = new Bitmap(path);
        if (bitmap.Width != width || bitmap.Height != height)
        {
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "基准 PNG 尺寸不符：{0} 实际 {1}x{2}，期望 {3}x{4}",
                    path,
                    bitmap.Width,
                    bitmap.Height,
                    width,
                    height));
        }

        byte[] bgra = new byte[width * height * 4];
        BitmapData? locked = null;
        try
        {
            locked = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            System.Runtime.InteropServices.Marshal.Copy(locked.Scan0, bgra, 0, bgra.Length);
        }
        finally
        {
            if (locked is not null)
            {
                bitmap.UnlockBits(locked);
            }
        }

        return bgra;
    }
}
