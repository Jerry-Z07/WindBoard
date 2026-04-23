using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinRT;

namespace WindBoard.Rendering
{
    /// <summary>
    /// SwapChainPanel 渲染器：滚动（Present1/Scroll）相关代码。
    /// </summary>
    internal sealed partial class DxSwapChainPanelRenderer
    {
        public bool TryRenderWithScroll(Vector2 scrollOffsetDip, Action<ID2D1DeviceContext, Rect> drawDirtyRegion)
        {
            if (!TryPrepareScrollPresent(scrollOffsetDip, out ScrollPresentInfo presentInfo))
            {
                return false;
            }

            ID2D1DeviceContext ctx = presentInfo.Context;
            EnsureClearBrush(ctx);

            try
            {
                ctx.Target = presentInfo.TargetBitmap;
                ctx.SetDpi(_dpiX, _dpiY);

                ctx.BeginDraw();
                ctx.Transform = Matrix3x2.Identity;

                foreach (RectI dirtyRectPixels in presentInfo.DirtyRectsPixels)
                {
                    if (dirtyRectPixels.Width <= 0 || dirtyRectPixels.Height <= 0)
                    {
                        continue;
                    }

                    Rect dirtyDip = PixelRectToDipRect(dirtyRectPixels);

                    ctx.PushAxisAlignedClip(dirtyDip, AntialiasMode.Aliased);
                    if (_clearBrush is not null)
                    {
                        ctx.FillRectangle(dirtyDip, _clearBrush);
                    }
                    drawDirtyRegion(ctx, dirtyDip);
                    ctx.PopAxisAlignedClip();
                }

                ctx.EndDraw(out _, out _);

                var presentParameters = new PresentParameters
                {
                    DirtyRectangles = ToRawRects(presentInfo.DirtyRectsPixels),
                    ScrollRectangle = (RawRect)presentInfo.ScrollRectPixels,
                    ScrollOffset = presentInfo.ScrollOffsetPixels,
                };

                presentInfo.SwapChain.Present1(1, PresentFlags.None, presentParameters);
                _hasValidPresentHistory = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private readonly struct ScrollPresentInfo
        {
            public ID2D1DeviceContext Context { get; init; }
            public IDXGISwapChain1 SwapChain { get; init; }
            public ID2D1Bitmap1 TargetBitmap { get; init; }
            public RectI ScrollRectPixels { get; init; }
            public RectI[] DirtyRectsPixels { get; init; }
            public Int2 ScrollOffsetPixels { get; init; }
        }

        private bool TryPrepareScrollPresent(Vector2 scrollOffsetDip, out ScrollPresentInfo presentInfo)
        {
            presentInfo = default;
            if (!TryGetScrollPresentResources(out ID2D1DeviceContext ctx, out IDXGISwapChain1 swapChain, out ID2D1Bitmap1 targetBitmap))
            {
                return false;
            }

            if (!TryGetScrollOffsetPixels(scrollOffsetDip, out Int2 scrollOffsetPixels))
            {
                return false;
            }

            if (!TryGetScrollRectsPixels(scrollOffsetPixels, out RectI scrollRectPixels, out RectI[] dirtyRectsPixels))
            {
                return false;
            }

            presentInfo = new ScrollPresentInfo
            {
                Context = ctx,
                SwapChain = swapChain,
                TargetBitmap = targetBitmap,
                ScrollRectPixels = scrollRectPixels,
                DirtyRectsPixels = dirtyRectsPixels,
                ScrollOffsetPixels = scrollOffsetPixels,
            };
            return true;
        }

        private bool TryGetScrollPresentResources(out ID2D1DeviceContext ctx, out IDXGISwapChain1 swapChain, out ID2D1Bitmap1 targetBitmap)
        {
            ctx = null!;
            swapChain = null!;
            targetBitmap = null!;

            if (!IsInitialized || !_hasValidPresentHistory)
            {
                return false;
            }

            CreateOrResizeSwapChainAndTargets();

            if (_d2dContext is null || _swapChain is null || _d2dTargetBitmap is null)
            {
                return false;
            }

            ctx = _d2dContext;
            swapChain = _swapChain;
            targetBitmap = _d2dTargetBitmap;
            return true;
        }

        private bool TryGetScrollOffsetPixels(Vector2 scrollOffsetDip, out Int2 scrollOffsetPixels)
        {
            scrollOffsetPixels = default;

            float pixelsPerDipX = GetPixelsPerDipX();
            float pixelsPerDipY = GetPixelsPerDipY();
            if (pixelsPerDipX <= 0.0001f || pixelsPerDipY <= 0.0001f)
            {
                return false;
            }

            int dxPixels = (int)Math.Round(scrollOffsetDip.X * pixelsPerDipX);
            int dyPixels = (int)Math.Round(scrollOffsetDip.Y * pixelsPerDipY);
            if (dxPixels == 0 && dyPixels == 0)
            {
                return false;
            }

            scrollOffsetPixels = new Int2(dxPixels, dyPixels);
            return true;
        }

        private bool TryGetScrollRectsPixels(Int2 scrollOffsetPixels, out RectI scrollRectPixels, out RectI[] dirtyRectsPixels)
        {
            scrollRectPixels = default;
            dirtyRectsPixels = [];

            int width = _pixelWidth;
            int height = _pixelHeight;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            int dxPixels = scrollOffsetPixels.X;
            int dyPixels = scrollOffsetPixels.Y;

            if (Math.Abs(dxPixels) >= width || Math.Abs(dyPixels) >= height)
            {
                return false;
            }

            // DXGI_PRESENT_PARAMETERS.pScrollRect 描述的是“当前帧的目标区域”，也就是滚动后仍然可复用上一帧内容的区域。
            // pScrollOffset 是从上一帧源区域到当前帧目标区域的偏移（source + offset = dest）。
            int scrollLeft = Math.Max(0, dxPixels);
            int scrollTop = Math.Max(0, dyPixels);
            int scrollRight = width + Math.Min(0, dxPixels);
            int scrollBottom = height + Math.Min(0, dyPixels);
            if (scrollRight <= scrollLeft || scrollBottom <= scrollTop)
            {
                return false;
            }

            scrollRectPixels = new RectI(scrollLeft, scrollTop, scrollRight - scrollLeft, scrollBottom - scrollTop);
            dirtyRectsPixels = DxDirtyRectCalculator.CreatePanDirtyRectsPixels(width, height, dxPixels, dyPixels);
            return dirtyRectsPixels.Length > 0;
        }

        private static RawRect[] ToRawRects(RectI[] rects)
        {
            var result = new RawRect[rects.Length];
            for (int i = 0; i < rects.Length; i++)
            {
                result[i] = rects[i];
            }

            return result;
        }

    }
}
