# Finish Work - Pre-Commit Checklist

Before submitting or committing, use this checklist to ensure work completeness.

**Timing**: After code is written and tested, before commit

---

## Checklist

### 1. Code Quality

```bash
# Must pass
dotnet build WindBoard.slnx -c Release
dotnet test WindBoard.slnx
```

- [ ] `dotnet build` passes with 0 errors?
- [ ] `dotnet test` passes with no failures?
- [ ] No `Console.WriteLine` statements (use `AppLog`)?
- [ ] No null-forgiving operator (`x!`) without justification?
- [ ] No `dynamic` or untyped usage without justification?

### 2. Code-Spec Sync

**Code-Spec Docs**:
- [ ] Does `.trellis/spec/backend/` need updates?
  - New patterns, new modules, new conventions
- [ ] Does `.trellis/spec/frontend/` need updates?
  - New components, new hooks, new patterns
- [ ] Does `.trellis/spec/guides/` need updates?
  - New cross-layer flows, lessons from bugs

**Key Question**: 
> "If I fixed a bug or discovered something non-obvious, should I document it so future me (or others) won't hit the same issue?"

If YES -> Update the relevant code-spec doc.

### 2.5. Code-Spec Hard Block (Infra/Cross-Layer)

If this change touches infra or cross-layer contracts, this is a blocking checklist:

- [ ] Spec content is executable (real signatures/contracts), not principle-only text
- [ ] Includes file path + command/API name + payload field names
- [ ] Includes validation and error matrix
- [ ] Includes Good/Base/Bad cases
- [ ] Includes required tests and assertion points

**Block Rule**:
If infra/cross-layer changed but the related spec is still abstract, do NOT finish. Run `/trellis:update-spec` manually first.

### 3. Public API Changes

If you modified public interfaces or command signatures:

- [ ] Interface signatures updated?
- [ ] Callers updated to match?
- [ ] Tests updated to cover new contract?

### 4. Persistence / Data Format Changes

If you modified file formats, serialization, or persistence:

- [ ] Version compatibility considered?
- [ ] Related parsers/serializers updated?
- [ ] Import/export logic updated (if applicable)?

### 5. Cross-Layer Verification

If the change spans multiple layers:

- [ ] Data flows correctly through all layers?
- [ ] Error handling works at each boundary?
- [ ] Types are consistent across layers?
- [ ] Loading states handled?

### 6. Manual Testing

- [ ] Feature works in the app?
- [ ] Edge cases tested?
- [ ] Error states tested?
- [ ] Works after app restart?

---

## Quick Check Flow

```bash
# 1. Code checks
dotnet build WindBoard.slnx -c Release

# 2. View changes
git status
git diff --name-only

# 3. Based on changed files, check relevant items above
```

---

## Common Oversights

| Oversight | Consequence | Check |
|-----------|-------------|-------|
| Code-spec docs not updated | Others don't know the change | Check .trellis/spec/ |
| Spec text is abstract only | Easy regressions in infra/cross-layer changes | Require signature/contract/matrix/cases/tests |
| Migration not created | Schema out of sync | Check db/migrations/ |
| Types not synced | Runtime errors | Check shared types |
| Tests not updated | False confidence | Run full test suite |
| Console.WriteLine left in | Noisy production logs | Search for Console.WriteLine |

---

## Relationship to Other Commands

```
Development Flow:
  Write code -> Test -> /trellis:finish-work -> git commit -> /trellis:record-session
                          |                              |
                   Ensure completeness              Record progress
                   
Debug Flow:
  Hit bug -> Fix -> /trellis:break-loop -> Knowledge capture
                       |
                  Deep analysis
```

- `/trellis:finish-work` - Check work completeness (this command)
- `/trellis:record-session` - Record session and commits
- `/trellis:break-loop` - Deep analysis after debugging

---

## Core Principle

> **Delivery includes not just code, but also documentation, verification, and knowledge capture.**

Complete work = Code + Docs + Tests + Verification
