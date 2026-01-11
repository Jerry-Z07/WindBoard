using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Vortice.Direct3D11;
using WindBoard.Services.InkV2.Rendering;

namespace WindBoard.Controls
{
    public sealed class InkSurface : Image
    {
        private D3DImageRenderTarget? _renderTarget;
        private bool _isRenderingSubscribed;
        private bool _needsRender = true;
        private bool _useCpuFallback;
        private long _nextDxRetryTick;
        private int _dxRetryDelayMs = 1200;
        private int _dxFailureCount;

        public event EventHandler<InkSurfaceRenderEventArgs>? RenderFrame;
        public event EventHandler<InkSurfaceFallbackRenderEventArgs>? RenderFallbackFrame;

        public InkSurface()
        {
            Stretch = Stretch.Fill;
            SnapsToDevicePixels = true;
            Loaded += InkSurface_Loaded;
            Unloaded += InkSurface_Unloaded;
            SizeChanged += InkSurface_SizeChanged;
            IsVisibleChanged += InkSurface_IsVisibleChanged;
        }

        protected override Size MeasureOverride(Size constraint)
        {
            Size desired = base.MeasureOverride(constraint);

            double w = Width;
            double h = Height;

            if (!double.IsNaN(w) && w > 0)
            {
                desired.Width = w;
            }

            if (!double.IsNaN(h) && h > 0)
            {
                desired.Height = h;
            }

            return desired;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double w = Width;
            double h = Height;

            if (!double.IsNaN(w) && w > 0)
            {
                finalSize.Width = w;
            }

            if (!double.IsNaN(h) && h > 0)
            {
                finalSize.Height = h;
            }

            _ = base.ArrangeOverride(finalSize);
            return finalSize;
        }

        public void InvalidateSurface()
        {
            _needsRender = true;
            InvalidateVisual();
        }

        public void ResetSurface()
        {
            try
            {
                EnsureRenderTarget();
                _renderTarget?.ResetBackBuffer();
                _useCpuFallback = false;
                ResetDxRetry();
                _needsRender = true;
                InvalidateVisual();
            }
            catch
            {
            }
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
            double dipWidth = ActualWidth;
            double dipHeight = ActualHeight;
            if (dipWidth <= 0 && !double.IsNaN(Width) && Width > 0) dipWidth = Width;
            if (dipHeight <= 0 && !double.IsNaN(Height) && Height > 0) dipHeight = Height;

            int pixelWidth = (int)Math.Ceiling(Math.Max(0, dipWidth) * dpi.DpiScaleX);
            int pixelHeight = (int)Math.Ceiling(Math.Max(0, dipHeight) * dpi.DpiScaleY);

            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                _needsRender = true;
                TryLogZeroSize(pixelWidth, pixelHeight);
                return;
            }

            if (pixelWidth != _renderTarget.PixelWidth || pixelHeight != _renderTarget.PixelHeight)
            {
                _needsRender = true;
            }

            if (!_needsRender) return;

            IntPtr hwnd = TryGetHwnd();
            if (hwnd == IntPtr.Zero) return;

            if (_useCpuFallback && !ShouldAttemptDxRetry())
            {
                return;
            }

            if (!_renderTarget.TryBeginDraw(hwnd, pixelWidth, pixelHeight))
            {
                if (!_useCpuFallback)
                {
                    _useCpuFallback = true;
                    string reason = _renderTarget.LastFailureReason ?? "Unknown";
                    Debug.WriteLine($"[InkSurface] DX begin-draw failed -> CPU fallback (frontBuffer={_renderTarget.IsFrontBufferAvailable} size={pixelWidth}x{pixelHeight})");
                    Debug.WriteLine($"[InkSurface] DX failure reason: {reason}");
                    InvalidateVisual();
                }
                ScheduleDxRetry();
                return;
            }

            try
            {
                ID3D11Device? device = _renderTarget.D3D11Device;
                ID3D11DeviceContext? context = _renderTarget.D3D11Context;
                ID3D11RenderTargetView? rtv = _renderTarget.D3D11RenderTargetView;
                ID3D11Texture2D? texture = _renderTarget.D3D11Texture;

                if (device == null || context == null || rtv == null || texture == null)
                {
                    return;
                }

                context.OMSetRenderTargets(rtv);
                context.ClearRenderTargetView(rtv, new Vortice.Mathematics.Color4(0, 0, 0, 0));

                RenderFrame?.Invoke(
                    this,
                    new InkSurfaceRenderEventArgs(
                        device,
                        context,
                        texture,
                        rtv,
                        pixelWidth,
                        pixelHeight,
                        dpi.DpiScaleX,
                        dpi.DpiScaleY));

                context.Flush();
                _needsRender = false;
                if (_useCpuFallback)
                {
                    _useCpuFallback = false;
                    ResetDxRetry();
                    Debug.WriteLine($"[InkSurface] CPU fallback cleared (DX resumed, driver={_renderTarget?.D3D11DriverType})");
                }
            }
            catch (Exception ex)
            {
                _needsRender = true;
                if (!_useCpuFallback)
                {
                    _useCpuFallback = true;
                    Debug.WriteLine($"[InkSurface] DX render failed -> CPU fallback: {ex}");
                    InvalidateVisual();
                }
                ScheduleDxRetry();
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

        private bool ShouldAttemptDxRetry()
        {
            long now = Environment.TickCount64;
            return now >= _nextDxRetryTick;
        }

        private void ScheduleDxRetry()
        {
            long now = Environment.TickCount64;
            int delay = Math.Clamp(_dxRetryDelayMs, 250, 20000);
            _nextDxRetryTick = now + delay;
            _dxRetryDelayMs = Math.Min(delay * 2, 20000);
            _dxFailureCount++;

            if (_dxFailureCount == 4)
            {
                Debug.WriteLine($"[InkSurface] DX retry backoff -> {delay}ms");
            }
        }

        private void ResetDxRetry()
        {
            _dxFailureCount = 0;
            _dxRetryDelayMs = 1200;
            _nextDxRetryTick = 0;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (_useCpuFallback && RenderFallbackFrame != null)
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                double dipWidth = ActualWidth;
                double dipHeight = ActualHeight;
                if (dipWidth <= 0 && !double.IsNaN(Width) && Width > 0) dipWidth = Width;
                if (dipHeight <= 0 && !double.IsNaN(Height) && Height > 0) dipHeight = Height;

                int pixelWidth = (int)Math.Ceiling(Math.Max(0, dipWidth) * dpi.DpiScaleX);
                int pixelHeight = (int)Math.Ceiling(Math.Max(0, dipHeight) * dpi.DpiScaleY);

                RenderFallbackFrame.Invoke(
                    this,
                    new InkSurfaceFallbackRenderEventArgs(
                        drawingContext,
                        pixelWidth,
                        pixelHeight,
                        dpi.DpiScaleX,
                        dpi.DpiScaleY));
                _needsRender = false;
                return;
            }

            base.OnRender(drawingContext);
        }

        private long _lastZeroSizeLogTick;
        private void TryLogZeroSize(int pixelWidth, int pixelHeight)
        {
            long now = Environment.TickCount64;
            if (now - _lastZeroSizeLogTick < 1200)
            {
                return;
            }

            _lastZeroSizeLogTick = now;

            string parentInfo = DescribeElement(Parent as FrameworkElement);
            string vParentInfo = DescribeElement(VisualTreeHelper.GetParent(this) as FrameworkElement);
            string windowInfo = DescribeElement(Window.GetWindow(this));
            bool hasSource = PresentationSource.FromVisual(this) != null;
            Debug.WriteLine(
                $"[InkSurface] Skip render: Actual={ActualWidth:F1}x{ActualHeight:F1} px={pixelWidth}x{pixelHeight} " +
                $"Width={Width:F1} Height={Height:F1} Visible={IsVisible} Loaded={IsLoaded} HasSource={hasSource} " +
                $"driver={_renderTarget?.D3D11DriverType} Parent={parentInfo} VParent={vParentInfo} Window={windowInfo}");
        }

        private static string DescribeElement(FrameworkElement? element)
        {
            if (element == null) return "null";
            return $"{element.GetType().Name}({element.ActualWidth:F1}x{element.ActualHeight:F1},vis={element.Visibility},isVis={element.IsVisible})";
        }
    }

    public sealed class InkSurfaceRenderEventArgs : EventArgs
    {
        public ID3D11Device Device { get; }
        public ID3D11DeviceContext Context { get; }
        public ID3D11Texture2D RenderTargetTexture { get; }
        public ID3D11RenderTargetView RenderTargetView { get; }
        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public double DpiScaleX { get; }
        public double DpiScaleY { get; }

        public InkSurfaceRenderEventArgs(
            ID3D11Device device,
            ID3D11DeviceContext context,
            ID3D11Texture2D renderTargetTexture,
            ID3D11RenderTargetView renderTargetView,
            int pixelWidth,
            int pixelHeight,
            double dpiScaleX,
            double dpiScaleY)
        {
            Device = device;
            Context = context;
            RenderTargetTexture = renderTargetTexture;
            RenderTargetView = renderTargetView;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            DpiScaleX = dpiScaleX;
            DpiScaleY = dpiScaleY;
        }
    }

    public sealed class InkSurfaceFallbackRenderEventArgs : EventArgs
    {
        public DrawingContext DrawingContext { get; }
        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public double DpiScaleX { get; }
        public double DpiScaleY { get; }

        public InkSurfaceFallbackRenderEventArgs(
            DrawingContext drawingContext,
            int pixelWidth,
            int pixelHeight,
            double dpiScaleX,
            double dpiScaleY)
        {
            DrawingContext = drawingContext;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            DpiScaleX = dpiScaleX;
            DpiScaleY = dpiScaleY;
        }
    }
}
