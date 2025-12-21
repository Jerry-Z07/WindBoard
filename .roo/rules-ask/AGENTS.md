# Project Documentation Rules (Non-Obvious Only)

- **UI Language**: All UI labels and comments use **Simplified Chinese (zh-CN)** despite being a .NET WPF application.
- **Material Design Theme**: Uses Material Design 3.
- **Touch Gesture Implementation**: Complex pinch-to-zoom uses snapshot-based tracking to prevent drift (non-standard approach).
- **Stroke Thickness Scaling**: Stroke thickness inversely scales with zoom for visual consistency (unusual for drawing applications).
- **Event Handling Pattern**: XAML `Tag` attributes are heavily used for parameter passing instead of command parameters.
- **Color Presets**: 9 specific colors defined in XAML with specific hex codes.
- **Tool Organization**: Three main tools (Pen, Eraser, Select) with thickness and color popup for Pen tool.
- **Zoom Range**: Limited to 0.5x to 5.0x zoom range with specific implementation details.