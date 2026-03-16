# MCP Full-Surface Follow-Up Plan

Date: 2026-03-16

Supersedes follow-up work from:

- `plan/121-unified-mcp-remote-api-plan-2026-03-16.md`

## Goal

Complete the remaining MCP feature surface so MCP is no longer just a transport/query adjunct, but a first-class remote API for:

- workspace analysis and refactoring queries
- runtime hot reload control and observability
- runtime hot design control, editing, and workspace interaction
- studio session control and remote editing flows
- preview lifecycle and live-update orchestration

This plan assumes the transport and host foundation from Plan 121 is already complete.

## Executive Summary

The shared JSON-RPC and MCP foundation is in place.

The main remaining gap is not protocol plumbing. It is feature exposure.

Current MCP coverage is strongest for:

- workspace preview project-context queries
- runtime status/doc/workspace snapshots
- runtime event subscriptions
- preview-host lifecycle and in-process preview hot reload

Current MCP coverage is weakest for:

- runtime mutations already available through hot design and studio APIs
- studio/session control flows
- hot reload control operations beyond status
- workspace-language tooling parity with the existing AXSG LSP custom surface

The next phase should therefore focus on shared mutation services first, then MCP adapters over those services.

## Current State

### Completed in the existing MCP work

Plan 121 is effectively complete for the original architecture scope:

- shared JSON-RPC transport layer
- workspace MCP host
- runtime MCP host
- preview-host MCP mode
- shared preview/studio protocol adapters
- runtime resource subscriptions
- runtime event resources
- preview lifecycle resources and notifications
- in-process preview hot reload over MCP

### Implemented hosts and strengths

#### Workspace MCP host

Implemented in:

- `src/XamlToCSharpGenerator.McpServer`

Currently exposes:

- `axsg.preview.projectContext`
- runtime-style read-only status resources/tools reused from `AxsgRuntimeMcpCatalog`

Good for:

- workspace preview resolution
- low-friction query access

Not yet good for:

- metadata-as-source queries
- inline C# projection queries
- cross-language navigation helpers
- rename planning / rename propagation helpers

#### Runtime MCP host

Implemented in:

- `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenRuntimeMcpServer.cs`

Currently exposes:

- hot reload status
- hot design status, documents, workspace
- studio status
- hot reload/hot design/studio event resources
- subscriptions and resource update notifications

Good for:

- snapshots
- live status/event observation

Not yet good for:

- driving hot design changes
- driving studio session control
- adjusting canvas/workspace/panel settings
- applying property/element/document edits
- undo/redo and selection workflows

#### Preview MCP host

Implemented in:

- `src/XamlToCSharpGenerator.PreviewerHost/PreviewHostMcpServer.cs`

Currently exposes:

- preview start
- preview update
- preview stop
- preview hot reload
- preview status/events/current-session resources

This is close to complete for the preview host itself.

The remaining preview-related work is mostly integration and documentation consistency, not large protocol gaps.

## Gap Analysis

## 1. Runtime MCP mutation gap

The runtime tooling APIs already support far more than MCP exposes today.

Existing runtime capabilities include:

- hot design enable/disable/toggle
- workspace mode and property-filter mode changes
- hit-test mode changes
- panel visibility and panel toggles
- canvas zoom, form factor, and theme changes
- document selection
- element selection
- document text apply
- property updates
- element insert/remove
- undo/redo
- studio enable/disable/configure/start/stop/apply-update

These capabilities already exist in:

- `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotDesignTool.cs`
- `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenStudioManager.cs`
- `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotDesignCoreTools.cs`

But MCP currently exposes mostly read-only snapshots.

That leaves a large feature mismatch between:

- runtime APIs
- studio TCP remote-design adapter
- MCP runtime host

## 2. Shared mutation-service gap

The query side already has transport-neutral services:

- `AxsgPreviewQueryService`
- `AxsgRuntimeQueryService`

There is no equivalent shared runtime mutation layer yet.

As a result, the remaining mutations would currently have to be wired directly into:

- MCP adapters
- studio TCP adapters
- any future runtime-attached clients

That would regress the architecture established in Plan 121.

We need shared mutation services first.

## 3. Workspace MCP feature-parity gap

The language server currently supports a much broader AXSG custom/editor surface than MCP exposes.

Important existing workspace-language capabilities include:

- metadata document generation
- inline C# projections
- C# to XAML references/declarations
- rename propagation queries
- refactor prepare-rename / rename
- standard navigation helpers such as hover/definition/references/document symbols

Not all LSP features should be mirrored verbatim into MCP, but the AXSG-specific reusable language-service operations should have MCP equivalents.

## 4. Resource-shape and dynamic-read gap

Current MCP resources are mostly fixed-URI snapshots.

Remaining useful MCP improvements include:

- explicit resource templates or equivalent parameterized read patterns for:
  - workspace snapshots by `buildUri`
  - filtered workspace snapshots
  - current document or selected element views
- richer studio/runtime projection resources
- clearer correlation between mutation tools and the snapshot resources they invalidate

## 5. Client and docs gap

The protocol foundation is documented, but once the remaining mutation/tool surface lands we will need:

- runtime MCP usage docs for hot design and studio control
- workspace MCP docs for language-service tools
- concrete examples for AI clients, CLI flows, and automation

## Design Principles For The Follow-Up

### Keep adapters thin

No new MCP tool should contain business logic that is not also reusable by:

- studio TCP adapters
- future in-process UI integrations
- tests

### Do not map MCP directly to LSP semantics

Reuse:

- shared transport
- shared services
- shared contracts where semantically appropriate

Do not reuse:

- raw LSP method names
- editor lifecycle assumptions
- token-stream semantics that do not fit MCP tool/resource workflows

### Prefer tool-oriented language-service exposure

For MCP, expose stable tool/resource semantics such as:

- “get metadata document”
- “get inline C# projection”
- “find XAML references for C# symbol”

Do not expose raw editor UI actions just because LSP happens to support them.

### Preserve runtime determinism and hot-reload safety

All runtime mutation tools must respect existing reliability rules:

- last-known-good behavior on failure
- minimal-diff semantics where already implemented
- deterministic apply/revert behavior
- no reflection/dynamic fallback in production runtime paths

## Target End State

At plan completion:

- runtime MCP can both inspect and drive hot design/studio flows
- preview MCP is feature-complete for session lifecycle and live updates
- workspace MCP exposes the key AXSG-specific language-service operations now only available through LSP custom requests
- shared query and mutation services sit underneath all adapters
- MCP docs clearly tell clients what to poll, what to subscribe to, and what to mutate

## Phased Plan

## Phase A. Shared Runtime Mutation Services

Introduce transport-neutral runtime mutation services parallel to `AxsgRuntimeQueryService`.

Status: complete.

Add:

- `AxsgRuntimeHotDesignService`
- `AxsgRuntimeStudioService`
- `AxsgRuntimeHotReloadService`

Responsibilities:

- validate and normalize arguments
- call `XamlSourceGenHotDesignTool`, `XamlSourceGenStudioManager`, and related runtime APIs
- shape stable typed results for adapters
- centralize correlation IDs, request IDs, and snapshot invalidation hints

Do not:

- put JSON parsing in these services
- put transport-specific error formatting in these services

Deliverables:

- shared C# service layer
- typed request/result DTOs if existing runtime types are too adapter-specific
- tests for service behavior independent of MCP/TCP

## Phase B. Runtime MCP Hot Design Control Surface

Expose hot design mutations over MCP using the shared runtime mutation services.

Status: complete.

Initial MCP tools to add:

- `axsg.hotDesign.enable`
- `axsg.hotDesign.disable`
- `axsg.hotDesign.toggle`
- `axsg.hotDesign.selectDocument`
- `axsg.hotDesign.selectElement`
- `axsg.hotDesign.applyDocumentText`
- `axsg.hotDesign.applyPropertyUpdate`
- `axsg.hotDesign.insertElement`
- `axsg.hotDesign.removeElement`
- `axsg.hotDesign.undo`
- `axsg.hotDesign.redo`
- `axsg.hotDesign.setWorkspaceMode`
- `axsg.hotDesign.setPropertyFilterMode`
- `axsg.hotDesign.setHitTestMode`
- `axsg.hotDesign.togglePanel`
- `axsg.hotDesign.setPanelVisibility`
- `axsg.hotDesign.setCanvasZoom`
- `axsg.hotDesign.setCanvasFormFactor`
- `axsg.hotDesign.setCanvasTheme`

Resource follow-up in this phase:

- make it explicit which resources are invalidated by each tool
- ensure `notifications/resources/updated` fire for affected snapshots

Acceptance for Phase B:

- everything currently drivable through hot design public tools is reachable through MCP
- event/status resources update correctly after mutations

## Phase C. Runtime MCP Studio Control Surface

Expose studio/session operations over MCP.

Status: complete.

Initial MCP tools to add:

- `axsg.studio.enable`
- `axsg.studio.disable`
- `axsg.studio.configure`
- `axsg.studio.startSession`
- `axsg.studio.stopSession`
- `axsg.studio.applyUpdate`
- `axsg.studio.scopes`

Integration requirement:

- route studio TCP server and MCP through the same shared runtime services where operations overlap
- avoid duplicating validation between TCP and MCP adapters

Acceptance for Phase C:

- studio session lifecycle is fully observable and controllable over MCP
- studio TCP and MCP are aligned semantically for overlapping operations

## Phase D. Runtime MCP Hot Reload Control Surface

The current hot reload MCP surface is too shallow.

Status: complete.

Evaluate and expose the safe control operations supported by the runtime:

- `axsg.hotReload.enable`
- `axsg.hotReload.disable`
- `axsg.hotReload.toggle`
- `axsg.hotReload.trackedDocuments`
- `axsg.hotReload.remoteTransportStatus`
- `axsg.hotReload.lastOperation`

If control APIs do not exist yet in a reusable form, add shared runtime services first and keep the tool surface limited to what is stable.

Acceptance for Phase D:

- hot reload MCP is more than a status snapshot
- resource/event snapshots and tools line up around the actual runtime capabilities

## Phase E. Workspace MCP Language-Service Surface

Add MCP tools for the reusable AXSG workspace-language operations already implemented behind LSP.

Status: complete.

Priority tools:

- `axsg.workspace.metadataDocument`
- `axsg.workspace.inlineCSharpProjections`
- `axsg.workspace.csharpReferences`
- `axsg.workspace.csharpDeclarations`
- `axsg.workspace.renamePropagation`
- `axsg.workspace.prepareRename`
- `axsg.workspace.rename`

Optional later read/query tools if they remain useful in MCP form:

- `axsg.workspace.hover`
- `axsg.workspace.definition`
- `axsg.workspace.references`
- `axsg.workspace.documentSymbols`

Important rule:

Only add MCP tools for operations that make sense outside an editor event loop.

Do not attempt to mirror:

- completion streaming
- semantic-token editor paint semantics

unless a clear MCP client use case appears.

Acceptance for Phase E:

- all major AXSG-specific LSP custom operations have transport-neutral implementations and MCP equivalents

## Phase F. Resource Templates And Snapshot Specialization

Status: complete. Implemented focused fixed resources plus dynamic per-build workspace resources, using the existing MCP dynamic resource catalog and `resources/list_changed` flow instead of introducing a separate resource-template protocol layer.

Once mutation tools exist, expand resources so clients can efficiently resync local state.

Candidate additions:

- parameterized workspace snapshot reads by `buildUri`
- selected document / selected element resources
- per-session preview current-state projections
- studio scope/resources snapshots

Preferred approach:

- use MCP resource templates if they improve client ergonomics
- otherwise keep tool-based reads but standardize the pattern consistently

Acceptance for Phase F:

- clients can refresh focused state without rereading every coarse snapshot

## Phase G. Documentation, Samples, And Client Guidance

Update docs after the mutation/tool surface is in place.

Required docs:

- runtime MCP guide for hot design control
- runtime MCP guide for studio control
- workspace MCP guide for language-service tools
- examples showing:
  - subscribe-then-mutate flows
  - preview-host MCP hot reload
  - runtime hot design apply/edit flows
  - workspace query flows

Acceptance for Phase G:

- all newly added MCP tools/resources are documented with sample inputs/outputs and usage guidance

## Delivery Order

Recommended implementation order:

1. Phase A
2. Phase B
3. Phase C
4. Phase D
5. Phase E
6. Phase F
7. Phase G

Reason:

- runtime mutation-service extraction is the prerequisite for clean adapter work
- hot design and studio mutations give the highest user-facing value
- hot reload control is narrower and should be aligned after the shared runtime control layer exists
- workspace-language parity is important, but independent enough to follow once runtime mutation architecture is stable

## Testing Plan

### Unit tests

Add focused tests for:

- shared runtime mutation services
- argument normalization and validation
- correlation and result shaping
- snapshot invalidation decisions

### Integration tests

Add MCP integration coverage for:

- runtime mutation tools
- runtime resource update notifications after mutations
- studio lifecycle tools
- workspace-language tools

### Adapter parity tests

For overlapping operations, verify parity between:

- MCP runtime host
- studio TCP adapter
- shared runtime services

### Regression tests

Protect against:

- adapter-only business logic
- stale resources after mutation
- list_changed / resources_updated gaps
- mutation results not matching subsequent resource snapshots

## Acceptance Criteria

This follow-up plan is complete when:

- all stable runtime hot design mutations are available through MCP
- studio lifecycle/control flows are available through MCP
- hot reload MCP exposes the agreed stable control/read surface beyond status only
- all major AXSG-specific language-service operations with MCP value have MCP equivalents
- shared runtime mutation services sit underneath MCP and other adapters
- docs and examples cover the full MCP surface

## Non-Goals

This plan does not require:

- replacing LSP with MCP for editor-native behaviors
- exposing every LSP request as an MCP tool
- changing the underlying hot reload socket transport in this phase
- inventing new runtime features solely to satisfy MCP parity

## Risks

### Overexposing editor semantics

Risk:

- copying LSP 1:1 into MCP would create awkward tools and weak client ergonomics

Mitigation:

- expose only stable, tool-oriented operations

### Adapter drift

Risk:

- MCP and studio TCP could diverge semantically if mutations are added directly in adapters

Mitigation:

- require shared mutation services first

### Resource invalidation complexity

Risk:

- richer mutation tools increase the chance of stale snapshot resources

Mitigation:

- define invalidation/update rules per tool and cover with tests

### Runtime stability regressions

Risk:

- new control tools could disturb hot reload/hot design behavior if they bypass existing safe paths

Mitigation:

- only call existing runtime public/core tools through thin shared services
- preserve existing last-known-good and deterministic apply behavior

## Immediate Next Step

Start with Phase A:

- extract shared runtime mutation services
- move existing studio TCP mutation handling onto those services
- then expose the first hot design mutation tools in the runtime MCP host
