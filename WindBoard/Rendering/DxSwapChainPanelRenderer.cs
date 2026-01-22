using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinRT;

namespace WindBoard.Rendering
{
    internal sealed class DxSwapChainPanelRenderer : IDisposable
    {
        private readonly SwapChainPanel _panel;

        private ID3D11Device? _d3dDevice;
        private ID3D11DeviceContext? _d3dContext;
        private IDXGISwapChain1? _swapChain;

        private ID2D1Factory1? _d2dFactory;
        private ID2D1Device? _d2dDevice;
        private ID2D1DeviceContext? _d2dContext;
        private ID2D1Bitmap1? _d2dTargetBitmap;

        private ID2D1SolidColorBrush? _gridBrush;

        private int _pixelWidth;
        private int _pixelHeight;
        private float _dpi;

        public DxSwapChainPanelRenderer(SwapChainPanel panel)
        {
            _panel = panel;
        }

        public bool IsInitialized { get; private set; }

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
            if (!IsInitialized || _d2dContext is null || _swapChain is null)
            {
                return;
            }

            float widthDip = (float)Math.Max(1.0, _panel.ActualWidth);
            float heightDip = (float)Math.Max(1.0, _panel.ActualHeight);

            var ctx = _d2dContext;

            ctx.BeginDraw();
            ctx.Clear(new Color4(1.0f, 1.0f, 1.0f, 1.0f));
            DrawGrid(ctx, widthDip, heightDip);
            draw(ctx);
            ctx.EndDraw(out _, out _);

            _swapChain.Present(1, PresentFlags.None);
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

            Vortice.Direct3D.FeatureLevel createdFeatureLevel;
            var result = D3D11.D3D11CreateDevice(
                IntPtr.Zero,
                driverType,
                flags,
                featureLevels,
                out device,
                out createdFeatureLevel,
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

            double rasterizationScale = _panel.XamlRoot?.RasterizationScale ?? 1.0;
            _dpi = (float)(96.0 * rasterizationScale);

            int newPixelWidth = Math.Max(1, (int)Math.Round(_panel.ActualWidth * rasterizationScale));
            int newPixelHeight = Math.Max(1, (int)Math.Round(_panel.ActualHeight * rasterizationScale));

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
            }
            else if (newPixelWidth != _pixelWidth || newPixelHeight != _pixelHeight)
            {
                _d2dContext.Target = null;
                _d2dTargetBitmap?.Dispose();
                _d2dTargetBitmap = null;

                _swapChain.ResizeBuffers(
                    2,
                    (uint)newPixelWidth,
                    (uint)newPixelHeight,
                    Format.B8G8R8A8_UNorm,
                    SwapChainFlags.None);
            }

            _pixelWidth = newPixelWidth;
            _pixelHeight = newPixelHeight;

            using var backBuffer = _swapChain.GetBuffer<IDXGISurface>(0);
            var bitmapProperties = new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                _dpi,
                _dpi,
                BitmapOptions.Target | BitmapOptions.CannotDraw,
                null);

            _d2dTargetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(backBuffer, bitmapProperties);
            _d2dContext.Target = _d2dTargetBitmap;
            _d2dContext.SetDpi(_dpi, _dpi);

            _gridBrush ??= _d2dContext.CreateSolidColorBrush(new Color4(0.90f, 0.90f, 0.90f, 1.0f));
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

        private void DrawGrid(ID2D1DeviceContext ctx, float widthDip, float heightDip)
        {
            if (_gridBrush is null)
            {
                return;
            }

            const float step = 40.0f;

            for (float x = 0; x <= widthDip; x += step)
            {
                ctx.DrawLine(new Vector2(x, 0), new Vector2(x, heightDip), _gridBrush, 1.0f);
            }

            for (float y = 0; y <= heightDip; y += step)
            {
                ctx.DrawLine(new Vector2(0, y), new Vector2(widthDip, y), _gridBrush, 1.0f);
            }
        }

        public void Dispose()
        {
            _gridBrush?.Dispose();
            _gridBrush = null;

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
