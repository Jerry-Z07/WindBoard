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
    internal sealed partial class DxSwapChainPanelRenderer(SwapChainPanel panel) : IDisposable
    {
        private const int DirtyRectExtraPixels = 2;
        private const float MinRenderScale = 0.25f;
        private const float MaxRenderScale = 1.0f;
        private const float InteractiveRenderScale = 0.95f;

        private readonly SwapChainPanel _panel = panel;

        private ID3D11Device? _d3dDevice;
        private ID3D11DeviceContext? _d3dContext;
        private IDXGISwapChain1? _swapChain;

        private ID2D1Factory1? _d2dFactory;
        private ID2D1Device? _d2dDevice;
        private ID2D1DeviceContext? _d2dContext;
        private ID2D1Bitmap1? _d2dTargetBitmap;
        private ID2D1SolidColorBrush? _clearBrush;
        private Color4 _clearBrushColor;

        private ID2D1Bitmap1? _cachedBackgroundBitmap;
        private int _cachedBackgroundPixelWidth;
        private int _cachedBackgroundPixelHeight;
        private float _cachedBackgroundDpiX;
        private float _cachedBackgroundDpiY;
        private bool _cachedBackgroundDirty = true;

        private Color4 _clearColor = new(1.0f, 1.0f, 1.0f, 1.0f);
        private int _pixelWidth;
        private int _pixelHeight;
        private float _dpiX;
        private float _dpiY;
        private float _renderScale = 1.0f;
        private bool _hasValidPresentHistory;

        public bool IsInitialized { get; private set; }

        public float RenderScale => _renderScale;

        /// <summary>
        /// 画布清屏色（同时用于背景缓存与滚动脏区填充）。
        /// </summary>
        public Color4 ClearColor
        {
            get => _clearColor;
            set
            {
                if (AreClose(_clearColor, value))
                {
                    return;
                }

                _clearColor = value;
                _cachedBackgroundDirty = true;
                _hasValidPresentHistory = false;
            }
        }

        public void Initialize()
        {
            if (IsInitialized)
            {
                return;
            }

            CreateDeviceResources();
            CreateOrResizeSwapChainAndTargets();
            IsInitialized = true;
        }

        public void SetInteractiveMode(bool isInteractive)
        {
            SetRenderScale(isInteractive ? InteractiveRenderScale : 1.0f);
        }

        public void SetRenderScale(float scale)
        {
            float clamped = Math.Clamp(scale, MinRenderScale, MaxRenderScale);
            if (Math.Abs(clamped - _renderScale) < 0.0001f)
            {
                return;
            }

            _renderScale = clamped;

            if (IsInitialized)
            {
                CreateOrResizeSwapChainAndTargets();
            }
        }

        public void Resize()
        {
            if (!IsInitialized)
            {
                return;
            }

            CreateOrResizeSwapChainAndTargets();
        }

        public void Render(Action<ID2D1DeviceContext> draw)
        {
            if (!IsInitialized)
            {
                return;
            }

            CreateOrResizeSwapChainAndTargets();

            if (_d2dContext is null || _swapChain is null || _d2dTargetBitmap is null)
            {
                return;
            }

            var ctx = _d2dContext;
            ctx.Target = _d2dTargetBitmap;
            ctx.SetDpi(_dpiX, _dpiY);

            ctx.BeginDraw();
            ctx.Transform = Matrix3x2.Identity;
            ctx.Clear(_clearColor);
            draw(ctx);
            ctx.EndDraw(out _, out _);

            _swapChain.Present(1, PresentFlags.None);
            _hasValidPresentHistory = true;
        }

        public void InvalidateCachedBackground()
        {
            _cachedBackgroundDirty = true;
        }

        public void ReleaseCachedBackground()
        {
            _cachedBackgroundBitmap?.Dispose();
            _cachedBackgroundBitmap = null;
            _cachedBackgroundDirty = true;
        }

        public void RenderWithCachedBackgroundDirtyRect(Rect dirtyRectDip, Action<ID2D1DeviceContext> drawBackground, Action<ID2D1DeviceContext> drawOverlay)
        {
            if (!IsInitialized)
            {
                return;
            }

            if (!_hasValidPresentHistory)
            {
                RenderWithCachedBackground(drawBackground, drawOverlay);
                return;
            }

            CreateOrResizeSwapChainAndTargets();

            if (_d2dContext is null || _swapChain is null || _d2dTargetBitmap is null)
            {
                return;
            }

            var ctx = _d2dContext;

            try
            {
                EnsureCachedBackgroundBitmap(ctx);

                if (_cachedBackgroundBitmap is null)
                {
                    Render(ctx2 =>
                    {
                        drawBackground(ctx2);
                        drawOverlay(ctx2);
                    });
                    return;
                }

                if (_cachedBackgroundDirty)
                {
                    RenderCachedBackground(ctx, drawBackground);
                    _cachedBackgroundDirty = false;
                }

                RectI dirtyRectPixels = DipRectToPixelRect(dirtyRectDip, DirtyRectExtraPixels);
                if (dirtyRectPixels.Width <= 0 || dirtyRectPixels.Height <= 0)
                {
                    return;
                }

                Rect clipDip = PixelRectToDipRect(dirtyRectPixels);

                ctx.Target = _d2dTargetBitmap;
                ctx.SetDpi(_dpiX, _dpiY);

                ctx.BeginDraw();
                ctx.Transform = Matrix3x2.Identity;

                ctx.PushAxisAlignedClip(clipDip, AntialiasMode.Aliased);
                ctx.DrawBitmap(_cachedBackgroundBitmap, 1.0f, BitmapInterpolationMode.Linear);
                drawOverlay(ctx);
                ctx.PopAxisAlignedClip();

                ctx.EndDraw(out _, out _);

                var present = new PresentParameters
                {
                    DirtyRectangles = new RawRect[] { dirtyRectPixels },
                    ScrollRectangle = null,
                    ScrollOffset = null,
                };

                _swapChain.Present1(1, PresentFlags.None, present);
                _hasValidPresentHistory = true;
            }
            catch
            {
                RenderWithCachedBackground(drawBackground, drawOverlay);
            }
        }

        public bool TryRenderWithScroll(Vector2 scrollOffsetDip, Action<ID2D1DeviceContext, Rect> drawDirtyRegion)
        {
            if (!IsInitialized)
            {
                return false;
            }

            if (!_hasValidPresentHistory)
            {
                return false;
            }

            CreateOrResizeSwapChainAndTargets();

            if (_d2dContext is null || _swapChain is null || _d2dTargetBitmap is null)
            {
                return false;
            }

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

            int width = _pixelWidth;
            int height = _pixelHeight;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

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

            var scrollRectPixels = new RectI(scrollLeft, scrollTop, scrollRight - scrollLeft, scrollBottom - scrollTop);
            var dirtyRectsPixels = DxDirtyRectCalculator.CreatePanDirtyRectsPixels(width, height, dxPixels, dyPixels);
            if (dirtyRectsPixels.Length == 0)
            {
                return false;
            }

            var ctx = _d2dContext;
            EnsureClearBrush(ctx);

            try
            {
                ctx.Target = _d2dTargetBitmap;
                ctx.SetDpi(_dpiX, _dpiY);

                ctx.BeginDraw();
                ctx.Transform = Matrix3x2.Identity;

                foreach (RectI dirtyRectPixels in dirtyRectsPixels)
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

                var dirtyRawRects = new RawRect[dirtyRectsPixels.Length];
                for (int i = 0; i < dirtyRectsPixels.Length; i++)
                {
                    dirtyRawRects[i] = dirtyRectsPixels[i];
                }

                var present = new PresentParameters
                {
                    DirtyRectangles = dirtyRawRects,
                    ScrollRectangle = (RawRect)scrollRectPixels,
                    ScrollOffset = new Int2(dxPixels, dyPixels),
                };

                _swapChain.Present1(1, PresentFlags.None, present);
                _hasValidPresentHistory = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void RenderWithCachedBackground(Action<ID2D1DeviceContext> drawBackground, Action<ID2D1DeviceContext> drawOverlay)
        {
            if (!IsInitialized)
            {
                return;
            }

            CreateOrResizeSwapChainAndTargets();

            if (_d2dContext is null || _swapChain is null || _d2dTargetBitmap is null)
            {
                return;
            }

            var ctx = _d2dContext;

            try
            {
                EnsureCachedBackgroundBitmap(ctx);

                if (_cachedBackgroundBitmap is null)
                {
                    Render(ctx2 =>
                    {
                        drawBackground(ctx2);
                        drawOverlay(ctx2);
                    });
                    return;
                }

                if (_cachedBackgroundDirty)
                {
                    RenderCachedBackground(ctx, drawBackground);
                    _cachedBackgroundDirty = false;
                }

                ctx.Target = _d2dTargetBitmap;
                ctx.SetDpi(_dpiX, _dpiY);

                ctx.BeginDraw();
                ctx.Transform = Matrix3x2.Identity;

                // 背景缓存是全屏不透明（白底），这里无需 Clear，减少一次全屏填充。
                ctx.DrawBitmap(_cachedBackgroundBitmap, 1.0f, BitmapInterpolationMode.Linear);
                drawOverlay(ctx);

                ctx.EndDraw(out _, out _);
                _swapChain.Present(1, PresentFlags.None);
                _hasValidPresentHistory = true;
            }
            catch
            {
                // 缓存路径失败时降级为全量渲染，确保功能可用。
                Render(ctx2 =>
                {
                    drawBackground(ctx2);
                    drawOverlay(ctx2);
                });
            }
        }

        private void CreateDeviceResources()
        {
            DeviceCreationFlags baseFlags = DeviceCreationFlags.BgraSupport;
            DeviceCreationFlags debugFlags = baseFlags;

#if DEBUG
            if (D3D11.SdkLayersAvailable())
            {
                debugFlags |= DeviceCreationFlags.Debug;
            }
#endif

            var featureLevels11_1 = new Vortice.Direct3D.FeatureLevel[]
            {
                Vortice.Direct3D.FeatureLevel.Level_11_1,
                Vortice.Direct3D.FeatureLevel.Level_11_0,
                Vortice.Direct3D.FeatureLevel.Level_10_1,
                Vortice.Direct3D.FeatureLevel.Level_10_0,
            };

            var featureLevels11_0 = new Vortice.Direct3D.FeatureLevel[]
            {
                Vortice.Direct3D.FeatureLevel.Level_11_0,
                Vortice.Direct3D.FeatureLevel.Level_10_1,
                Vortice.Direct3D.FeatureLevel.Level_10_0,
            };

            if (!TryCreateD3DDevice(Vortice.Direct3D.DriverType.Hardware, debugFlags, featureLevels11_1, out _d3dDevice, out _d3dContext)
                && !TryCreateD3DDevice(Vortice.Direct3D.DriverType.Hardware, baseFlags, featureLevels11_1, out _d3dDevice, out _d3dContext)
                && !TryCreateD3DDevice(Vortice.Direct3D.DriverType.Hardware, baseFlags, featureLevels11_0, out _d3dDevice, out _d3dContext)
                && !TryCreateD3DDevice(Vortice.Direct3D.DriverType.Warp, baseFlags, featureLevels11_0, out _d3dDevice, out _d3dContext))
            {
                throw new InvalidOperationException("创建 D3D11 设备失败。");
            }

            _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded, DebugLevel.None);

            using var dxgiDevice = _d3dDevice!.QueryInterface<IDXGIDevice>();
            _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
            _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
        }

        private static bool TryCreateD3DDevice(
            Vortice.Direct3D.DriverType driverType,
            DeviceCreationFlags flags,
            Vortice.Direct3D.FeatureLevel[] featureLevels,
            out ID3D11Device? device,
            out ID3D11DeviceContext? context)
        {
            device = null;
            context = null;

            var result = D3D11.D3D11CreateDevice(
                IntPtr.Zero,
                driverType,
                flags,
                featureLevels,
                out device,
                out Vortice.Direct3D.FeatureLevel createdFeatureLevel,
                out context);

            if (result.Failure || device is null || context is null)
            {
                context?.Dispose();
                device?.Dispose();
                context = null;
                device = null;
                return false;
            }

            return true;
        }

        private void CreateOrResizeSwapChainAndTargets()
        {
            if (_d3dDevice is null || _d2dContext is null)
            {
                return;
            }

            double compositionScaleX = _panel.CompositionScaleX;
            double compositionScaleY = _panel.CompositionScaleY;
            if (compositionScaleX <= 0.0)
            {
                compositionScaleX = 1.0;
            }

            if (compositionScaleY <= 0.0)
            {
                compositionScaleY = 1.0;
            }

            double effectiveScaleX = compositionScaleX * _renderScale;
            double effectiveScaleY = compositionScaleY * _renderScale;

            float newDpiX = (float)(96.0 * effectiveScaleX);
            float newDpiY = (float)(96.0 * effectiveScaleY);

            int newPixelWidth = Math.Max(1, (int)Math.Round(_panel.ActualWidth * effectiveScaleX));
            int newPixelHeight = Math.Max(1, (int)Math.Round(_panel.ActualHeight * effectiveScaleY));

            bool sizeChanged = newPixelWidth != _pixelWidth || newPixelHeight != _pixelHeight;
            bool dpiChanged = Math.Abs(newDpiX - _dpiX) > 0.01f || Math.Abs(newDpiY - _dpiY) > 0.01f;

            _dpiX = newDpiX;
            _dpiY = newDpiY;

            bool needRecreateTarget = false;

            if (_swapChain is null)
            {
                using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);

                var desc = new SwapChainDescription1
                {
                    Width = (uint)newPixelWidth,
                    Height = (uint)newPixelHeight,
                    Format = Format.B8G8R8A8_UNorm,
                    Stereo = false,
                    SampleDescription = new SampleDescription(1, 0),
                    BufferUsage = Usage.RenderTargetOutput,
                    BufferCount = 2,
                    Scaling = Scaling.Stretch,
                    SwapEffect = SwapEffect.FlipSequential,
                    AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied,
                    Flags = SwapChainFlags.None,
                };

                _swapChain = factory.CreateSwapChainForComposition(_d3dDevice, desc, null);
                SetSwapChainOnPanel(_swapChain);
                ApplySwapChainPanelTransform(_swapChain, effectiveScaleX, effectiveScaleY, newPixelWidth, newPixelHeight);
                needRecreateTarget = true;
                ReleaseCachedBackground();
                _hasValidPresentHistory = false;
            }
            else if (sizeChanged || dpiChanged)
            {
                needRecreateTarget = true;

                _d2dContext.Target = null;
                _d2dTargetBitmap?.Dispose();
                _d2dTargetBitmap = null;

                if (sizeChanged)
                {
                    _d3dContext?.Flush();

                    _swapChain.ResizeBuffers(
                        2,
                        (uint)newPixelWidth,
                        (uint)newPixelHeight,
                        Format.B8G8R8A8_UNorm,
                        SwapChainFlags.None);
                }

                ApplySwapChainPanelTransform(_swapChain, effectiveScaleX, effectiveScaleY, newPixelWidth, newPixelHeight);
                ReleaseCachedBackground();
                _hasValidPresentHistory = false;
            }

            _pixelWidth = newPixelWidth;
            _pixelHeight = newPixelHeight;

            if (needRecreateTarget || _d2dTargetBitmap is null)
            {
                using var backBuffer = _swapChain.GetBuffer<IDXGISurface>(0);
                var bitmapProperties = new BitmapProperties1(
                    new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                    _dpiX,
                    _dpiY,
                    BitmapOptions.Target | BitmapOptions.CannotDraw,
                    null);

                _d2dTargetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(backBuffer, bitmapProperties);
                _d2dContext.Target = _d2dTargetBitmap;
                _d2dContext.SetDpi(_dpiX, _dpiY);
            }

        }

        private void EnsureCachedBackgroundBitmap(ID2D1DeviceContext ctx)
        {
            bool needRecreate = _cachedBackgroundBitmap is null
                || _cachedBackgroundPixelWidth != _pixelWidth
                || _cachedBackgroundPixelHeight != _pixelHeight
                || Math.Abs(_cachedBackgroundDpiX - _dpiX) > 0.01f
                || Math.Abs(_cachedBackgroundDpiY - _dpiY) > 0.01f;

            if (!needRecreate)
            {
                return;
            }

            ReleaseCachedBackground();

            var bitmapProperties = new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                _dpiX,
                _dpiY,
                BitmapOptions.Target,
                null);

            _cachedBackgroundBitmap = ctx.CreateBitmap(new SizeI(_pixelWidth, _pixelHeight), IntPtr.Zero, 0, bitmapProperties);
            _cachedBackgroundPixelWidth = _pixelWidth;
            _cachedBackgroundPixelHeight = _pixelHeight;
            _cachedBackgroundDpiX = _dpiX;
            _cachedBackgroundDpiY = _dpiY;
            _cachedBackgroundDirty = true;
        }

        private void RenderCachedBackground(ID2D1DeviceContext ctx, Action<ID2D1DeviceContext> drawBackground)
        {
            if (_cachedBackgroundBitmap is null)
            {
                return;
            }

            ctx.Target = _cachedBackgroundBitmap;
            ctx.SetDpi(_dpiX, _dpiY);

            ctx.BeginDraw();
            ctx.Transform = Matrix3x2.Identity;
            ctx.Clear(_clearColor);
            drawBackground(ctx);
            ctx.EndDraw(out _, out _);
        }

        private float GetPixelsPerDipX() => _dpiX / 96.0f;

        private float GetPixelsPerDipY() => _dpiY / 96.0f;

        private RectI DipRectToPixelRect(Rect rectDip, int extraPixels)
        {
            float pixelsPerDipX = GetPixelsPerDipX();
            float pixelsPerDipY = GetPixelsPerDipY();

            int left = (int)Math.Floor(rectDip.Left * pixelsPerDipX) - extraPixels;
            int top = (int)Math.Floor(rectDip.Top * pixelsPerDipY) - extraPixels;
            int right = (int)Math.Ceiling(rectDip.Right * pixelsPerDipX) + extraPixels;
            int bottom = (int)Math.Ceiling(rectDip.Bottom * pixelsPerDipY) + extraPixels;

            left = Math.Clamp(left, 0, _pixelWidth);
            top = Math.Clamp(top, 0, _pixelHeight);
            right = Math.Clamp(right, 0, _pixelWidth);
            bottom = Math.Clamp(bottom, 0, _pixelHeight);

            return new RectI(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }

        private Rect PixelRectToDipRect(RectI rectPixels)
        {
            float pixelsPerDipX = GetPixelsPerDipX();
            float pixelsPerDipY = GetPixelsPerDipY();

            if (pixelsPerDipX <= 0.0001f || pixelsPerDipY <= 0.0001f)
            {
                return Rect.FromLTRB(0, 0, 0, 0);
            }

            float left = rectPixels.Left / pixelsPerDipX;
            float top = rectPixels.Top / pixelsPerDipY;
            float right = rectPixels.Right / pixelsPerDipX;
            float bottom = rectPixels.Bottom / pixelsPerDipY;
            return Rect.FromLTRB(left, top, right, bottom);
        }

        private void EnsureClearBrush(ID2D1DeviceContext ctx)
        {
            if (_clearBrush is not null && AreClose(_clearBrushColor, _clearColor))
            {
                return;
            }

            _clearBrush?.Dispose();
            _clearBrush = ctx.CreateSolidColorBrush(_clearColor);
            _clearBrushColor = _clearColor;
        }

        private static bool AreClose(Color4 a, Color4 b)
        {
            return Math.Abs(a.R - b.R) < 0.0001f
                && Math.Abs(a.G - b.G) < 0.0001f
                && Math.Abs(a.B - b.B) < 0.0001f
                && Math.Abs(a.A - b.A) < 0.0001f;
        }

        private static void ApplySwapChainPanelTransform(
            IDXGISwapChain1 swapChain,
            double scaleX,
            double scaleY,
            int pixelWidth,
            int pixelHeight)
        {
            try
            {
                using var swapChain2 = swapChain.QueryInterface<IDXGISwapChain2>();
                swapChain2.SetSourceSize((uint)pixelWidth, (uint)pixelHeight);

                var inverseScale = new Matrix3x2(
                    (float)(1.0 / scaleX),
                    0.0f,
                    0.0f,
                    (float)(1.0 / scaleY),
                    0.0f,
                    0.0f);
                swapChain2.MatrixTransform = inverseScale;
            }
            catch
            {
                // 如果系统/驱动不支持 SwapChain2，这里忽略即可（至少不会因为缩放接口崩溃）
            }
        }

        private static void SetSwapChainOnPanel(SwapChainPanel panel, IDXGISwapChain1 swapChain)
        {
            var native = panel.As<ISwapChainPanelNative>();
            native.SetSwapChain(swapChain.NativePointer);
        }

        private void SetSwapChainOnPanel(IDXGISwapChain1 swapChain)
        {
            SetSwapChainOnPanel(_panel, swapChain);
        }

        public void Dispose()
        {
            ReleaseCachedBackground();

            _clearBrush?.Dispose();
            _clearBrush = null;

            _d2dTargetBitmap?.Dispose();
            _d2dTargetBitmap = null;

            _d2dContext?.Dispose();
            _d2dContext = null;

            _d2dDevice?.Dispose();
            _d2dDevice = null;

            _d2dFactory?.Dispose();
            _d2dFactory = null;

            _swapChain?.Dispose();
            _swapChain = null;

            _d3dContext?.Dispose();
            _d3dContext = null;

            _d3dDevice?.Dispose();
            _d3dDevice = null;
        }

        [ComImport]
        [Guid("63AAD0B8-7C24-40FF-85A8-640D944CC325")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISwapChainPanelNative
        {
            void SetSwapChain(IntPtr swapChain);
        }
    }
}
