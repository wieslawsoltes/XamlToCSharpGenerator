---
title: "Packaging and Release"
---

# Packaging and Release

AXSG ships NuGet packages, a .NET CLI tool, and a VS Code extension.

## Local packaging

Use the repo scripts:

```bash
./build-docs.sh
bash eng/release/package-artifacts.sh 0.1.0-local
```

```powershell
./build-docs.ps1
pwsh eng/release/package-artifacts.ps1 -Version 0.1.0-local
```

## CI and release flows

- `CI` validates builds, tests, packages, and docs
- `Release` packs and publishes NuGet packages and the VS Code extension
- `Docs` publishes the Lunet site to GitHub Pages

See the [Artifact Matrix](../reference/artifact-matrix) for shipped packages and the [Installation](../getting-started/installation) guide for local setup.
