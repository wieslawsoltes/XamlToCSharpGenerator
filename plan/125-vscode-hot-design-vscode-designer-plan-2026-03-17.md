# VS Code Hot Design Parity for Preview + XAML Editor

Date: 2026-03-17
Status: Implemented

## Goal

Bring VS Code preview and XAML editing closer to Hot Design Studio by reusing the existing AXSG Hot Design runtime workspace for:

- preview hit testing
- selection synchronization
- logical and visual tree inspection
- properties inspection and editing
- insert/remove/undo/redo
- source-range aware editor navigation

The VS Code extension remains a client. Runtime semantic truth stays in the Hot Design workspace.

## Scope

### Runtime and transport

- Extend Hot Design element nodes with precise source ranges.
- Add transport-neutral DTOs for:
  - live tree snapshots
  - overlay snapshots
  - hit-test results
- Reuse `AxsgRuntimeQueryService`, `AxsgRuntimeHotDesignService`,
  `XamlSourceGenStudioHitTestingService`, and
  `XamlSourceGenStudioLiveTreeProjectionService`.
- Add preview-only bridge state so the preview designer host can expose:
  - current live root
  - active build uri/source path
  - current XAML text
  - hover and selection overlay state
- Add a preview-only update applier so Hot Design mutations can succeed without writing source files directly from the preview process.

### Preview host

- Start a session-local design sidecar in the bundled designer host.
- Extend preview-host helper protocol with design commands.
- Extend preview-host MCP surface with `axsg.preview.design.*` tools/resources.
- Proxy preview design operations through the preview host so VS Code never talks directly to the designer process.

### VS Code extension

- Add `AXSG Design` activity bar container.
- Add views:
  - Documents
  - Toolbox
  - Logical Tree
  - Visual Tree
  - Properties
- Add a shared `DesignSessionController` to own:
  - preview-host design requests
  - cached workspace/tree/overlay state
  - editor synchronization
  - mutation application via minimal diffs
- Add preview overlay rendering and toolbar mode controls in the existing preview webview.

## Implementation plan

### Phase 1. Runtime source model and payloads

- Extend `SourceGenHotDesignElementNode` with `SourceRange`.
- Reuse `XamlSourceGenHotDesignDocumentEditor` to compute exact element spans.
- Serialize `SourceRange` through runtime payload builders.
- Add DTOs for live-tree, overlay, and hit-test responses.

### Phase 2. Preview runtime bridge

- Add a preview bridge in the designer host runtime that tracks the current root and XAML text.
- Register the active preview document with Hot Design in both preview compiler modes.
- Start a preview-only Hot Design update applier that returns mutation results without persisting files.
- Reuse live-tree and hit-testing helpers against the current preview root.

### Phase 3. Preview design transport

- Extend the studio remote command catalog for:
  - workspace/document/element queries
  - logical tree
  - visual tree
  - overlay snapshot
  - point hit testing
  - property updates
  - insert/remove
  - undo/redo
  - workspace mode
  - hit-test mode
  - property filter mode
- Start the design server in the preview designer host on a session-local loopback port.
- Add preview-host client proxy support and session methods.
- Expose matching `axsg.preview.design.*` tools/resources from the preview-host MCP server.

### Phase 4. VS Code design client

- Add package contributions for the new design activity-bar container and views.
- Add a `DesignSessionController`.
- Add tree providers for documents/toolbox/logical tree/visual tree.
- Add a properties webview view that renders editable property groups and quick sets.
- Apply returned minimal diffs to the active XAML buffer instead of writing files directly.

### Phase 5. Synchronization and preview overlay

- Add preview toolbar controls for interactive/design mode and hit-test mode.
- Add hover/selection overlay rendering in the preview webview.
- Sync selection across:
  - preview
  - trees
  - properties
  - editor caret/selection
- Prevent feedback loops with origin/version guards.

### Phase 6. Verification

- Runtime tests for source ranges, live trees, and point hit testing.
- Preview host tests for design command routing.
- Extension tests for helper logic and state synchronization where feasible.
- Manual verification in VS Code for both `sourceGenerated` and `avalonia` preview modes.

## Design rules

- The XAML editor buffer is authoritative.
- Design mutations return minimal diffs and the extension applies them to the buffer.
- Preview refresh remains driven by the existing live-update path from editor text.
- No second VS Code-only semantic model is introduced.
- Preview size changes must not rebuild the preview session.

## Implemented

- Added transport-neutral preview design DTOs for source ranges, live trees, overlays,
  and hit-test results in `XamlToCSharpGenerator.Runtime.Core`.
- Extended runtime hot-design payload/query/mutation services so preview sessions can
  query workspace state, logical tree, visual tree, overlays, and perform
  selection/mutation operations through the existing Hot Design workspace.
- Added preview-only Hot Design bridge/runtime installer support so both preview
  compiler modes can project the active preview document into the shared runtime
  design workspace.
- Extended the preview host session/protocol/router and MCP server with
  `axsg.preview.design.*` tools and session-local resources for current workspace,
  selected document, selected element, logical tree, visual tree, and overlay.
- Added a new VS Code `AXSG Design` activity-bar container with:
  - Documents tree
  - Toolbox tree
  - Logical Tree view
  - Visual Tree view
  - Properties webview
- Added a shared `DesignSessionController` to synchronize preview, trees,
  properties, and editor selection, and to apply minimal text diffs back to the
  authoritative XAML editor buffer.
- Extended the existing preview webview with design-mode toolbar controls,
  hit-test-mode controls, and hover/selection overlays that reuse the existing
  fixed-size/zoom/centering math.

## Verification

- `node --check tools/vscode/axsg-language-server/design-support.js`
- `node --check tools/vscode/axsg-language-server/preview-support.js`
- `npm test` in `tools/vscode/axsg-language-server`
- `dotnet build src/XamlToCSharpGenerator.PreviewerHost/XamlToCSharpGenerator.PreviewerHost.csproj`
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~PreviewerHost"`

## Risks and mitigations

- Risk: preview and runtime workspace drift during invalid XAML.
  - Mitigation: keep last-known-good preview/workspace behavior and refresh once valid text returns.
- Risk: preview-only mutations succeed but visual tree lags until the editor update lands.
  - Mitigation: mutation flow immediately patches the editor buffer so the existing preview update path converges quickly.
- Risk: activity-bar panels can outlive preview sessions.
  - Mitigation: controller tracks active session lifecycle and clears state when preview stops.
