using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using Vortice.WIC;
using WindBoard.Board;
using WindBoard.Board.Persistence;
using WindBoard.Board.Viewport;
using WindBoard.Features.Export.Models;
using WindBoard.Localization;
using WindBoard.Rendering.Board;
using UiColor = Windows.UI.Color;

namespace WindBoard.Features.Export.Services
{
    /// <summary>
    /// 白板离屏光栅化导出器（用于 PNG 与 PDF 位图渲染）。
    /// </summary>
    internal sealed class BoardRasterExporter : IDisposable
    {
        private ID2D1Factory1? _d2dFactory;
        private IWICImagingFactory? _wicFactory;

        private readonly BoardSceneRenderer _sceneRenderer = new();

        public BoardRasterExporter()
        {
            CreateDeviceResources();
        }

        public void ExportPng(BoardPageSnapshot page, string filePath, BoardRasterExportOptions options, CancellationToken cancellationToken = default)
        {
            if (page is null)
            {
                throw new ArgumentNullException(nameof(page));
            }

            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException(L10n.Get("Export_PathEmpty_Message"), nameof(filePath));
            }

            cancellationToken.ThrowIfCancellationRequested();

            using RenderedWicBitmap rendered = RenderToWicBitmap(page, options, cancellationToken);
            SaveWicBitmapToFile(rendered.Bitmap, rendered.PixelWidth, rendered.PixelHeight, rendered.Dpi, filePath, ContainerFormatGuids.Png);
        }

        public RasterizedRgbPage RenderRgbPage(BoardPageSnapshot page, BoardRasterExportOptions options, CancellationToken cancellationToken = default)
        {
            if (page is null)
            {
                throw new ArgumentNullException(nameof(page));
            }

            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            cancellationToken.ThrowIfCancellationRequested();

            using RenderedWicBitmap rendered = RenderToWicBitmap(page, options, cancellationToken);
            byte[] rgb = ExtractRgbBytes(rendered.Bitmap, rendered.PixelWidth, rendered.PixelHeight, cancellationToken);

            return new RasterizedRgbPage(
                PixelWidth: rendered.PixelWidth,
                PixelHeight: rendered.PixelHeight,
                Dpi: rendered.Dpi,
                WidthDip: rendered.WidthDip,
                HeightDip: rendered.HeightDip,
                RgbBytes: rgb);
        }

        private void CreateDeviceResources()
        {
            _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded, DebugLevel.None);

            // WIC 工厂：用于创建内存位图与编码 PNG。
            _wicFactory = new IWICImagingFactory();
        }

        private RenderedWicBitmap RenderToWicBitmap(BoardPageSnapshot page, BoardRasterExportOptions options, CancellationToken cancellationToken)
        {
            if (_d2dFactory is null || _wicFactory is null)
            {
                throw new InvalidOperationException(L10n.Get("Export_Raster_OffscreenNotInitialized_Message"));
            }

            // 1) 将快照还原为可渲染的文档对象（仅包含笔迹）
            BoardDocument document = CreateDocument(page);

            // 2) 计算内容包围盒（世界坐标≈DIP）
            bool hasBounds = TryGetDocumentBounds(document, out float minX, out float minY, out float maxX, out float maxY);

            float paddingDip = Math.Max(0.0f, options.PaddingDip);
            float widthDip;
            float heightDip;
            Vector2 cameraWorld;
            float zoom = 1.0f;
            float dpi;
            int pixelWidth;
            int pixelHeight;

            if (options.FixedFrame is BoardRasterFixedFrame fixedFrame)
            {
                // 固定画面：输出尺寸由预设决定，内容按比例缩放后居中放置。
                int maxEdge = options.MaxEdgePixels <= 0 ? 16384 : options.MaxEdgePixels;

                pixelWidth = Math.Clamp(fixedFrame.PixelWidth, 1, maxEdge);
                pixelHeight = Math.Clamp(fixedFrame.PixelHeight, 1, maxEdge);

                // 固定分辨率导出以像素为主，这里使用 96 DPI，保证 1 DIP = 1 px，计算更直观。
                dpi = 96.0f;
                widthDip = pixelWidth;
                heightDip = pixelHeight;

                if (hasBounds)
                {
                    float contentW = Math.Max(0.001f, maxX - minX);
                    float contentH = Math.Max(0.001f, maxY - minY);

                    float availableW = Math.Max(1.0f, widthDip - paddingDip * 2.0f);
                    float availableH = Math.Max(1.0f, heightDip - paddingDip * 2.0f);

                    zoom = Math.Min(availableW / contentW, availableH / contentH);
                    zoom = Math.Max(0.0001f, zoom);

                    cameraWorld = new Vector2((minX + maxX) / 2.0f, (minY + maxY) / 2.0f);
                }
                else
                {
                    cameraWorld = Vector2.Zero;
                }
            }
            else
            {
                // 自适应画面：根据内容包围盒裁切，输出尺寸随内容变化。
                if (hasBounds)
                {
                    float contentW = Math.Max(1.0f, maxX - minX);
                    float contentH = Math.Max(1.0f, maxY - minY);
                    widthDip = contentW + paddingDip * 2.0f;
                    heightDip = contentH + paddingDip * 2.0f;
                    cameraWorld = new Vector2((minX + maxX) / 2.0f, (minY + maxY) / 2.0f);
                }
                else
                {
                    // 空页面：使用 UI 提供的“当前视口”作为导出尺寸，避免导出 1×1 的空白图。
                    widthDip = Math.Max(1.0f, options.FallbackViewportSizeDip.X);
                    heightDip = Math.Max(1.0f, options.FallbackViewportSizeDip.Y);
                    cameraWorld = Vector2.Zero;
                }

                int requestedDpi = options.Dpi <= 0 ? 96 : options.Dpi;
                dpi = ConstrainDpi(requestedDpi, widthDip, heightDip, options.MaxEdgePixels);

                pixelWidth = Math.Max(1, (int)Math.Ceiling(widthDip * dpi / 96.0f));
                pixelHeight = Math.Max(1, (int)Math.Ceiling(heightDip * dpi / 96.0f));
            }

            // 3) 创建 WIC 位图（32bppPBGRA），并创建“WIC 位图渲染目标”在其上绘制。
            // 
            // 注意：
            // - 不使用 CreateBitmapFromWicBitmap 的原因：该 API 更偏向“从 WIC 载入位图作为资源”，
            //   在某些驱动/实现下可能不会把绘制结果回写到原始 IWICBitmap，从而导致导出全白/全透明。
            // - CreateWicBitmapRenderTarget 是 Direct2D 官方提供的“写回 WIC 位图”的通道，更稳定。
            IWICBitmap wicBitmap = _wicFactory.CreateBitmap((uint)pixelWidth, (uint)pixelHeight, Vortice.WIC.PixelFormat.Format32bppPBGRA, BitmapCreateCacheOption.CacheOnLoad);
            try
            {
                wicBitmap.SetResolution(dpi, dpi);

                var d2dPixelFormat = new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);
                var rtProps = new RenderTargetProperties(RenderTargetType.Default, d2dPixelFormat, dpi, dpi, RenderTargetUsage.None, FeatureLevel.Default);
                using ID2D1RenderTarget renderTarget = _d2dFactory.CreateWicBitmapRenderTarget(wicBitmap, rtProps);

                // 4) 配置视口：世界坐标（DIP）→ 屏幕坐标（DIP），再由 D2D DPI 映射到像素
                var viewport = new BoardViewport();
                viewport.UpdateViewportSize(new Vector2(widthDip, heightDip));
                viewport.SetViewForExport(cameraWorld, zoom);

                // 5) 离屏绘制
                Color4 bg = ToColor4(options.BackgroundColor);

                renderTarget.SetDpi(dpi, dpi);

                renderTarget.BeginDraw();
                renderTarget.Transform = Matrix3x2.Identity;
                renderTarget.Clear(bg);

                _sceneRenderer.DrawBackground(renderTarget, document, viewport);

                renderTarget.EndDraw(out _, out _);

                return new RenderedWicBitmap(wicBitmap, pixelWidth, pixelHeight, dpi, widthDip, heightDip);
            }
            catch
            {
                wicBitmap.Dispose();
                throw;
            }
        }

        private static float ConstrainDpi(int requestedDpi, float widthDip, float heightDip, int maxEdgePixels)
        {
            // 限制最大边像素，避免用户在极大画布/极高 DPI 下导出导致 OOM。
            int maxEdge = maxEdgePixels <= 0 ? 16384 : maxEdgePixels;

            float limitByW = maxEdge * 96.0f / Math.Max(1.0f, widthDip);
            float limitByH = maxEdge * 96.0f / Math.Max(1.0f, heightDip);
            float dpi = Math.Min(requestedDpi, Math.Min(limitByW, limitByH));

            // 给一个非常保守的下限，避免出现 0 DPI。
            return Math.Max(12.0f, dpi);
        }

        private static BoardDocument CreateDocument(BoardPageSnapshot page)
        {
            var document = new BoardDocument();

            foreach (StrokeSnapshot strokeSnapshot in page.Strokes)
            {
                var stroke = new Stroke
                {
                    Color = new Vortice.Mathematics.Color4(
                        strokeSnapshot.ColorRgba.X,
                        strokeSnapshot.ColorRgba.Y,
                        strokeSnapshot.ColorRgba.Z,
                        strokeSnapshot.ColorRgba.W),
                    BaseSize = strokeSnapshot.BaseSize,
                    EnablePressure = strokeSnapshot.EnablePressure,
                };

                foreach (StrokePointSnapshot pointSnapshot in strokeSnapshot.Points)
                {
                    stroke.Points.Add(new StrokePoint(pointSnapshot.Position, pointSnapshot.Pressure));
                }

                // 离屏导出依赖 Bounds 做可见裁剪，这里统一重建一次，保证数据来自快照时也能稳定渲染。
                stroke.RecalculateBoundsFromPoints();

                document.Strokes.Add(stroke);
            }

            return document;
        }

        private static bool TryGetDocumentBounds(BoardDocument document, out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = float.PositiveInfinity;
            minY = float.PositiveInfinity;
            maxX = float.NegativeInfinity;
            maxY = float.NegativeInfinity;

            foreach (Stroke stroke in document.Strokes)
            {
                if (!stroke.HasBounds)
                {
                    continue;
                }

                minX = Math.Min(minX, stroke.BoundsMin.X);
                minY = Math.Min(minY, stroke.BoundsMin.Y);
                maxX = Math.Max(maxX, stroke.BoundsMax.X);
                maxY = Math.Max(maxY, stroke.BoundsMax.Y);
            }

            return minX <= maxX && minY <= maxY;
        }

        private void SaveWicBitmapToFile(IWICBitmap bitmap, int pixelWidth, int pixelHeight, float dpi, string filePath, Guid containerFormat)
        {
            if (_wicFactory is null)
            {
                throw new InvalidOperationException(L10n.Get("Export_Raster_WicFactoryNotInitialized_Message"));
            }

            using IWICStream stream = _wicFactory.CreateStream(filePath, System.IO.FileAccess.Write);
            using IWICBitmapEncoder encoder = _wicFactory.CreateEncoder(containerFormat);
            encoder.Initialize(stream, BitmapEncoderCacheOption.NoCache);

            using IWICBitmapFrameEncode frame = encoder.CreateNewFrame(out var encoderOptions);
            frame.Initialize(encoderOptions);
            frame.SetSize((uint)pixelWidth, (uint)pixelHeight);
            frame.SetResolution(dpi, dpi);

            Guid format = Vortice.WIC.PixelFormat.Format32bppPBGRA;
            frame.SetPixelFormat(ref format);

            frame.WriteSource(bitmap);
            frame.Commit();
            encoder.Commit();
        }

        private static byte[] ExtractRgbBytes(IWICBitmap bitmap, int pixelWidth, int pixelHeight, CancellationToken cancellationToken)
        {
            // 说明：
            // - 这里以 32bppPBGRA 为输入（Direct2D 常见输出）；
            // - PDF 里使用 DeviceRGB，因此导出为紧密排列的 RGB（无 alpha）。
            using IWICBitmapLock bmpLock = bitmap.Lock(new System.Drawing.Rectangle(0, 0, pixelWidth, pixelHeight), BitmapLockFlags.Read);

            DataRectangle data = bmpLock.Data;
            int stride = (int)bmpLock.Stride;

            int rowBytes = pixelWidth * 4;
            if (stride < rowBytes)
            {
                throw new InvalidOperationException(L10n.Get("Export_Raster_InvalidStride_Message"));
            }

            var rgb = new byte[pixelWidth * pixelHeight * 3];
            var bgraRow = new byte[rowBytes];

            for (int y = 0; y < pixelHeight; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IntPtr srcRow = data.DataPointer + y * stride;
                int dstRowIndex = y * pixelWidth * 3;

                // 逐像素转换：BGRA → RGB（忽略 alpha）。
                // 说明：为了避免逐像素 Marshal.ReadByte 的高开销，这里按行拷贝到托管缓冲区再转换。
                Marshal.Copy(srcRow, bgraRow, 0, rowBytes);
                for (int x = 0; x < pixelWidth; x++)
                {
                    int src = x * 4;
                    byte b = bgraRow[src + 0];
                    byte g = bgraRow[src + 1];
                    byte r = bgraRow[src + 2];

                    int dst = dstRowIndex + x * 3;
                    rgb[dst + 0] = r;
                    rgb[dst + 1] = g;
                    rgb[dst + 2] = b;
                }
            }

            return rgb;
        }

        private static Color4 ToColor4(UiColor color)
        {
            return new Color4(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, color.A / 255.0f);
        }

        public void Dispose()
        {
            _sceneRenderer.Dispose();

            _wicFactory?.Dispose();
            _wicFactory = null;

            _d2dFactory?.Dispose();
            _d2dFactory = null;
        }

        private sealed class RenderedWicBitmap : IDisposable
        {
            public RenderedWicBitmap(IWICBitmap bitmap, int pixelWidth, int pixelHeight, float dpi, float widthDip, float heightDip)
            {
                Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
                PixelWidth = pixelWidth;
                PixelHeight = pixelHeight;
                Dpi = dpi;
                WidthDip = widthDip;
                HeightDip = heightDip;
            }

            public IWICBitmap Bitmap { get; }

            public int PixelWidth { get; }

            public int PixelHeight { get; }

            public float Dpi { get; }

            public float WidthDip { get; }

            public float HeightDip { get; }

            public void Dispose()
            {
                Bitmap.Dispose();
            }
        }
    }

    internal sealed record RasterizedRgbPage(int PixelWidth, int PixelHeight, float Dpi, float WidthDip, float HeightDip, byte[] RgbBytes);
}
