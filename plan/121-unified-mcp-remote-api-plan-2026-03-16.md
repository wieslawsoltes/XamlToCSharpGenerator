# Unified MCP / Remote API Plan

Date: 2026-03-16

## Goal

Provide one shared remote API surface for:

- hot reload runtime control and status
- hot design runtime control and workspace inspection
- VS Code preview orchestration
- VS Code extension workspace queries

The shared API must avoid duplicating transport logic between LSP, preview helper RPC, studio remote design RPC, and a new MCP server.

## Short Answer

Yes, MCP support is feasible.

Yes, we should reuse the existing LSP transport work, but only at the JSON-RPC transport layer.

No, we should not treat MCP as “LSP with different method names”.

Reason:

- LSP and MCP both use JSON-RPC 2.0 and the same `Content-Length` framed stdio transport pattern.
- LSP method semantics are editor-document oriented.
- MCP method semantics are tool/resource/prompt oriented.
- Reusing framing, serialization, request/response helpers, cancellation plumbing, and dispatcher patterns is good.
- Reusing LSP request names, lifecycle assumptions, or editor-specific payloads would be the wrong abstraction.

## Current State

The repository currently has four separate remote/control surfaces:

1. Language server:
   - `src/XamlToCSharpGenerator.LanguageServer`
   - Uses custom LSP framing classes and `axsg/*` custom requests.

2. VS Code preview helper:
   - `src/XamlToCSharpGenerator.PreviewerHost`
   - Uses custom JSON-line request/response commands.

3. Studio remote design server:
   - `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenStudioRemoteDesignServer.cs`
   - Uses custom TCP line-delimited JSON commands.

4. Hot reload remote transport:
   - `src/XamlToCSharpGenerator.Runtime.Avalonia/RemoteSocketTransport.cs`
   - Uses a custom socket protocol for remote apply operations.

The duplication is not only transport duplication. It also duplicates:

- request routing
- error shaping
- capability discovery
- status querying
- operation naming

## Target Architecture

Introduce a unified AXSG remote operation layer with protocol adapters on top.

### 1. Shared operation layer

This is the real API surface.

It owns stable operation names and typed request/response contracts for:

- workspace / language operations
- preview operations
- hot reload runtime operations
- hot design / studio operations

This layer should not know whether the caller is LSP, MCP, VS Code, or a direct TCP client.

### 2. Shared JSON-RPC transport layer

This is reusable between LSP and MCP.

Responsibilities:

- `Content-Length` framing
- JSON serialization options
- JSON node cloning / result shaping
- request and response helpers

This transport layer should be transport-only. It should not encode LSP or MCP semantics.

### 3. Protocol adapters

- LSP adapter
  - maps editor-centric requests to shared operations
  - preserves existing `axsg/*` requests for VS Code compatibility

- MCP adapter
  - maps MCP `tools/*` and `resources/*` requests to the same shared operations
  - exposes AXSG capabilities to AI agents and MCP clients

- legacy adapters
  - preview helper JSON-line RPC
  - studio remote design TCP JSON
  - these should migrate to the shared operation layer progressively

## Unified API Shape

The shared API should be grouped by capability, not by transport.

### Workspace capability

- resolve preview project context
- query symbol/navigation metadata
- later: rename/cross-language refactoring entry points

### Preview capability

- resolve preview launch inputs
- start/update/stop sessions
- later: in-process source-generated refresh
- later: preview diagnostics / live session state

### Hot reload capability

- get runtime status
- query registered build URIs / tracked documents
- apply remote update requests
- later: subscribe to pipeline events

### Hot design capability

- get status
- list registered documents
- query workspace snapshot
- apply update / select document / select element

## MCP Surface

The MCP server should expose:

- tools
  - best for operations and mutations
- resources
  - best for read-only snapshots and status

Initial MCP tool set:

- `axsg.preview.projectContext`
- `axsg.hotReload.status`
- `axsg.hotDesign.status`
- `axsg.hotDesign.documents`
- `axsg.hotDesign.workspace`
- `axsg.studio.status`

Initial MCP resources:

- `axsg://runtime/hotreload/status`
- `axsg://runtime/hotdesign/status`
- `axsg://runtime/hotdesign/documents`
- `axsg://runtime/studio/status`

## Migration Strategy

### Phase 0. Shared transport foundation

- Extract generic JSON-RPC reader/writer and JSON node helpers from the language server.
- Keep `LspMessageReader` / `LspMessageWriter` as thin compatibility wrappers.

### Phase 1. MCP server bootstrap

- Add `src/XamlToCSharpGenerator.McpServer`.
- Support:
  - `initialize`
  - `notifications/initialized`
  - `tools/list`
  - `tools/call`
  - `resources/list`
  - `resources/read`
- Wire initial read/query tools to existing managers and language service.

### Phase 2. Shared operation services

- Extract shared operation handlers from:
  - language server custom `axsg/*` handlers
  - studio remote design server command handlers
  - preview helper orchestration
- Make LSP and MCP thin adapters over the same handler services.

### Phase 3. Preview helper migration

- Replace JSON-line preview helper command routing with shared operation handlers.
- Keep helper transport lightweight, but stop duplicating request contracts.

### Phase 4. Studio remote design migration

- Replace ad-hoc line JSON command names with shared operation handlers.
- Either:
  - keep TCP transport and map it to shared operations, or
  - replace it with JSON-RPC framed transport.

### Phase 5. Runtime-attached MCP mode

- Allow an Avalonia app or preview host to host the same MCP operation catalog in-process.
- This is the mode that gives AI clients live runtime visibility into active hot reload / hot design state.
- Status: complete.

### Phase 6. Event subscriptions

- Add MCP resource refresh or tool polling guidance first.
- Implement `resources/subscribe` and `notifications/resources/updated` for the runtime host.
- Start with runtime snapshots that already map cleanly to resources:
  - hot reload status
  - hot design status
  - hot design documents
  - studio status
- Later evaluate MCP sampling / streaming patterns for:
  - hot reload pipeline events
  - studio status changes
  - preview lifecycle state
- Status: first slice complete for runtime resource subscriptions.

## Important Constraint

A standalone workspace MCP tool process can answer workspace queries immediately, but it cannot see live runtime state from a different running app process unless we add a runtime-attached bridge.

So the architecture must support two host modes:

- workspace MCP host
  - language service and preview orchestration
- runtime MCP host
  - in-process hot reload / hot design / studio state

Both should expose the same operation catalog where possible.

## Initial Implementation In This Change

This change now completes the first six migration slices, including the initial runtime-attached subscription surface.

Included:

1. Shared JSON-RPC transport project.
2. Language server migration to shared transport helpers.
3. New MCP server host with initial read/query tools and resources.
4. Hot reload runtime status snapshot API needed by MCP.
5. Shared operation query services consumed by both LSP and MCP adapters.
6. Preview helper migration to shared preview request/response contracts and a transport-neutral command router.
7. Studio remote-design TCP migration to shared request/response contracts and a transport-neutral runtime command router.
8. Runtime-attached MCP hosting for in-process hot reload / hot design / studio queries.
9. MCP runtime resource subscriptions and `notifications/resources/updated` for live status/doc refresh.

Not yet included:

- in-process preview hot reload over MCP

## Validation Plan

- unit test shared JSON-RPC writer behavior via the existing LSP transport tests
- add MCP server integration tests for:
  - initialize
  - tools/list
  - tools/call for runtime status
  - tools/call for preview project context
- add hot reload status snapshot tests

## Acceptance Criteria

This phase is complete when:

- LSP and MCP share the same JSON-RPC framing implementation
- the repo contains a working MCP server tool
- MCP can query preview project context and runtime hot design / studio / hot reload status
- existing LSP tests keep passing

## Follow-On Work

After this phase, the next highest-value step was Phase 7:

- extend subscriptions beyond status/doc snapshots into richer runtime event streams
- evaluate whether preview lifecycle should use MCP notifications, resource templates, or a dedicated runtime event resource
- add explicit client guidance for polling vs subscriptions per capability

## Current Phase 7 Status

Implemented now:

- runtime MCP event resources for:
  - `axsg://runtime/hotreload/events`
  - `axsg://runtime/hotdesign/events`
  - `axsg://runtime/studio/events`
- runtime `notifications/resources/updated` for those event resources
- bounded recent-event snapshots backed by live runtime manager/event-bus subscriptions
- preview-host MCP mode over JSON-RPC stdio
- preview lifecycle resources:
  - `axsg://preview/session/status`
  - `axsg://preview/session/events`
  - dynamic `axsg://preview/session/current`
- preview-host `resources/subscribe` and `notifications/resources/updated` for preview lifecycle resources
- `notifications/resources/list_changed` support and dynamic resource catalog changes
- tool list-change notifications via `notifications/tools/list_changed`
- explicit host instructions telling clients when to poll versus subscribe

Current status:

- in-process preview hot reload over MCP is now implemented via preview-host MCP `axsg.preview.hotReload`
- preview hot reload uses the same transport-neutral preview session/router layer as the lightweight helper transport
- preview session status/events resources remain the subscription surface for request/completion lifecycle
