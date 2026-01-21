## ADDED Requirements

### Requirement: WBI v2 ink storage
The application MUST support a new WBI ink storage format (v2) that serializes v2 ink data (strokes, fragments, points, tools) per page.

#### Scenario: Save and load v2 ink
- **GIVEN** a page with v2 ink
- **WHEN** the user exports and later imports a WBI file
- **THEN** the ink MUST be restored with the same geometry, ordering, and appearance

#### Scenario: Thickness semantics preserved
- **GIVEN** strokes use different thickness semantics (view-invariant vs world-invariant)
- **WHEN** the page is saved and loaded via WBI v2
- **THEN** the restored strokes MUST keep the same thickness semantics

### Requirement: Import legacy ISF ink
The application MUST be able to import legacy WBI pages that store ink in `.isf` (`System.Windows.Ink`) by converting them into the v2 ink model.

#### Scenario: Import old WBI
- **GIVEN** a WBI file containing `.isf` ink
- **WHEN** the user opens it in the new engine
- **THEN** the ink MUST be converted into v2 strokes/fragments/points with reasonable fidelity (position, color, thickness, pressure)

#### Scenario: Legacy import chooses a compatible thickness semantics
- **GIVEN** the legacy ink appearance assumed a specific thickness behavior
- **WHEN** the ink is imported into v2
- **THEN** the importer MUST assign a thickness semantics that best matches the legacy behavior by default
