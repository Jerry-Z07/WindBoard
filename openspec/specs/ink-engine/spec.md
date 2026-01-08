# ink-engine Specification

## Purpose
TBD - created by archiving change refactor-ink-engine. Update Purpose after archive.
## Requirements
### Requirement: Ink Backend Abstraction
The application MUST route inking operations through a backend abstraction so that the input pipeline does not depend on WPF `InkCanvas` rendering/editing behavior.

#### Scenario: Default backend is the custom ink engine
- **GIVEN** the application is using default settings
- **WHEN** the user writes on the canvas
- **THEN** strokes are created, rendered, and edited using the custom backend
- **AND** the application does not rely on `InkCanvas` built-in selection/eraser behavior.

#### Scenario: No backend-selection setting exists
- **GIVEN** the application is using default settings
- **WHEN** the user changes app settings
- **THEN** there is no option to switch to a legacy `InkCanvas` backend.

### Requirement: Backend-agnostic Stroke Model
The system MUST define a backend-agnostic stroke model that can represent ink content independently of WPF `Stroke` objects.

#### Scenario: Model captures points and metadata
- **WHEN** a stroke is created from input samples
- **THEN** the model stores a stable stroke identifier, color, logical thickness, and brush kind
- **AND** the model stores the point stream (canvas DIP coordinates, pressure, timestamp) required for replay and rendering.

### Requirement: Incremental Rendering (Custom Backend)
The ink engine MUST support incremental rendering such that updating an active stroke does not require rebuilding or redrawing all historical strokes each frame.

#### Scenario: Active stroke updates do not rebuild history
- **GIVEN** a page already contains many finalized strokes
- **WHEN** the user draws a new stroke
- **THEN** only visuals associated with the active stroke are updated for each input batch
- **AND** cached visuals for finalized strokes are reused.

### Requirement: Tile-baked Cache (Custom Backend)
The ink engine MUST support a tile-baked cache for finalized strokes so that rendering cost is bounded by the number of dirty tiles rather than total stroke count.

#### Scenario: Finalized strokes are cached by tiles
- **GIVEN** a page contains many finalized strokes
- **WHEN** the user continues writing without modifying historical strokes
- **THEN** finalized stroke content is reused from the tile cache
- **AND** only tiles affected by new ink are updated.

### Requirement: Legacy Backend Removal
The application MUST remove the legacy `InkCanvas` backend once the custom backend meets editing parity for core operations.

#### Scenario: Legacy backend is not available after parity
- **GIVEN** the custom backend supports partial point erasing and selection operations (move/resize/copy/delete/reorder)
- **WHEN** the application starts
- **THEN** the custom backend is used
- **AND** there is no legacy `InkCanvas` backend fallback.

### Requirement: WBI Versioning for Ink Model
The application MUST continue to import existing WBI v1.0 files, and it MAY introduce a new WBI export version that older WindBoard versions are not required to open.

#### Scenario: Importing existing WBI v1.0 continues to work
- **GIVEN** a WBI v1.0 file contains per-page `.isf` stroke data
- **WHEN** the user imports the WBI file
- **THEN** the page ink content is restored correctly

#### Scenario: Exporting model ink can require a newer app version
- **GIVEN** the user exports content that requires the backend-agnostic stroke model for fidelity
- **WHEN** the application writes a WBI file
- **THEN** the manifest sets `min_compatible_version` to a version that ensures unsupported older apps will not import it
- **AND** the WBI includes a versioned ink payload for the model.

