## ADDED Requirements

### Requirement: InkDocument v2 data model
The application MUST represent page ink using a self-owned `InkDocument` model (not `System.Windows.Ink`), including strokes, fragments, points, and z-order.

#### Scenario: Page stores v2 ink data
- **WHEN** a page contains ink
- **THEN** the page MUST persist and operate on `InkDocument` as the source of truth
- **AND** the UI layer MUST NOT require `InkCanvas` or `StrokeCollection` to function

### Requirement: Partial erasing produces fragments
The ink system MUST support arbitrary partial erasing and MUST split affected strokes into fragments rather than deleting the entire stroke.

#### Scenario: Erase middle of a stroke
- **GIVEN** a stroke fragment crosses the eraser path
- **WHEN** the user erases through the middle of that fragment
- **THEN** the system MUST remove the erased portion and create two or more remaining fragments as needed

### Requirement: Spatial index for hit-testing
The ink system MUST maintain a spatial index to support fast hit-testing and region queries without scanning all strokes.

#### Scenario: Hit-test uses the index
- **GIVEN** a document with a large number of stroke segments
- **WHEN** the user performs selection or erasing
- **THEN** the engine MUST query candidates via the spatial index before performing precise geometry tests

### Requirement: Preserve vector point data
The ink system MUST preserve ink as vector point data (including pressure where available) and MUST NOT rely on raster-only representations as the source of truth.

#### Scenario: Zoomed-in inspection uses preserved points
- **GIVEN** a page contains fine ink details
- **WHEN** the user zooms in to inspect those details
- **THEN** the system MUST be able to render from preserved vector points rather than a lossy raster cache

### Requirement: Undo/redo for ink edits
Ink edits MUST be undoable and redoable per page, including writing, erasing splits, selection transforms, delete/copy, and z-order changes.

#### Scenario: Undo restores erased parts
- **GIVEN** the user partially erased a stroke
- **WHEN** the user triggers Undo
- **THEN** the original fragments MUST be restored exactly
