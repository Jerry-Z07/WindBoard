## ADDED Requirements

### Requirement: DirectX-based rendering in WPF
The application MUST render ink via DirectX (D3D11 + D2D1 or equivalent) and display it in WPF without using `InkCanvas`.

#### Scenario: Ink renders without InkCanvas
- **WHEN** ink exists on the current page
- **THEN** the ink MUST be drawn by the DirectX renderer
- **AND** the UI MUST not depend on `InkCanvas` rendering paths

### Requirement: Viewport-only rendering
The renderer MUST render only the viewport region (screen-visible area) rather than allocating and drawing a full-size canvas texture.

#### Scenario: Large canvas does not allocate huge textures
- **GIVEN** a page with an 8000×8000 logical canvas
- **WHEN** the viewport is smaller than the canvas
- **THEN** the renderer MUST allocate GPU resources proportional to the viewport size (with bounded oversampling as needed)

### Requirement: Stable performance under large ink loads
The renderer MUST remain responsive under large ink loads and MUST support view culling and caching strategies.

#### Scenario: Pan/zoom with many strokes
- **GIVEN** a page containing a very large number of points
- **WHEN** the user pans or zooms continuously
- **THEN** the renderer MUST avoid O(totalPoints) per-frame CPU work where possible by using visibility culling and cached realizations

### Requirement: Detail-preserving rendering
The renderer MUST preserve fine stroke details at normal and zoomed-in views. Any level-of-detail (LOD) simplification MUST be bounded by a screen-space error threshold and MUST NOT apply to high-quality export rendering.

#### Scenario: Fine details remain visible when zoomed in
- **GIVEN** ink contains small characters or fine strokes
- **WHEN** the user zooms in to inspect details
- **THEN** the renderer MUST draw the ink without lossy simplification that removes those details

#### Scenario: LOD is bounded and only used when zoomed out
- **GIVEN** a page with extremely dense ink
- **WHEN** the user zooms out to a far view
- **THEN** the renderer MAY simplify geometry
- **AND** the simplification MUST be bounded by a screen-space error threshold

### Requirement: Device-loss resilience
The rendering layer MUST handle device loss and MUST recover without crashing the app.

#### Scenario: Device reset
- **WHEN** the underlying DirectX device is lost or reset
- **THEN** the renderer MUST recreate required resources and resume drawing
