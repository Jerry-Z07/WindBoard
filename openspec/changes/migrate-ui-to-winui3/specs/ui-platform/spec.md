## ADDED Requirements

### Requirement: WinUI 3 is the UI host (WPF replaced)
The application MUST provide a WinUI 3 (Windows App SDK) UI host for the main window experience, including page management and the ink canvas view. The legacy WPF host MUST be removed from the shipped product.

#### Scenario: App launches with WinUI host
- **WHEN** the user launches the application
- **THEN** the main UI MUST be hosted by WinUI 3
- **AND** core workflows (open/create page, draw ink, erase, select) MUST be available

#### Scenario: No WPF host is shipped
- **WHEN** the application is installed and launched
- **THEN** it MUST NOT require WPF windows/controls for the primary experience
- **AND** ink presentation MUST NOT depend on WPF `D3DImage`

### Requirement: Input is captured via Pointer events
The UI host MUST capture pen/touch/mouse input via the WinUI Pointer event model and MUST provide pressure-aware sampling when available.

#### Scenario: Pen pressure is captured
- **GIVEN** the user draws with a pen device that reports pressure
- **WHEN** the user writes a stroke
- **THEN** the input pipeline MUST receive pressure values
- **AND** the renderer MUST reflect pressure in stroke appearance according to tool settings

### Requirement: No dependency on WPF in the UI host
The WinUI UI host MUST NOT require WPF runtime types (`System.Windows.*`) for UI composition or ink presentation.

#### Scenario: UI runs without WPF
- **WHEN** the WinUI host starts
- **THEN** the UI MUST function without loading WPF windows or controls

### Requirement: Unpackaged distribution
The application MUST support unpackaged WinUI 3 deployment (no MSIX packaging) and SHOULD remain compatible with the existing installer/distribution approach (e.g., Inno Setup / zip).

#### Scenario: Install and run without MSIX
- **WHEN** the user installs the application via the provided installer or extracted folder
- **THEN** the application MUST start and render ink without requiring MSIX packaging
