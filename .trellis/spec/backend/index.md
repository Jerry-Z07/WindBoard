# Backend Development Guidelines

> Best practices for backend (domain logic, services, persistence) development in WindBoard.

---

## Overview

WindBoard backend refers to the Board domain model layer, service layer, and persistence layer. It is pure C# code with no UI dependencies. Core principle: the Board layer must stay clean and must not reference any UI namespace.

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

- [ ] [Directory Structure](./directory-structure.md) - Confirm files are placed in the correct layer
- [ ] [Error Handling](./error-handling.md) - Confirm the correct error-handling pattern is used
- [ ] [Logging Guidelines](./logging-guidelines.md) - Confirm logs are not added in high-frequency paths
