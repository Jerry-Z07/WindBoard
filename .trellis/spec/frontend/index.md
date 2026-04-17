# Frontend Development Guidelines

> Best practices for frontend (WinUI 3 UI, controls, features) development in WindBoard.

---

## Overview

WindBoard 前端指 WinUI 3 XAML 页面、UserControl、MainWindow 和 Feature UI。项目不使用 MVVM，code-behind 直接操控控件，服务通过静态单例访问。

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

---

## Pre-Development Checklist

Before modifying frontend code, read:

- [ ] [Component Guidelines](./component-guidelines.md) — 确认使用正确的事件处理和通信模式
- [ ] [State Management](./state-management.md) — 确认通过正确的路径修改状态
- [ ] [Quality Guidelines](./quality-guidelines.md) — 确认遵循 WinUI 和本地化约定
