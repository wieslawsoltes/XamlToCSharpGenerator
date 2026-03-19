# PR Summary: VS Code Hot Design Preview, Inspector, Docs, and Packaging

## Branch

- `feature/vscode-hot-design-preview-packaging`

## Commit Breakdown

1. `50f58d94e` `Add preview hot design runtime and transport support`
2. `be0b84c17` `Add VS Code inspector and design preview tooling`
3. `d186672ef` `Record VS Code hot design implementation plan`
4. `eb2060526` `Expand docs for VS Code and source-gen initialization`
5. `08119fbb3` `Bundle missing runtime assemblies and bump alpha version`

## Overview

This branch adds end-to-end Hot Design support for the VS Code preview experience and introduces a dedicated inspector surface in the VS Code activity bar. It extends the preview host, remote protocol, Avalonia runtime bridge, and VS Code extension so design state can be queried and mutated from the editor and preview without creating a second parallel design model.

The branch also fixes multiple preview stability and packaging issues discovered while building that flow:

- initial preview startup ordering regressions
- duplicate assembly load failures in the previewer
- missing native preview host assets
- missing runtime assemblies in the umbrella NuGet package
- inspector refresh failures caused by preview design server serialization and transport problems
- `Design.PreviewWith` regressions in source-generated preview overlay handling

## Runtime / Preview Host Work

### Preview design runtime bridge

- Added preview-specific Hot Design registration and query support so preview sessions can participate in the same workspace model used by the broader Hot Design tooling.
- Introduced transport-neutral DTOs for:
  - text ranges
  - live tree snapshots
  - hit-test results
  - overlay snapshots and bounds
- Extended element models with source-span data needed for deterministic editor synchronization.

### Preview host and protocol

- Added session-local preview design resources and tools to the preview host MCP surface.
- Added preview design routing and remote command handling for:
  - workspace queries
  - logical and visual tree queries
  - hover and selection overlays
  - hit testing
  - selection and property-edit workflows
- Added a dedicated preview design client path between the preview host and the runtime-side design server.

### Preview host reliability fixes

- Fixed initial frame startup ordering so the first preview frame is sent only after the preview web client is ready.
- Fixed duplicate assembly load issues by removing repeated `Assembly.LoadFrom(...)` update behavior from the live update path.
- Fixed source-generated preview host argument resolution so the bundled designer host uses its own runtimeconfig and deps files instead of stale app-host output.
- Added support assets for the bundled designer host so platform-native dependencies such as SkiaSharp are present in the VS Code-distributed previewer.
- Fixed `Design.PreviewWith` overlay behavior for design-only roots such as `ResourceDictionary` so authored preview content is preserved.

### Inspector data stability

- Moved live-tree and hit-test queries onto the UI thread where required.
- Materialized design payloads eagerly before JSON serialization to avoid deferred enumeration against live runtime state.
- Raised JSON serializer depth to prevent deep visual trees from failing serialization.
- Hardened preview design server error handling so response failures return structured errors instead of silently tearing down the socket.

## VS Code Extension Work

### Dedicated inspector surface

- Added a dedicated left-side activity-bar container for the inspector.
- Moved inspector panels out of the preview surface and into the dedicated container.
- Added a custom rail icon and a command to focus the inspector container directly.

### Inspector panels

- Added or wired the following inspector views:
  - Documents
  - Toolbox
  - Logical Tree
  - Visual Tree
  - Properties
- Added shared design session management so preview, trees, properties, and editor state stay synchronized.

### Preview surface improvements

- Added design overlay rendering to the preview webview.
- Added preview controls for mode and tree selection.
- Refined toolbar layout so mode, tree, and zoom controls share a single row.
- Kept fixed-size preview frame behavior and centering behavior in sync with design overlays and zoom calculations.

### Interaction and synchronization

- Added preview-side hit testing and selection routing.
- Added selection synchronization across:
  - preview
  - logical tree
  - visual tree
  - properties
  - XAML editor
- Fixed tree selection to use source element identity rather than only live-tree ids.
- Added drag-and-drop from the toolbox to:
  - the XAML editor
  - the preview surface

### Preview mode behavior

- Changed the default compiler mode to `auto`.
- Updated `auto` behavior so it prefers source-generated preview only when live-preview capability is actually available and otherwise falls back cleanly.
- Preserved unsaved XAML live updates when `auto` mode resolves to fallback preview behavior.

### Properties panel

- Reworked the properties UI to resemble a more typical property grid:
  - clearer name/value columns
  - grouped sections
  - pinned and read-only badges
  - improved value editor layout
- Added client-side filtering by property name.

## Documentation Work

### Planning artifact

- Added the implementation plan document under `plan/125-vscode-hot-design-vscode-designer-plan-2026-03-17.md`.

### VS Code documentation

- Added a full Lunet docs section for the VS Code extension covering:
  - installation and setup
  - configuration
  - editing and navigation
  - preview and inspector usage
  - troubleshooting
- Updated site navigation so the VS Code docs are first-class and easy to discover.

### Source-generated initialization guidance

- Added detailed guidance for:
  - `AvaloniaNameGeneratorIsEnabled`
  - backend mode combinations
  - `InitializeComponent` generation behavior
  - `AvaloniaXamlLoader.Load(this)` incompatibility with active source-generated initialization
- Added explicit `App.axaml.cs` examples and migration guidance so application startup uses the generated initialization path correctly.

## Packaging Work

### Version bump

- Bumped the repo version suffix from `alpha.17` to `alpha.18`.
- Updated the VS Code extension package version to the same alpha.

### Marketplace publishing

- Investigated the VS Code Marketplace publish flow after a successful CI publish produced a 404 item URL and no public search results.
- Confirmed the release workflow was packaging and publishing tagged alpha releases as Marketplace pre-releases.
- Updated the release pipeline to build a Marketplace-specific stable VSIX for tagged alpha releases instead of reusing the prerelease-marked VSIX artifact.
- Added a packaging override in the VS Code extension packaging script so Marketplace publishing can explicitly disable prerelease packaging while preserving the existing default behavior elsewhere.
- This keeps GitHub and NuGet release prerelease semantics unchanged while making the Marketplace listing publicly discoverable.

### Umbrella NuGet package fix

- Audited the top-level `XamlToCSharpGenerator` package contents.
- Confirmed the single-package distribution was missing internal runtime assets required by consumers.
- Added missing runtime assemblies to the top-level package `lib/` assets for `net6.0`, `net8.0`, and `net10.0`:
  - `XamlToCSharpGenerator.MiniLanguageParsing.dll`
  - `XamlToCSharpGenerator.RemoteProtocol.dll`
  - `XamlToCSharpGenerator.Runtime.Core.dll`
  - `XamlToCSharpGenerator.Runtime.Avalonia.dll`
  - `XamlToCSharpGenerator.Runtime.dll`

### Packaging regression coverage

- Added and extended package integration tests to validate:
  - top-level package asset contents
  - standalone `Runtime.Avalonia` package dependency declarations

## Validation Summary

Validation performed during this branch included targeted test and package checks across the runtime, preview host, VS Code extension, docs, and packaging surface.

Representative checks run during the work:

- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~PreviewerHost"`
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~XamlSourceGenStudioRemoteDesignServerTests"`
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~PackageIntegrationTests"`
- `npm test -- --runInBand` in `tools/vscode/axsg-language-server`
- `dotnet tool run lunet build` in `site`
- `dotnet pack src/XamlToCSharpGenerator/XamlToCSharpGenerator.csproj -c Release`
- `npx @vscode/vsce package`

## Reviewer Notes

- The branch intentionally separates runtime/protocol work from VS Code client work and from packaging/docs updates so review can follow the layering boundaries.
- The top-level package still uses manual asset bundling rather than transitive nuspec dependencies, so the added package integration checks are important guardrails for future changes.
- The PR summary file itself is intentionally left uncommitted so it can be edited into the eventual PR description as needed.
