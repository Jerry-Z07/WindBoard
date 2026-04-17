# Backend Development Guidelines

> Best practices for backend (domain logic, services, persistence) development in WindBoard.

---

## Overview

WindBoard 后端指 Board 域模型层、服务层和持久化层——纯 C# 代码，无 UI 依赖。核心原则：Board 层必须保持纯净，不引用任何 UI 命名空间。

---

## Guidelines Index

| Guide | Description | Status |
|-------|-------------|--------|
| [Directory Structure](./directory-structure.md) | Module organization and file layout | Filled |
| [Data Persistence](./database-guidelines.md) | WBIX format, settings storage, path infrastructure | Filled |
| [Error Handling](./error-handling.md) | Error types, crash reporter, Result patterns | Filled |
| [Quality Guidelines](./quality-guidelines.md) | Code standards, forbidden patterns, testing | Filled |
| [Logging Guidelines](./logging-guidelines.md) | AppLog, log levels, what to log/not log | Filled |

---

## Pre-Development Checklist

Before modifying backend code, read:

- [ ] [Directory Structure](./directory-structure.md) — 确认文件放在正确的层
- [ ] [Error Handling](./error-handling.md) — 确认使用正确的错误处理模式
- [ ] [Logging Guidelines](./logging-guidelines.md) — 确认不在高频路径添加日志
