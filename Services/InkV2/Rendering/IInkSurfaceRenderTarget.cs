using System;
using System.Windows.Interop;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace WindBoard.Services.InkV2.Rendering
{
    internal interface IInkSurfaceRenderTarget : IDisposable
    {
        D3DImage ImageSource { get; }
        bool IsFrontBufferAvailable { get; }
        int PixelWidth { get; }
        int PixelHeight { get; }
        string? LastFailureReason { get; }

        ID3D11Device? D3D11Device { get; }
        ID3D11DeviceContext? D3D11Context { get; }
        ID3D11Texture2D? D3D11Texture { get; }
        ID3D11RenderTargetView? D3D11RenderTargetView { get; }
        DriverType D3D11DriverType { get; }

        void ResetBackBuffer();
        bool TryBeginDraw(IntPtr hwnd, int pixelWidth, int pixelHeight);
        void EndDraw();
    }
}

