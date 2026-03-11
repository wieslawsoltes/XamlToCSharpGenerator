---
title: "Packaging and Release"
---

# Packaging and Release

This guide covers how AXSG packages NuGet artifacts, the VSIX, docs output, and release metadata.

## Artifact families

AXSG ships:

- NuGet packages for compiler/runtime/tooling layers
- a .NET tool package for the language server
- a VSIX for the VS Code extension
- a Lunet-generated docs site published to GitHub Pages

## Local packaging workflow

### Shell

```bash
bash ./eng/release/package-artifacts.sh 0.1.0-local
```

### PowerShell

```powershell
pwsh ./eng/release/package-artifacts.ps1 -Version 0.1.0-local
```

By default these write to:

- `artifacts/nuget`
- `artifacts/vsix`

## CI and release split

### CI

CI is expected to:

- build and test the supported graph
- package NuGet artifacts and the VSIX
- build docs and upload them as artifacts
- validate the docs site and release scripts

### Release workflow

The release workflow is expected to:

- pack the same artifacts from a tagged version
- publish NuGet packages when `NUGET_API_KEY` is configured
- publish the VS Code extension when `VSCE_PAT` is configured
- create the GitHub release payload

## Version sources

- `.NET` artifacts use `VersionPrefix` and `VersionSuffix` from `Directory.Build.props`
- the VS Code extension manifest version must stay aligned with that contract
- preview/CI versions are derived by the workflow/scripts rather than hardcoded in multiple places

## Release checklist

1. verify docs build with `check-docs.sh`
2. verify package/VSIX scripts locally if release plumbing changed
3. update package/extension metadata if the artifact surface changed
4. tag the release version
5. ensure required secrets/environments are configured

## Related docs

- [Artifact Matrix](../reference/artifact-matrix/)
- [Package and Assembly](../reference/package-and-assembly/)
- [Docs and Release Infrastructure](../advanced/docs-and-release-infrastructure/)
- [Lunet Docs Pipeline](../reference/lunet-docs-pipeline/)
