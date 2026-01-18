## ADDED Requirements

### Requirement: WPF remains the primary UI host
The application MUST keep WPF as the primary UI host for the main window shell (toolbars, dialogs, settings). The canvas area MUST be hosted by a WinUI 3 XAML Island embedded inside the WPF window.

#### Scenario: App launches with WPF shell + WinUI canvas island
- **WHEN** the user launches the application
- **THEN** the main window MUST be a WPF window
- **AND** the canvas area MUST be rendered by the WinUI 3 island
- **AND** core workflows (draw ink, erase, select, zoom/pan) MUST be available

### Requirement: Unpackaged distribution remains supported
The application MUST support unpackaged deployment (no MSIX requirement) and SHOULD remain compatible with the existing installer/distribution approach (e.g., Inno Setup / zip). The distribution MUST define how Windows App SDK runtime dependencies are satisfied.

#### Scenario: Install and run without MSIX
- **WHEN** the user installs the application via the provided installer or extracted folder
- **THEN** the application MUST start and render the canvas without requiring MSIX packaging
