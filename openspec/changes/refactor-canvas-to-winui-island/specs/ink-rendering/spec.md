## ADDED Requirements

### Requirement: DXGI swap chain presentation in WinUI 3 island
The application MUST render ink via DirectX (D3D11 + D2D1 or equivalent) and MUST present the result via a DXGI swap chain hosted in a WinUI 3 `SwapChainPanel` embedded as a XAML Island inside the WPF window. Ink presentation MUST NOT depend on WPF `D3DImage` or any D3D9 interop.

#### Scenario: Ink renders via swap chain inside WPF shell
- **GIVEN** the main window is hosted by WPF
- **AND** the user opens a page that contains ink
- **WHEN** the canvas is shown
- **THEN** the ink MUST be drawn by the DirectX renderer
- **AND** the rendered output MUST be presented via a DXGI swap chain in the WinUI island
- **AND** the system MUST NOT require `D3DImage` / `IDirect3DSurface9` to display ink

### Requirement: Overlay compositing inside the island
The system MUST allow canvas overlays (attachments, selection UI, eraser cursor, selection dock) to be composited above the ink surface inside the same WinUI XAML visual tree, without WPF airspace limitations.

#### Scenario: Overlay renders above ink and receives hit testing
- **GIVEN** ink is visible on the canvas
- **WHEN** the user shows selection UI or interacts with an attachment
- **THEN** the overlay MUST appear above the ink
- **AND** hit testing for the overlay MUST work correctly

### Requirement: Rendering lifecycle stability
The swap chain based presentation MUST handle window lifecycle events (resize, DPI change, minimize/restore, device lost) without crashing and MUST recover rendering automatically.

#### Scenario: Resize and device lost recovery
- **WHEN** the window is resized or DPI changes
- **THEN** the swap chain buffers MUST be resized/recreated as needed
- **AND** the next frame MUST render correctly
- **WHEN** the graphics device is removed/reset (device lost)
- **THEN** the system MUST recreate device-dependent resources and resume rendering
