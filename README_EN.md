[English](./README.en.md) | [简体中文](./README.md)

<div align="center">
  <img src="./WindBoard/Assets/icon.png" alt="icon" width="20%" />
</div>

<div align="center">
  <h2><strong>WindBoard</strong></h2>
</div>

<div align="center">
  An open-source, simple, and easy-to-use whiteboard for Windows
</div>

---

![GitHub commit activity (branch)](https://img.shields.io/github/commit-activity/m/Jerry-Z07/WindBoard/main) ![GitHub Issues or Pull Requests](https://img.shields.io/github/issues/Jerry-Z07/WindBoard) ![GitHub License](https://img.shields.io/github/license/Jerry-Z07/WindBoard)

> [!NOTE]
> This project is under active development. APIs and features may change frequently. Use with caution in production.

> [!IMPORTANT]
> The app is not mature yet. If you use it in high-stakes scenarios (e.g., classrooms), make sure you can handle unexpected issues (pen feel anomalies, crashes, etc.).

## Screenshots
Screenshots and user documentation are being added. PRs are welcome.

## Features
- Basic inking (thickness, color, erase, undo/redo, etc.)
- Export / import
- Multi-page management
- Infinite canvas
- Multi-language support
- ... (WIP)

### Some special features
- Disguise mode: replace the app icon and title
- Visual presenter integration: basic integration with Seewo Visual Presenter (an entry point to jump to it)

## Tech stack
- UI: WinUI 3 (Windows App SDK)
- Rendering: DirectX (Vortice.Direct2D1 / Vortice.Direct3D11)
- Runtime: .NET 10 (`net10.0-windows`)
- Serialization: System.Text.Json + custom workspace format (WBIX)
- Tests: xUnit (`WindBoard.Tests`)
- Packaging: Inno Setup (`installer/`)

## Quick start

> [!IMPORTANT]
> This project is developed entirely with AI. If this is not acceptable for you, please consider alternative software.

### For users
Download from [Releases](https://github.com/Jerry-Z07/WindBoard/releases/latest):
- `.exe` is the installer, `.zip` is the portable build
- Builds with `-fd` are framework-dependent (no bundled runtime). You need to install the .NET 10 Desktop Runtime yourself.
  - Runtime download: <https://dotnet.microsoft.com/download/dotnet/10.0>

If you run into issues or want a feature, please open an Issue.

### For developers
#### Requirements
- .NET 10.0 SDK
- Windows 10/11 (minimum `10.0.19041.0`)
- IDEs:
  - Visual Studio 2026 or later
  - JetBrains Rider 2025.3.1 or later
  - Visual Studio Code

#### Build & test
- Build: `dotnet build WindBoard.slnx -c Release -p:Platform=x64`
- Test: `dotnet test WindBoard.slnx -p:Platform=x64`

You can also read these AI-generated summaries (mostly accurate):
[![zread](https://img.shields.io/badge/Ask_Zread-_.svg?style=flat&color=00b0aa&labelColor=000000&logo=data%3Aimage%2Fsvg%2Bxml%3Bbase64%2CPHN2ZyB3aWR0aD0iMTYiIGhlaWdodD0iMTYiIHZpZXdCb3g9IjAgMCAxNiAxNiIgZmlsbD0ibm9uZSIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj4KPHBhdGggZD0iTTQuOTYxNTYgMS42MDAxSDIuMjQxNTZDMS44ODgxIDEuNjAwMSAxLjYwMTU2IDEuODg2NjQgMS42MDE1NiAyLjI0MDFWNC45NjAxQzEuNjAxNTYgNS4zMTM1NiAxLjg4ODEgNS42MDAxIDIuMjQxNTYgNS42MDAxSDQuOTYxNTZDNS4zMTUwMiA1LjYwMDEgNS42MDE1NiA1LjMxMzU2IDUuNjAxNTYgNC45NjAxVjIuMjQwMUM1LjYwMTU2IDEuODg2NjQgNS4zMTUwMiAxLjYwMDEgNC45NjE1NiAxLjYwMDFaIiBmaWxsPSIjZmZmIi8%2BCjxwYXRoIGQ9Ik00Ljk2MTU2IDEwLjM5OTlIMi4yNDE1NkMxLjg4ODEgMTAuMzk5OSAxLjYwMTU2IDEwLjY4NjQgMS42MDE1NiAxMS4wMzk5VjEzLjc1OTlDMS42MDE1NiAxNC4xMTM0IDEuODg4MSAxNC4zOTk5IDIuMjQxNTYgMTQuMzk5OUg0Ljk2MTU2QzUuMzE1MDIgMTQuMzk5OSA1LjYwMTU2IDE0LjExMzQgNS42MDE1NiAxMy43NTk5VjExLjAzOTlDNS42MDE1NiAxMC42ODY0IDUuMzE1MDIgMTAuMzk5OSA0Ljk2MTU2IDEwLjM5OTlaIiBmaWxsPSIjZmZmIi8%2BCjxwYXRoIGQ9Ik0xMy43NTg0IDEuNjAwMUgxMS4wMzg0QzEwLjY4NSAxLjYwMDEgMTAuMzk4NCAxLjg4NjY0IDEwLjM5ODQgMi4yNDAxVjQuOTYwMUMxMC4zOTg0IDUuMzEzNTYgMTAuNjg1IDUuNjAwMSAxMS4wMzg0IDUuNjAwMUgxMy43NTg0QzE0LjExMTkgNS42MDAxIDE0LjM5ODQgNS4zMTM1NiAxNC4zOTg0IDQuOTYwMVYyLjI0MDFDMTQuMzk4NCAxLjg4NjY0IDE0LjExMTkgMS42MDAxIDEzLjc1ODQgMS42MDAxWiIgZmlsbD0iI2ZmZiIvPgo8cGF0aCBkPSJNNCAxMkwxMiA0TDQgMTJaIiBmaWxsPSIjZmZmIi8%2BCjxwYXRoIGQ9Ik00IDEyTDEyIDQiIHN0cm9rZT0iI2ZmZiIgc3Ryb2tlLXdpZHRoPSIxLjUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIvPgo8L3N2Zz4K&logoColor=ffffff)](https://zread.ai/Jerry-Z07/WindBoard) (Chinese/English)
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/Jerry-Z07/WindBoard) (English)

## Documentation
- WBIX format: `docs/dev/EN/WBIX_EN.md`
- Localization conventions: `docs/dev/EN/LOCALIZATION_EN.md`

## Roadmap / TODO
- [ ] Better stroke smoothing
- [ ] Zoom/move performance with lots of strokes
- [ ] Touch area detection (for palm eraser)
- [ ] Overall performance improvements
- [ ] More complete docs

## Contributing
Issues and PRs are welcome. Thanks for your support!

## Ecosystem
To save and restore ink states, we designed a private format: `.wbix` (see `docs/dev/EN/WBIX_EN.md`).



## License
Apache License 2.0

