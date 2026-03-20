# Developer Guide

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10) (version pinned in `global.json`)
- An IDE with Avalonia support (Rider or VS Code with C# Dev Kit)

---

## Building

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release
```

## Running

```bash
# Direct run
dotnet run --project SmartCopy.App

# Watch mode (auto-reloads on file changes)
dotnet watch run --project SmartCopy.App
```

## Testing

```bash
# Run all tests
dotnet test

# Run a single test class
dotnet test --filter "FullyQualifiedName~TestClassName"
```

Tests mostly use `MemoryFileSystemProvider` for fast, hermetic file system operations — no real I/O.

---

## Publishing Locally

Three publish profiles are defined in `SmartCopy.App/Properties/PublishProfiles/`:

| Profile | Output |
|---|---|
| `win-x64` | `artifacts/publish/SmartCopy.App/release_win-x64/SmartCopy.exe` |
| `linux-x64` | `artifacts/publish/SmartCopy.App/release_linux-x64/SmartCopy` |
| `osx-arm64` | `artifacts/publish/SmartCopy.App/release_osx-arm64/SmartCopy` |

All profiles produce a **self-contained single-file binary** with no external dependencies.

```bash
dotnet publish SmartCopy.App/SmartCopy.App.csproj -p:PublishProfile=win-x64
```

In VS Code, use the **publish** task (Ctrl+Shift+B → publish) and pick a profile from the dropdown.

---

## Release Pipeline

Releases are fully automated via GitHub Actions. To cut a release, push a version tag:

```bash
git tag v1.2.3
git push origin v1.2.3
```

This triggers [`.github/workflows/release.yml`](../.github/workflows/release.yml), which runs four jobs:

### 1. `build` — Three parallel platform builds

Each platform builds on its native runner:

| Runner | Profile | Archive |
|---|---|---|
| `windows-latest` | `win-x64` | `SmartCopy-{version}-win-x64.zip` |
| `ubuntu-latest` | `linux-x64` | `SmartCopy-{version}-linux-x64.tar.gz` |
| `macos-latest` | `osx-arm64` | `SmartCopy-{version}-osx-arm64.tar.gz` |

Each archive contains the binary, `README.md`, and `LICENSE`.

### 2. `release` — GitHub Release

Downloads all three archives and creates a GitHub Release with GitHub auto-generated release notes and all archives attached as assets.

### 3. `winget` — Windows Package Manager submission (optional)

Uses [`vedantmgoyal2009/winget-releaser`](https://github.com/vedantmgoyal2009/winget-releaser) to fork `microsoft/winget-pkgs`, generate the YAML manifests for `machinewrapped.SmartCopy`, and open a PR automatically.

- First submission requires manual approval from the winget team (~1-2 days).
- Subsequent version bumps are reviewed faster once the package is established.
- Requires the `WINGET_TOKEN` secret (see [Secrets](#secrets) below).
- The job is skipped if `WINGET_TOKEN` is not set.
- A notice is emitted when the job is skipped.

After the winget PR merges, users can install via:
```
winget install machinewrapped.SmartCopy
```

### 4. `homebrew` — Homebrew tap update (optional)

Downloads the macOS archive, computes its SHA256, then pushes an updated `Casks/smartcopy.rb` to the [`machinewrapped/homebrew-smartcopy`](https://github.com/machinewrapped/homebrew-smartcopy) tap repo with the new version and checksum.

- Requires the `HOMEBREW_TAP_TOKEN` secret (see [Secrets](#secrets) below).
- The job is skipped if `HOMEBREW_TAP_TOKEN` is not set.
- The job is also skipped if the tap repo does not exist yet.
- A notice is emitted when the job is skipped due to a missing tap repo.
- A notice is emitted when `HOMEBREW_TAP_TOKEN` is not set.
- macOS builds are not signed/notarized; Homebrew is the recommended channel even though Gatekeeper prompts may still appear.

After the tap is updated, users can install via:
```
brew tap machinewrapped/smartcopy
brew install --cask smartcopy
```

---

## Secrets

Optional secrets for distribution channels in the SmartCopy2026 repo (Settings → Secrets → Actions):

| Secret | Purpose | Required PAT scopes |
|---|---|---|
| `WINGET_TOKEN` | Fork winget-pkgs and create PRs | `public_repo` |
| `HOMEBREW_TAP_TOKEN` | Push commits to homebrew-smartcopy | `repo` (full) |

---

## One-Time Setup

- Publish profiles created in `SmartCopy.App/Properties/PublishProfiles/`
- `<AssemblyName>SmartCopy</AssemblyName>` set in `SmartCopy.App.csproj` so the binary is named `SmartCopy` / `SmartCopy.exe` rather than `SmartCopy.App`
- Homebrew cask template at [`.github/homebrew/smartcopy.rb`](../.github/homebrew/smartcopy.rb) — copy this into `Casks/smartcopy.rb` in the `machinewrapped/homebrew-smartcopy` tap repo as the initial file

---

## Versioning

Versions are driven entirely by git tags (`v{major}.{minor}.{patch}`). The tag is passed to `dotnet publish` as `-p:Version=...` at build time, so the assembly version matches the release tag. There is no version property in any `.csproj`; `Directory.Build.props` provides a fallback of `2.0.0` for local builds, which the release pipeline overrides with the git tag.
