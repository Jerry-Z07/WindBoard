using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using WindBoard.Services.InkV2.Rendering;

namespace WindBoard.Controls
{
    public sealed class InkSurface : Image
    {
        private D3DImageRenderTarget? _renderTarget;
        private bool _isRenderingSubscribed;
        private bool _needsRender = true;

        public event EventHandler<InkSurfaceRenderEventArgs>? RenderFrame;

        public InkSurface()
        {
            Stretch = Stretch.Fill;
            SnapsToDevicePixels = true;
            Loaded += InkSurface_Loaded;
            Unloaded += InkSurface_Unloaded;
            SizeChanged += InkSurface_SizeChanged;
            IsVisibleChanged += InkSurface_IsVisibleChanged;
        }

        public void InvalidateSurface()
        {
            _needsRender = true;
        }

        private void InkSurface_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureRenderTarget();
            EnsureRenderingSubscription();
            _needsRender = true;
        }

        private void InkSurface_Unloaded(object sender, RoutedEventArgs e)
        {
            RemoveRenderingSubscription();
            DisposeRenderTarget();
        }

        private void InkSurface_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible)
            {
                RemoveRenderingSubscription();
                return;
            }

            EnsureRenderTarget();
            EnsureRenderingSubscription();
            _needsRender = true;
        }

        private void InkSurface_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _needsRender = true;
        }

        private void EnsureRenderTarget()
        {
            if (_renderTarget != null) return;

            _renderTarget = new D3DImageRenderTarget();
            _renderTarget.ImageSource.IsFrontBufferAvailableChanged += ImageSource_IsFrontBufferAvailableChanged;
            Source = _renderTarget.ImageSource;
        }

        private void DisposeRenderTarget()
        {
            if (_renderTarget == null) return;

            try
            {
                _renderTarget.ImageSource.IsFrontBufferAvailableChanged -= ImageSource_IsFrontBufferAvailableChanged;
            }
            catch
            {
            }

            try
            {
                _renderTarget.Dispose();
            }
            catch
            {
            }

            _renderTarget = null;
        }

        private void ImageSource_IsFrontBufferAvailableChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            _needsRender = true;
        }

        private void EnsureRenderingSubscription()
        {
            if (_isRenderingSubscribed) return;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
            _isRenderingSubscribed = true;
        }

        private void RemoveRenderingSubscription()
        {
            if (!_isRenderingSubscribed) return;
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _isRenderingSubscribed = false;
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (!IsVisible) return;

            if (_renderTarget == null)
            {
                EnsureRenderTarget();
                if (_renderTarget == null) return;
            }

            var dpi = VisualTreeHelper.GetDpi(this);
            int pixelWidth = (int)Math.Ceiling(Math.Max(0, ActualWidth) * dpi.DpiScaleX);
            int pixelHeight = (int)Math.Ceiling(Math.Max(0, ActualHeight) * dpi.DpiScaleY);

            if (pixelWidth != _renderTarget.PixelWidth || pixelHeight != _renderTarget.PixelHeight)
            {
                _needsRender = true;
            }

            if (!_needsRender) return;

            IntPtr hwnd = TryGetHwnd();
            if (hwnd == IntPtr.Zero) return;

            if (!_renderTarget.TryBeginDraw(hwnd, pixelWidth, pixelHeight))
            {
                return;
            }

            try
            {
                ID3D11Device? device = _renderTarget.D3D11Device;
                ID3D11DeviceContext? context = _renderTarget.D3D11Context;
                ID3D11RenderTargetView? rtv = _renderTarget.D3D11RenderTargetView;

                if (device == null || context == null || rtv == null)
                {
                    return;
                }

                context.OMSetRenderTargets(rtv);
                context.ClearRenderTargetView(rtv, new Color4(0, 0, 0, 0));

                RenderFrame?.Invoke(
                    this,
                    new InkSurfaceRenderEventArgs(
                        device,
                        context,
                        rtv,
                        pixelWidth,
                        pixelHeight,
                        dpi.DpiScaleX,
                        dpi.DpiScaleY));

                context.Flush();
                _needsRender = false;
            }
            catch
            {
                _needsRender = true;
            }
            finally
            {
                try
                {
                    _renderTarget.EndDraw();
                }
                catch
                {
                }
            }
        }

        private IntPtr TryGetHwnd()
        {
            if (PresentationSource.FromVisual(this) is not HwndSource hwndSource)
            {
                return IntPtr.Zero;
            }

            return hwndSource.Handle;
        }
    }

    public sealed class InkSurfaceRenderEventArgs : EventArgs
    {
        public ID3D11Device Device { get; }
        public ID3D11DeviceContext Context { get; }
        public ID3D11RenderTargetView RenderTargetView { get; }
        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public double DpiScaleX { get; }
        public double DpiScaleY { get; }

        public InkSurfaceRenderEventArgs(
            ID3D11Device device,
            ID3D11DeviceContext context,
            ID3D11RenderTargetView renderTargetView,
            int pixelWidth,
            int pixelHeight,
            double dpiScaleX,
            double dpiScaleY)
        {
            Device = device;
            Context = context;
            RenderTargetView = renderTargetView;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            DpiScaleX = dpiScaleX;
            DpiScaleY = dpiScaleY;
        }
    }
}

