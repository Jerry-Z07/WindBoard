using System;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using WindBoard.Board;
using WindBoard.Board.Items;
using WindBoard.Board.Viewport;
using WindBoard.Rendering.Board;

namespace WindBoard.Tests.Rendering.Snapshot;

/// <summary>
/// 离屏渲染 harness：WARP 软件 D3D11 设备 + D2D 设备上下文 + 离屏位图，渲染后读回 BGRA 像素。
/// </summary>
/// <remarks>
/// 设备创建顺序与主程序 <c>DxSwapChainPanelRenderer.CreateDeviceResources</c> 保持一致
/// （D3D11 设备 → IDXGIDevice → D2D 工厂 → D2D 设备 → 设备上下文），仅两处不同：
/// - 设备类型固定为 <see cref="DriverType.Warp"/>（软件光栅化，无 GPU/显示器依赖，CI 可跑）；
/// - 渲染目标为 DXGI surface 支撑的离屏 D2D 位图（而非交换链面板）。
/// 与主程序同走 D2D device context 路径，<see cref="BoardSceneRenderer"/> 内部的
/// <c>WithOptionalDeviceContext2</c> / DrawInk / InkStyle 分支行为一致，保证快照保真。
/// 96 DPI 下 1 DIP = 1 像素，快照尺寸即 DIP 尺寸。
/// </remarks>
internal sealed class OffscreenRenderHarness : IDisposable
{
    /// <summary>清屏色：与主程序默认画布底色一致（#2E2F33）。</summary>
    internal static readonly Color4 CanvasClearColor = new(0x2E / 255.0f, 0x2F / 255.0f, 0x33 / 255.0f, 1.0f);

    /// <summary>清屏色的 BGRA 字节基准（unorm 转换 = round(f×255)，供自检逐字节断言）。</summary>
    internal static readonly byte[] CanvasClearColorBgra = [0x33, 0x2F, 0x2E, 0xFF];

    private const float SnapshotDpi = 96.0f;

    private readonly ID3D11Device _d3dDevice;
    private readonly ID3D11DeviceContext _d3dContext;
    private readonly ID2D1Factory1 _d2dFactory;
    private readonly ID2D1DeviceContext _d2dContext;
    private readonly ID3D11Texture2D _targetTexture;
    private readonly ID2D1Bitmap1 _targetBitmap;
    private readonly ID3D11Texture2D _stagingTexture;

    private bool _disposed;

    public OffscreenRenderHarness(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;

        // D3D11 WARP 软件设备：BgraSupport 是 D2D 互操作（CreateBitmapFromDxgiSurface）的硬性要求；
        // 与主程序一致不启用 Debug 层（CI 环境可能没有 SDK 层）。
        var featureLevels = new Vortice.Direct3D.FeatureLevel[]
        {
            Vortice.Direct3D.FeatureLevel.Level_11_1,
            Vortice.Direct3D.FeatureLevel.Level_11_0,
        };

        Result result = D3D11.D3D11CreateDevice(
            IntPtr.Zero,
            DriverType.Warp,
            DeviceCreationFlags.BgraSupport,
            featureLevels,
            out ID3D11Device? device,
            out _,
            out ID3D11DeviceContext? context);

        if (result.Failure || device is null || context is null)
        {
            context?.Dispose();
            device?.Dispose();
            throw new InvalidOperationException($"创建 WARP D3D11 设备失败：0x{result.Code:X8}");
        }

        _d3dDevice = device;
        _d3dContext = context;

        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded, DebugLevel.None);

        // D2D 设备与上下文必须来自同一工厂/设备链（跨工厂资源会导致 EndDraw 失败，见 quality-guidelines）。
        using IDXGIDevice dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
        using ID2D1Device d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
        _d2dContext = d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
        _d2dContext.SetDpi(SnapshotDpi, SnapshotDpi);

        // 渲染目标纹理（GPU 可写）→ DXGI surface → D2D 位图（作为上下文 target）。
        var targetDesc = new Texture2DDescription(
            Format.B8G8R8A8_UNorm,
            (uint)width,
            (uint)height,
            arraySize: 1,
            mipLevels: 1,
            bindFlags: BindFlags.RenderTarget,
            usage: ResourceUsage.Default,
            cpuAccessFlags: CpuAccessFlags.None,
            sampleCount: 1,
            sampleQuality: 0,
            miscFlags: ResourceOptionFlags.None);
        _targetTexture = _d3dDevice.CreateTexture2D(targetDesc);

        using IDXGISurface targetSurface = _targetTexture.QueryInterface<IDXGISurface>();
        // bitmapProperties 传 null：由 D2D 从 surface 推断像素格式（B8G8R8A8_UNorm/Premultiplied），
        // DPI 默认 96（与上下文 SetDpi 一致）。显式指定 Target 选项的属性组反而触发 E_INVALIDARG
        // （实测；与 OneMore 等成熟离屏实现一致，DXGI surface 支撑的位图可直接 SetTarget）。
        _targetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(targetSurface, null);
        _d2dContext.Target = _targetBitmap;

        // staging 纹理（CPU 可读）用于像素读回。
        var stagingDesc = new Texture2DDescription(
            Format.B8G8R8A8_UNorm,
            (uint)width,
            (uint)height,
            arraySize: 1,
            mipLevels: 1,
            bindFlags: BindFlags.None,
            usage: ResourceUsage.Staging,
            cpuAccessFlags: CpuAccessFlags.Read,
            sampleCount: 1,
            sampleQuality: 0,
            miscFlags: ResourceOptionFlags.None);
        _stagingTexture = _d3dDevice.CreateTexture2D(stagingDesc);
    }

    /// <summary>快照宽度（像素，96 DPI 下等于 DIP）。</summary>
    public int Width { get; }

    /// <summary>快照高度（像素，96 DPI 下等于 DIP）。</summary>
    public int Height { get; }

    /// <summary>
    /// 渲染一帧并读回 BGRA 像素缓冲。
    /// </summary>
    /// <param name="document">被渲染文档。</param>
    /// <param name="activeInkItem">活动笔迹（可空）。</param>
    /// <param name="viewport">视口（调用方负责设置相机/缩放/尺寸）。</param>
    /// <returns>BGRA 字节缓冲，长度 = width * height * 4，行内无 padding。</returns>
    public byte[] Render(BoardDocument document, IBoardInkItem? activeInkItem, BoardViewport viewport)
    {
        return Render(ctx => new BoardSceneRenderer().Draw(ctx, document, activeInkItem, viewport));
    }

    /// <summary>
    /// 渲染一帧并读回 BGRA 像素缓冲（低层入口，允许自定义绘制动作）。
    /// </summary>
    public byte[] Render(Action<ID2D1RenderTarget> drawAction)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _d2dContext.BeginDraw();
        Result endResult;
        try
        {
            // BoardSceneRenderer 只负责场景内容，清屏是调用方职责（与主程序 Render 流程一致）。
            _d2dContext.Clear(CanvasClearColor);
            drawAction(_d2dContext);
        }
        finally
        {
            // EndDraw 与 BeginDraw 必须配对执行；结果在 finally 之外判定（CA2219）。
            endResult = _d2dContext.EndDraw(out _, out _);
        }

        if (endResult.Failure)
        {
            throw new InvalidOperationException($"D2D EndDraw 失败：0x{endResult.Code:X8}");
        }

        _d3dContext.CopyResource(_stagingTexture, _targetTexture);

        byte[] pixels = new byte[Width * Height * 4];
        int srcStride = Width * 4;
        MappedSubresource mapped = _d3dContext.Map(_stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            // staging 纹理的 RowPitch 不保证等于 width*4（驱动可加 padding），必须逐行拷贝。
            for (int y = 0; y < Height; y++)
            {
                Marshal.Copy(
                    IntPtr.Add(mapped.DataPointer, y * (int)mapped.RowPitch),
                    pixels,
                    y * srcStride,
                    srcStride);
            }
        }
        finally
        {
            _d3dContext.Unmap(_stagingTexture, 0);
        }

        return pixels;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _stagingTexture.Dispose();
        _d2dContext.Target = null;
        _targetBitmap.Dispose();
        _targetTexture.Dispose();
        _d2dContext.Dispose();
        _d2dFactory.Dispose();
        _d3dContext.Dispose();
        _d3dDevice.Dispose();
    }
}
