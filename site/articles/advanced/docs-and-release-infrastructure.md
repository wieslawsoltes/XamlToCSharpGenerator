---
title: "Docs and Release Infrastructure"
---

# Docs and Release Infrastructure

AXSG ships documentation, NuGet packages, a .NET tool, and a VS Code extension. The docs and release infrastructure ties those surfaces together so the published site, shipped artifacts, and CI outputs stay aligned.

## Docs stack

The docs site uses Lunet and generates both narrative pages and .NET API pages.

Key inputs:

- `site/config.scriban`
- `site/menu.yml`
- `site/articles/**`
- `site/.lunet/includes/_builtins/bundle.sbn-html`
- `site/.lunet/css/template-main.css`
- `site/.lunet/css/site-overrides.css`

Key scripts:

- `build-docs.sh` / `build-docs.ps1`
- `check-docs.sh` / `check-docs.ps1`
- `serve-docs.sh` / `serve-docs.ps1`

## Release pipeline

The release workflow is responsible for:

- packing NuGet artifacts
- packaging the VS Code extension as a VSIX
- publishing NuGet packages
- publishing the VS Code extension to the Marketplace when credentials are configured
- creating the GitHub release payload

## CI responsibilities

CI should prove more than compilation:

- docs build must succeed
- docs validation must reject broken internal links and raw markdown links
- packaged artifacts must still build from the checked-in scripts
- release workflows must continue to accept the version contract from `Directory.Build.props`

## Versioning model

The site and release pipeline assume shared repo versioning:

- .NET packages read `VersionPrefix` / `VersionSuffix` from `Directory.Build.props`
- the VS Code extension manifest is updated in lockstep
- CI/release workflows derive preview and tagged versions from that shared contract

## Operational guidance

When you change docs or release plumbing:

1. run `check-docs.sh`
2. rebuild the site locally
3. verify the generated API landing pages render correctly
4. verify internal article links do not point to raw `.md` files
5. if you touch release scripts, validate both shell and PowerShell entry points

## Related docs

- [Lunet Docs Pipeline](../reference/lunet-docs-pipeline/)
- [Packaging and Release](../guides/packaging-and-release/)
- [Artifact Matrix](../reference/artifact-matrix/)
