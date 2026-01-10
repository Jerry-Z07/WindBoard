using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D9;
using Vortice.DXGI;

namespace WindBoard.Services.InkV2.Rendering
{
    internal sealed class D3DImageRenderTarget : IDisposable
    {
        private readonly D3DImage _imageSource = new D3DImage();

        private ID3D11Device? _d3d11Device;
        private ID3D11DeviceContext? _d3d11Context;
        private DriverType _d3d11DriverType = DriverType.Unknown;

        private IDirect3D9Ex? _d3d9;
        private IDirect3DDevice9Ex? _d3d9Device;

        private ID3D11Texture2D? _d3d11Texture;
        private ID3D11RenderTargetView? _d3d11Rtv;

        private IDirect3DTexture9? _d3d9Texture;
        private IDirect3DSurface9? _d3d9Surface;

        private int _pixelWidth;
        private int _pixelHeight;
        private string? _lastFailureReason;
        private long _lastFailureLogTick;

        public D3DImage ImageSource => _imageSource;
        public bool IsFrontBufferAvailable => _imageSource.IsFrontBufferAvailable;
        public int PixelWidth => _pixelWidth;
        public int PixelHeight => _pixelHeight;
        internal string? LastFailureReason => _lastFailureReason;

        public ID3D11Device? D3D11Device => _d3d11Device;
        public ID3D11DeviceContext? D3D11Context => _d3d11Context;
        public ID3D11Texture2D? D3D11Texture => _d3d11Texture;
        public ID3D11RenderTargetView? D3D11RenderTargetView => _d3d11Rtv;
        public DriverType D3D11DriverType => _d3d11DriverType;

        public D3DImageRenderTarget()
        {
            _imageSource.IsFrontBufferAvailableChanged += ImageSource_IsFrontBufferAvailableChanged;
        }

        public bool TryBeginDraw(IntPtr hwnd, int pixelWidth, int pixelHeight)
        {
            if (!_imageSource.IsFrontBufferAvailable)
            {
                _lastFailureReason = "FrontBufferUnavailable";
                return false;
            }

            if (!TryEnsureResources(hwnd, pixelWidth, pixelHeight))
            {
                return false;
            }

            if (!_imageSource.IsFrontBufferAvailable)
            {
                _lastFailureReason = "FrontBufferUnavailableAfterEnsureResources";
                return false;
            }

            _imageSource.Lock();
            return true;
        }

        public void EndDraw()
        {
            if (_pixelWidth > 0 && _pixelHeight > 0)
            {
                _imageSource.AddDirtyRect(new Int32Rect(0, 0, _pixelWidth, _pixelHeight));
            }
            _imageSource.Unlock();
        }

        private void ImageSource_IsFrontBufferAvailableChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!_imageSource.IsFrontBufferAvailable)
            {
                DetachBackBuffer();
                return;
            }

            if (_d3d9Surface == null) return;
            AttachBackBuffer();
        }

        private bool TryEnsureResources(IntPtr hwnd, int pixelWidth, int pixelHeight)
        {
            if (hwnd == IntPtr.Zero)
            {
                _lastFailureReason = "InvalidHwnd";
                return false;
            }
            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                DetachBackBuffer();
                DisposeBackBuffer();
                _pixelWidth = 0;
                _pixelHeight = 0;
                _lastFailureReason = "InvalidSize";
                return false;
            }

            if (!TryEnsureDevices(hwnd))
            {
                return false;
            }

            if (IsDeviceRemoved())
            {
                DisposeDevices();
                if (!TryEnsureDevices(hwnd))
                {
                    return false;
                }
            }

            if (_d3d11Texture != null && _pixelWidth == pixelWidth && _pixelHeight == pixelHeight)
            {
                return true;
            }

            _pixelWidth = pixelWidth;
            _pixelHeight = pixelHeight;
            CreateBackBuffer(pixelWidth, pixelHeight);
            return _d3d11Texture != null && _d3d11Rtv != null;
        }

        private bool TryEnsureDevices(IntPtr hwnd)
        {
            if (_d3d11Device != null && _d3d11Context != null && _d3d9Device != null)
            {
                return true;
            }

            DisposeDevices();

            try
            {
                if (!TryCreateD3D11Device(DriverType.Hardware, out var device, out var context, out DriverType createdDriverType))
                {
                    _lastFailureReason = "D3D11CreateDeviceFailed";
                    return false;
                }
                _d3d11Device = device;
                _d3d11Context = context;
                _d3d11DriverType = createdDriverType;
                Debug.WriteLine($"[D3D] D3D11 device created: {createdDriverType}");

                Result d3d9Result = D3D9.Direct3DCreate9Ex(out _d3d9);
                if (d3d9Result.Failure || _d3d9 == null)
                {
                    _lastFailureReason = $"D3D9Create9ExFailed: 0x{d3d9Result.Code:X8}";
                    DisposeDevices();
                    return false;
                }

                var pp = new Vortice.Direct3D9.PresentParameters
                {
                    BackBufferWidth = 1,
                    BackBufferHeight = 1,
                    BackBufferFormat = Vortice.Direct3D9.Format.A8R8G8B8,
                    BackBufferCount = 1,
                    SwapEffect = Vortice.Direct3D9.SwapEffect.Discard,
                    DeviceWindowHandle = hwnd,
                    Windowed = true,
                    PresentationInterval = Vortice.Direct3D9.PresentInterval.Immediate
                };

                // Windowed 模式下不需要提供 fullscreenDisplayMode（传入默认结构体可能触发 D3DERR_INVALIDCALL）。
                _d3d9Device = _d3d9.CreateDeviceEx(
                    0,
                    Vortice.Direct3D9.DeviceType.Hardware,
                    hwnd,
                    CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded | CreateFlags.FpuPreserve,
                    pp);

                if (_d3d9Device == null)
                {
                    _lastFailureReason = "D3D9CreateDeviceExReturnedNull";
                    return false;
                }

                _lastFailureReason = null;
                return true;
            }
            catch (Exception ex)
            {
                _lastFailureReason = $"EnsureDevicesException: {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}";
                TryLogFailure(_lastFailureReason);
                DisposeDevices();
                return false;
            }
        }

        private static bool TryCreateD3D11Device(
            DriverType driverType,
            out ID3D11Device? device,
            out ID3D11DeviceContext? context,
            out DriverType createdDriverType)
        {
            device = null;
            context = null;
            createdDriverType = DriverType.Unknown;

            FeatureLevel[] featureLevels = new[]
            {
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0,
                FeatureLevel.Level_10_1,
                FeatureLevel.Level_10_0,
                FeatureLevel.Level_9_3
            };

            DeviceCreationFlags creationFlags = DeviceCreationFlags.BgraSupport;

            Result result = D3D11.D3D11CreateDevice(
                IntPtr.Zero,
                driverType,
                creationFlags,
                featureLevels,
                out device,
                out _,
                out context);

            if (result.Success)
            {
                createdDriverType = driverType;
                return device != null && context != null;
            }

            if (driverType == DriverType.Hardware)
            {
                return TryCreateD3D11Device(DriverType.Warp, out device, out context, out createdDriverType);
            }

            return false;
        }

        private bool IsDeviceRemoved()
        {
            if (_d3d11Device == null) return false;

            try
            {
                Result reason = _d3d11Device.DeviceRemovedReason;
                return reason.Failure;
            }
            catch
            {
                return true;
            }
        }

        private void CreateBackBuffer(int pixelWidth, int pixelHeight)
        {
            DetachBackBuffer();
            DisposeBackBuffer();

            if (_d3d11Device == null || _d3d11Context == null || _d3d9Device == null)
            {
                return;
            }

            try
            {
                var desc = new Texture2DDescription
                {
                    Width = (uint)pixelWidth,
                    Height = (uint)pixelHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Vortice.DXGI.Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                    CPUAccessFlags = CpuAccessFlags.None,
                    MiscFlags = ResourceOptionFlags.Shared
                };

                unsafe
                {
                    _d3d11Texture = _d3d11Device.CreateTexture2D(desc, initialData: null);
                }

                if (_d3d11Texture == null)
                {
                    _lastFailureReason = "CreateTexture2DReturnedNull";
                    return;
                }

                _d3d11Rtv = _d3d11Device.CreateRenderTargetView(_d3d11Texture);

                using var dxgiResource = _d3d11Texture.QueryInterface<IDXGIResource>();
                IntPtr sharedHandle = dxgiResource.SharedHandle;
                if (sharedHandle == IntPtr.Zero)
                {
                    _lastFailureReason = "SharedHandleIsZero";
                    return;
                }

                using var d3d9Device9 = _d3d9Device.QueryInterface<IDirect3DDevice9>();
                _d3d9Texture = d3d9Device9.CreateTexture(
                    (uint)pixelWidth,
                    (uint)pixelHeight,
                    1,
                    Vortice.Direct3D9.Usage.RenderTarget,
                    Vortice.Direct3D9.Format.A8R8G8B8,
                    Vortice.Direct3D9.Pool.Default,
                    ref sharedHandle);

                if (_d3d9Texture == null)
                {
                    _lastFailureReason = "D3D9CreateTextureReturnedNull";
                    return;
                }

                _d3d9Surface = _d3d9Texture.GetSurfaceLevel(0);
                if (_d3d9Surface == null)
                {
                    _lastFailureReason = "D3D9GetSurfaceLevelReturnedNull";
                    return;
                }

                AttachBackBuffer();
                _lastFailureReason = null;
            }
            catch (Exception ex)
            {
                _lastFailureReason = $"CreateBackBufferException: {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}";
                TryLogFailure(_lastFailureReason);
                DetachBackBuffer();
                DisposeBackBuffer();
            }
        }

        private void TryLogFailure(string reason)
        {
            long now = Environment.TickCount64;
            if (now - _lastFailureLogTick < 2000)
            {
                return;
            }

            _lastFailureLogTick = now;
            Debug.WriteLine($"[D3D] {reason}");
        }

        private void AttachBackBuffer()
        {
            if (_d3d9Surface == null) return;
            _imageSource.Lock();
            _imageSource.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _d3d9Surface.NativePointer, true);
            _imageSource.Unlock();
        }

        private void DetachBackBuffer()
        {
            try
            {
                _imageSource.Lock();
                _imageSource.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
                _imageSource.Unlock();
            }
            catch
            {
            }
        }

        private void DisposeBackBuffer()
        {
            _d3d9Surface?.Dispose();
            _d3d9Surface = null;

            _d3d9Texture?.Dispose();
            _d3d9Texture = null;

            _d3d11Rtv?.Dispose();
            _d3d11Rtv = null;

            _d3d11Texture?.Dispose();
            _d3d11Texture = null;
        }

        private void DisposeDevices()
        {
            DisposeBackBuffer();

            _d3d9Device?.Dispose();
            _d3d9Device = null;

            _d3d9?.Dispose();
            _d3d9 = null;

            _d3d11Context?.Dispose();
            _d3d11Context = null;

            _d3d11Device?.Dispose();
            _d3d11Device = null;
            _d3d11DriverType = DriverType.Unknown;
        }

        public void Dispose()
        {
            _imageSource.IsFrontBufferAvailableChanged -= ImageSource_IsFrontBufferAvailableChanged;
            DetachBackBuffer();
            DisposeDevices();
        }
    }
}
