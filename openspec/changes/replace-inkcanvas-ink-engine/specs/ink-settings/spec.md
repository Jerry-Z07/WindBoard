## ADDED Requirements

### Requirement: Rebuilt writing settings model
Writing-related settings (smoothing, pressure/pen tip behavior, size) MUST be rebuilt around the v2 engine and MUST NOT be implemented via `InkCanvas`/`DrawingAttributes` state.

#### Scenario: Settings drive v2 engine only
- **WHEN** the user changes writing settings
- **THEN** the changes MUST apply to the v2 ink engine/tool model
- **AND** legacy `InkCanvas`-specific settings MUST NOT be required for correct behavior

### Requirement: Switchable stroke thickness semantics
The application MUST support both view-invariant and world-invariant stroke thickness semantics, and the user MUST be able to switch between them.

#### Scenario: View-invariant thickness
- **WHEN** view-invariant thickness is enabled
- **THEN** the stroke thickness MUST remain visually constant on screen during zoom in/out

#### Scenario: World-invariant thickness
- **WHEN** world-invariant thickness is enabled
- **THEN** the stroke thickness MUST scale with zoom in/out (thicker when zoomed in, thinner when zoomed out)

#### Scenario: Persist thickness semantics per stroke
- **GIVEN** a stroke was created under a specific thickness semantics
- **WHEN** the user later switches the thickness semantics setting
- **THEN** existing strokes MUST keep their original appearance semantics

### Requirement: Remove legacy writing settings
Legacy writing settings that were coupled to the `InkCanvas` implementation MUST be removed (or ignored) to prevent mixed behavior with the v2 engine.

#### Scenario: Legacy settings do not affect v2 ink
- **GIVEN** a settings file contains legacy writing fields from older versions
- **WHEN** the application loads settings under the v2 engine
- **THEN** those legacy fields MUST NOT change v2 ink behavior

### Requirement: Extensible processing pipeline
The ink engine MUST provide a clear processing pipeline boundary (input → filtering → smoothing → pressure mapping → stroke output) to enable future ink processing features.

#### Scenario: Add a new processor
- **GIVEN** a future feature needs to process ink points (e.g., recognition or replay)
- **WHEN** a new processor is introduced
- **THEN** it MUST be possible to integrate it without rewriting the renderer or the document core
