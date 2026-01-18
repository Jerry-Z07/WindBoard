## ADDED Requirements

### Requirement: Pointer input is captured in the island and bridged to core modes
The canvas island MUST capture pen/touch/mouse input via the WinUI Pointer event model and MUST bridge it into the existing core interaction modes (Ink/Eraser/Select) using a platform-neutral DTO or adapter layer.

#### Scenario: Pen pressure is captured and reflected in strokes
- **GIVEN** the user draws with a pen device that reports pressure
- **WHEN** the user writes a stroke on the canvas
- **THEN** the input bridge MUST provide pressure values to the ink pipeline
- **AND** the renderer MUST reflect pressure in stroke appearance according to tool settings

### Requirement: Intermediate points are used for high-frequency sampling
The input bridge MUST use intermediate points (or an equivalent mechanism) to increase sampling density for pen input when available, in order to maintain low-latency, smooth strokes.

#### Scenario: High sampling density during fast pen movement
- **WHEN** the user draws a fast stroke
- **THEN** the input bridge MUST provide multiple points between frame updates when available
