---
name: release-main-tag
description: "Promotes develop to main, reviews branch diff, recreates a version tag, triggers the Release workflow, and verifies published release assets. Use when shipping a version from develop to main in this repository."
---

# Release Main Tag

Use this skill when you want a repeatable release flow:

1. Review `main` vs `develop` changes
2. Check version / tag / release notes consistency
3. Merge `develop` into `main`
4. Create or recreate tag `vX.Y.Z`
5. Trigger GitHub Release workflow by pushing tag
6. Verify workflow result and release assets

## Inputs

- `VERSION_TAG` (required): for example `v2.8.0`
- `SOURCE_BRANCH` (optional, default `develop`)
- `TARGET_BRANCH` (optional, default `main`)

## Safety Rules

- Run in a clean working tree.
- Never force-push.
- Only delete/recreate an existing tag when explicitly requested.
- If any step fails, stop and report the exact failure point.

## Steps

### 1) Preflight

```bash
git status --short --branch
git fetch origin --tags --prune
git rev-parse --verify origin/main
git rev-parse --verify origin/develop
```

### 2) Review branch differences before merge

```bash
git log --oneline origin/main..origin/develop
git diff --stat origin/main...origin/develop
```

### 3) Pre-tag consistency check (version / tag / release notes)

Before creating a tag, perform the following checks. Stop if any of them fails:

```powershell
$tag = 'vX.Y.Z'
$version = $tag.TrimStart('v','V')

# 1) The project version (Directory.Build.props) must match the version in the tag
[xml]$props = Get-Content 'Directory.Build.props'
$versionPrefix = $props.Project.PropertyGroup.VersionPrefix
if ($versionPrefix -ne $version) {
  throw "VersionPrefix mismatch: Directory.Build.props=$versionPrefix, tag=$version"
}

# 2) The release note file names must be consistent with the tag
$zh = "docs/release-notes/$tag.zh-CN.md"
$en = "docs/release-notes/$tag.en-US.md"
if (!(Test-Path $zh)) { throw "Missing release note: $zh" }
if (!(Test-Path $en)) { throw "Missing release note: $en" }

# 3) The release note content must at least contain the version identifier (vX.Y.Z or X.Y.Z)
$zhText = Get-Content -Raw -Encoding UTF8 $zh
$enText = Get-Content -Raw -Encoding UTF8 $en
if ($zhText -notmatch [regex]::Escape($tag) -and $zhText -notmatch [regex]::Escape($version)) {
  throw "Release note content mismatch: $zh"
}
if ($enText -notmatch [regex]::Escape($tag) -and $enText -notmatch [regex]::Escape($version)) {
  throw "Release note content mismatch: $en"
}
```

### 4) Merge source into target

```bash
git checkout main
git pull --ff-only origin main
git merge develop
git push origin main
```

### 5) Tag handling

If tag does not exist:

```bash
git tag -a vX.Y.Z -m "vX.Y.Z"
git push origin vX.Y.Z
```

If tag exists and user asked to recreate:

```bash
git tag -d vX.Y.Z
git push origin :refs/tags/vX.Y.Z
git tag -a vX.Y.Z -m "vX.Y.Z"
git push origin vX.Y.Z
```

### 6) Watch Release workflow

```bash
gh run list --workflow "Release" --limit 3
gh run watch <run-id> --exit-status
```

### 7) Verify release artifacts

```bash
gh release view vX.Y.Z --json tagName,url,publishedAt,assets
```

Expected assets:
- `latest.json`
- 3 portable zip packages (`win-x86`, `win-x64`, `win-arm64`)
- 6 installer exe packages (installer + `-fd` for each arch)

## Output Format

Report these items clearly:

- Final commit hash on `main`
- Tag target commit hash (`vX.Y.Z^{}`)
- Version consistency check result (`Directory.Build.props` + release notes)
- Release workflow run URL and result
- Release URL
- Asset count and key artifact names
