# Frontend Development Guidelines

> Best practices for frontend (WinUI 3 UI, controls, features) development in WindBoard.
>
> The WinForms crash window (`WindBoard.CrashReporter`) is the one non-WinUI UI in the repo; its contract is in [Crash Reporter UI](./crash-reporter-ui.md).

---

## Overview

WindBoard frontend refers to WinUI 3 XAML pages, UserControls, the MainWindow, and Feature UI. The project does not use MVVM; code-behind manipulates controls directly, and services are accessed through static singletons.

---

## Guidelines Index

| Guide | Description | Status |
|-------|-------------|--------|
| [Directory Structure](./directory-structure.md) | Feature module organization, partial class patterns | Filled |
| [Component Guidelines](./component-guidelines.md) | UserControl patterns, event handling, localization | Filled |
| [Hook Guidelines](./hook-guidelines.md) | Event-driven patterns, lifecycle hooks, DispatcherQueue | Filled |
| [State Management](./state-management.md) | Domain state, settings state, UI local state | Filled |
| [Quality Guidelines](./quality-guidelines.md) | Forbidden patterns, WinUI best practices, review checklist | Filled |
| [Type Safety](./type-safety.md) | Nullable types, record patterns, Result objects | Filled |
| [WinUI Dependencies](./winui-dependencies.md) | WinAppSDK/CommunityToolkit 版本契约、FileSavePicker 占位文件判重、弹窗内容自适应约定（DevWinUI 已移除） | Filled |
| [Crash Reporter UI](./crash-reporter-ui.md) | WinForms 崩溃窗口：跨进程参数契约、高 DPI 配置基线、`TableLayoutPanel` 行分配规则 | Filled |

---

## Pre-Development Checklist

Before modifying frontend code, read:

- [ ] [Component Guidelines](./component-guidelines.md) - Confirm the correct event-handling and communication pattern is used
- [ ] [State Management](./state-management.md) - Confirm state is changed through the correct path
- [ ] [Quality Guidelines](./quality-guidelines.md) - Confirm WinUI and localization conventions are followed
- [ ] [Crash Reporter UI](./crash-reporter-ui.md) - Required only when changing `WindBoard.CrashReporter` or the crash-launch parameters
