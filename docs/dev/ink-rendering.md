# Ink Rendering Pipeline

WindBoard renders ink as a WPF `Image` (`InkSurface`) so it can participate in the normal WPF visual tree:
`ZIndex`, clipping, transforms, and overlay UI (attachments / selection overlays) all behave as expected.

## Why We Still Have DX9Ex (D3DImage Interop)

WPF's `D3DImage` accepts a D3D9 surface (`IDirect3DSurface9`) as its back buffer.
That means a DXGI / D3D11 render target cannot be presented to WPF directly without an interop bridge.

The current approach is:

- Render with D3D11 (DXGI) so Direct2D can draw efficiently (ink geometry, antialiasing, etc.).
- Create a shared D3D11 texture (`DXGI shared handle`).
- Open that shared handle as a D3D9 texture/surface and attach it to `D3DImage`.

This is the reason you will see a "DX11 -> DX9Ex -> D3DImage" chain in the codebase.
It is intentionally isolated to a single backend class.

## Code Map

- Interop backend (DX11 <-> DX9Ex <-> D3DImage):
  - `Services/InkV2/Rendering/D3DImageRenderTarget.cs`
  - Implemented behind `Services/InkV2/Rendering/IInkSurfaceRenderTarget.cs`
- WPF surface control (drives per-frame rendering, fallback state machine):
  - `Views/Controls/InkSurface.cs`
- DX renderer (Direct2D-on-DXGI drawing):
  - `Services/InkV2/Rendering/InkDxRenderer.cs`
- CPU fallback renderer (pure WPF `DrawingContext`):
  - `Services/InkV2/Rendering/InkCpuRenderer.cs`
- Shared visibility culling + self-heal logic (used by both DX and CPU paths):
  - `Services/InkV2/Rendering/InkVisibilityCulling.cs`

## Fallback Behavior

`InkSurface` attempts DX rendering when possible. If DX begin-draw or rendering fails:

- it switches to CPU fallback rendering for stability, and
- retries DX with exponential backoff.

For debugging or unstable drivers, `InkSurface` exposes a `ForceCpuFallback` switch.

## Notes / Known WPF Pitfalls

- Avoid applying `BitmapCache` to containers above `D3DImage` content.
  In practice it can cause frozen frames/ghosting on some machines.
  See related comment in `MainWindow/MainWindow.Architecture.cs`.

