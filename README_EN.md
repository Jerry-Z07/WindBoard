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

![GitHub commit activity (branch)](https://img.shields.io/github/commit-activity/m/Jerry-Z07/WindBoard/main) ![GitHub Issues or Pull Requests](https://img.shields.io/github/issues/Jerry-Z07/WindBoard) ![GitHub License](https://img.shields.io/github/license/Jerry-Z07/WindBoard) [![zread](https://img.shields.io/badge/Ask_Zread-_.svg?style=flat&color=00b0aa&labelColor=000000&logo=data%3Aimage%2Fsvg%2Bxml%3Bbase64%2CPHN2ZyB3aWR0aD0iMTYiIGhlaWdodD0iMTYiIHZpZXdCb3g9IjAgMCAxNiAxNiIgZmlsbD0ibm9uZSIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj4KPHBhdGggZD0iTTQuOTYxNTYgMS42MDAxSDIuMjQxNTZDMS44ODgxIDEuNjAwMSAxLjYwMTU2IDEuODg2NjQgMS42MDE1NiAyLjI0MDFWNC45NjAxQzEuNjAxNTYgNS4zMTM1NiAxLjg4ODEgNS42MDAxIDIuMjQxNTYgNS42MDAxSDQuOTYxNTZDNS4zMTUwMiA1LjYwMDEgNS42MDE1NiA1LjMxMzU2IDUuNjAxNTYgNC45NjAxVjIuMjQwMUM1LjYwMTU2IDEuODg2NjQgNS4zMTUwMiAxLjYwMDEgNC45NjE1NiAxLjYwMDFaIiBmaWxsPSIjZmZmIi8%2BCjxwYXRoIGQ9Ik00Ljk2MTU2IDEwLjM5OTlIMi4yNDE1NkMxLjg4ODEgMTAuMzk5OSAxLjYwMTU2IDEwLjY4NjQgMS42MDE1NiAxMS4wMzk5VjEzLjc1OTlDMS42MDE1NiAxNC4xMTM0IDEuODg4MSAxNC4zOTk5IDIuMjQxNTYgMTQuMzk5OUg0Ljk2MTU2QzUuMzE1MDIgMTQuMzk5OSA1LjYwMTU2IDE0LjExMzQgNS42MDE1NiAxMy43NTk5VjExLjAzOTlDNS42MDE1NiAxMC42ODY0IDUuMzE1MDIgMTAuMzk5OSA0Ljk2MTU2IDEwLjM5OTlaIiBmaWxsPSIjZmZmIi8%2BCjxwYXRoIGQ9Ik0xMy43NTg0IDEuNjAwMUgxMS4wMzg0QzEwLjY4NSAxLjYwMDEgMTAuMzk4NCAxLjg4NjY0IDEwLjM5ODQgMi4yNDAxVjQuOTYwMUMxMC4zOTg0IDUuMzEzNTYgMTAuNjg1IDUuNjAwMSAxMS4wMzg0IDUuNjAwMUgxMy43NTg0QzE0LjExMTkgNS42MDAxIDE0LjM5ODQgNS4zMTM1NiAxNC4zOTg0IDQuOTYwMVYyLjI0MDFDMTQuMzk4NCAxLjg4NjY0IDE0LjExMTkgMS42MDAxIDEzLjc1ODQgMS42MDAxWiIgZmlsbD0iI2ZmZiIvPgo8cGF0aCBkPSJNNCAxMkwxMiA4TDQgMTJaIiBmaWxsPSIjZmZmIi8%2BCjxwYXRoIGQ9Ik00IDEyTDEyIDgiIHN0cm9rZT0iI2ZmZiIgc3Ryb2tlLXdpZHRoPSIxLjUiIHN0cm9rZS1saW5lY2FwPSJyb3VuZCIvPgo8L3N2Zz4K&logoColor=ffffff)](https://zread.ai/Jerry-Z07/WindBoard) [![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/Jerry-Z07/WindBoard)

> [!NOTE]
> ~~This project is under active development. APIs and features may change frequently.~~ Use with caution in production.  
> Due to heavy school workload, the developer may not be able to handle Issues and PRs in time, and the development progress will also slow down.

> [!IMPORTANT]
> This project is developed entirely with AI. If this is not acceptable for you, please consider alternative software.  
> The software is not mature yet. If you use it in high-stakes production environments (e.g., classrooms), please ensure you can handle unexpected issues caused by the software (such as pen feel anomalies, crashes, etc.).

## Screenshots
<details>
<summary>Click to view screenshots</summary>

![Main Window](./docs/assets/images/MainWindow.png)
![About](./docs/assets/images/About.png)
</details>

## Features
- Basic inking (thickness, color, eraser, undo/redo, etc.)
- Export / import
- Multi-page management
- Infinite canvas
- Multi-language support
- ... (WIP)

### Highlights
- Highly customizable: optional content can be attached to the Dock bar
- Disguise mode: make the software appear as a custom icon/process name

## Tech stack
- UI: WinUI 3 (Windows App SDK)
- Rendering: DirectX (Vortice.Direct2D1 / Vortice.Direct3D11)
- Runtime: .NET 10 (`net10.0-windows`)
- Serialization: System.Text.Json + custom workspace format (WBIX)
- Tests: xUnit (`WindBoard.Tests`)
- Packaging: Inno Setup (`installer/`)

## Quick start

You can download the software from [Releases](https://github.com/Jerry-Z07/WindBoard/releases/latest):
- `.exe` is the installer, `.zip` is the portable build
- Builds with `-fd` are framework-dependent (no bundled runtime). You need to install the .NET 10 Desktop Runtime yourself; builds without `-fd` are self-contained and do not require a separate runtime installation
  - Runtime download: <https://dotnet.microsoft.com/download/dotnet/10.0>

## Contributing
Issues and PRs are welcome. Thanks for your support!

### Development Environment Requirements
- .NET 10.0 SDK
- Windows 10/11 (minimum `10.0.19041.0`)
- Development Tools:
  - Visual Studio 2026 or later
  - JetBrains Rider 2025.3.1 or later
  - Visual Studio Code

### Build & Test
- Build: `dotnet build WindBoard.slnx -c Release`
- Test: `dotnet test WindBoard.slnx`

### Contributing Translations
Please refer to the [Localization Conventions](./docs/dev/guides/localization.en-US.md) document

### Vibe Coding
- The project is currently configured with [AGENTS.md](AGENTS.md)
- Tools used in development:
  - MCP

| **MCP**    | **Description**                                                | **Tutorial**                                                            |
|------------|----------------------------------------------------------------|-------------------------------------------------------------------------|
| ace-tool   | Codebase search tool                                          | https://linux.do/t/topic/1360514                                      |
| context7   | Query development docs                                        | https://github.com/upstash/context7#installation                      |
| fetch      | Fetch web content                                             | https://github.com/modelcontextprotocol/servers/tree/main/src/fetch   |
| github     | Assist with GitHub operations                                 | https://github.com/github/github-mcp-server                           |

  - Others

| **Tool**                                               | **Recommendation Reason**       | **Notes** |
|--------------------------------------------------------|--------------------------------|-----------|
| [qlty](https://docs.qlty.sh/cli/quickstart)            | Code review & analysis CLI     | Open source, project was initialized |
| [MCP Router](https://github.com/mcp-router/mcp-router) | Simplify MCP tool configuration | Open source |


## Roadmap / TODO
- [ ] UI unification and beautification
- [ ] Better stroke smoothing algorithm
- [ ] Touch area detection algorithm (for palm eraser)
- [ ] Overall performance optimization
- [ ] Complete documentation

## Eco
To save and restore ink states, we designed a private format: `.wbix`.  
See [WBIX format](./docs/dev/guides/wbix.en-US.md)

## License
Apache License 2.0

