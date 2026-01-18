## MODIFIED Requirements

### Requirement: DirectX-based rendering via DXGI swap chain
The application MUST render ink via DirectX (D3D11 + D2D1 or equivalent) and MUST present it via a DXGI swap chain hosted in XAML (e.g., WinUI 3 `SwapChainPanel`). The presentation path MUST NOT depend on WPF `D3DImage` or D3D9 interop.

#### Scenario: Ink renders via swap chain presentation
- **WHEN** ink exists on the current page
- **THEN** the ink MUST be drawn by the DirectX renderer
- **AND** the rendered output MUST be presented via a DXGI swap chain
- **AND** the system MUST NOT require `D3DImage` / `IDirect3DSurface9` to display ink

## ADDED Requirements

### Requirement: Stable overlay composition in XAML host
The system MUST allow UI overlays (selection UI, attachments, toolbars) to be composited above the ink surface in the same XAML visual tree without airspace limitations.

#### Scenario: Overlay renders above ink
- **GIVEN** ink is visible on the canvas
- **WHEN** the user shows selection UI or attachments above the canvas
- **THEN** the overlay MUST appear above the ink
- **AND** hit testing for the overlay MUST work correctly
