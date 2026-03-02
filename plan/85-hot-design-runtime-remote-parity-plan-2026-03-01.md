# 85) Hot Design Runtime + Remote Parity Plan (2026-03-01)

## Objective

Raise SourceGen Studio to production-level live design parity for desktop + mobile by hardening:

1. Hit testing precision (logical default, visual optional).
2. Selection and adorner coherence.
3. Element tree fidelity (hierarchical, runtime-aware, not flat).
4. Property editor depth (metadata-rich, grouped, editor hints).
5. Toolbox quality (framework + project controls, categorized and searchable).
6. Mobile iOS/Android Studio startup integration.
7. Full remote design transport with VNC endpoint coordination for desktop-to-mobile live editing.

## Source Analysis

### Upstream runtime patterns reviewed

1. `/Users/wieslawsoltes/GitHub/uno/src/Uno.UI.RemoteControl/HotReload/ClientHotReloadProcessor.ClientApi.cs`
2. `/Users/wieslawsoltes/GitHub/uno/src/Uno.UI.RemoteControl/HotReload/ClientHotReloadProcessor.Common.Status.cs`
3. `/Users/wieslawsoltes/GitHub/uno/src/Uno.UI.RemoteControl/HotReload/WindowExtensions.cs`
4. `/Users/wieslawsoltes/GitHub/uno/src/Uno.UI.RemoteControl.Server.Processors/HotReload/ServerHotReloadProcessor.cs`

### Current implementation reviewed

1. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenStudioOverlayView.cs`
2. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenStudioShellViewModel.cs`
3. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotDesignCoreTools.cs`
4. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenStudioHost.cs`
5. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenStudioManager.cs`
6. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime.Avalonia/RemoteSocketTransport.cs`
7. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotReloadManager.cs`

## Gap Matrix

| Area | Current state | Gap | Required change |
| --- | --- | --- | --- |
| Hit testing | Manual bounds/depth heuristic in overlay | Precision drift under transforms/clipping and templated visuals | Use renderer-backed hit-test API + deterministic selection mapping service |
| Selection | Name/type matching with fallback | Ambiguity in deep trees, weaker source mapping | Introduce explicit selection resolver with ranked identity chain |
| Element tree | XAML AST-only tree in workspace snapshot | Can collapse to root-only for runtime-generated/template-heavy visual content | Add live runtime tree snapshot service (logical default / visual optional) |
| Properties | Basic list with limited metadata | Not editor-grade for large surfaces | Enrich metadata (owner, category, editor kind, enum options, read-only/reset hints) |
| Toolbox | Mostly static baseline list | Missing dynamic framework/project inventory quality | Dynamic typed catalog + categorized templates + tags |
| Mobile integration | Hot reload hooks present; studio hooks incomplete for iOS/Android | No first-class Studio startup path from env | Add app-builder/env helper + wire ControlCatalog mobile entry points |
| Remote design | Remote transport exists for hot reload apply operations | No dedicated remote studio command channel/workspace projection | Add Studio remote command server and JSON protocol |
| VNC mode | No explicit coordination surface | Cannot pair remote edit command channel with frame-transport endpoint | Add VNC endpoint contract + status propagation + UI exposure |

## Architecture Changes

1. Add dedicated runtime services for hit testing, live tree projection, and selection resolution (single responsibility per service).
2. Keep XAML source tree and runtime tree as separate models, expose runtime tree as primary canvas tree when available.
3. Extend Studio status contracts to include remote endpoint and VNC metadata.
4. Start/stop remote design server from Studio host lifecycle.
5. Integrate mobile builders through explicit environment-driven startup helper.

## Execution Phases

## Phase A - Hit testing + selection core

1. Add `XamlSourceGenStudioHitTestingService` using Avalonia visual hit-test APIs.
2. Add `XamlSourceGenStudioSelectionResolver` with ranked identity resolution and deterministic tie-breaks.
3. Replace overlay pointer selection path with service-based path.
4. Add guard tests for logical vs visual mode hit-selection behavior.

## Phase B - Live element tree pipeline

1. Add runtime tree projection service (logical-tree default, visual-tree optional).
2. Extend element-node contract with runtime/source mapping metadata.
3. Feed projected runtime tree into Studio shell view model and tree UI.
4. Preserve source-edit operations by mapping live node -> source element id.

## Phase C - Properties + toolbox depth

1. Extend property entry metadata (editor hints, enum options, owner/read-only/reset).
2. Improve property ordering/grouping for large control surfaces.
3. Build dynamic toolbox from known/project control inventory with category + snippet strategy.
4. Add tests for metadata correctness and toolbox population.

## Phase D - Remote design + VNC contract

1. Add Studio remote status contract in Runtime.Core.
2. Implement Studio remote TCP JSON command server:
   - `ping`
   - `getStatus`
   - `getWorkspace`
   - `selectDocument`
   - `selectElement`
   - `applyDocumentText`
3. Add VNC endpoint metadata in options/status so remote clients can pair design commands with frame transport.
4. Wire server lifecycle into `XamlSourceGenStudioHost`.

## Phase E - iOS/Android integration

1. Add app-builder helper that enables Studio from environment in all platforms.
2. Wire helper into:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/ControlCatalog.iOS/AppDelegate.cs`
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/ControlCatalog.Android/MainActivity.cs`
3. Support environment configuration for remote host/port and VNC endpoint.
4. Update iOS documentation with remote-design and VNC workflow.

## Phase F - Validation

1. Focused runtime tests for:
   - hit testing and selection mapping
   - live tree snapshot shape
   - remote server command protocol
   - mobile startup env wiring
2. Run focused test suites and report residual known blockers.

## Acceptance Criteria

1. Design mode selection uses logical tree by default; visual mode toggle behaves deterministically.
2. Canvas selection/adorner feedback is stable under scroll/transform/template content.
3. Element tree is hierarchical and non-flat for runtime content-heavy pages.
4. Property editor exposes owner/category/editor metadata and enum candidates.
5. Toolbox includes dynamic project controls and framework controls by category.
6. iOS/Android can enable Studio via env without code changes.
7. Remote command channel returns workspace/status and applies edits.
8. VNC endpoint metadata is exposed in Studio status and remote handshake payloads.

## Implementation Notes (2026-03-01)

### Implemented now

1. Runtime hit-testing and selection
   - Added `XamlSourceGenStudioHitTestingService` with renderer hit-testing (`GetVisualsAt`) and clipped bounds projection.
   - Added `XamlSourceGenStudioSelectionResolver` with ranked element matching and source-element resolution.
   - Updated overlay pointer handling and adorner bounds to use service APIs.

2. Live element tree projection
   - Added `XamlSourceGenStudioLiveTreeProjectionService` supporting logical-tree default and visual-tree optional projection.
   - Added live/source mapping fields on element node model:
     - `SourceBuildUri`
     - `SourceElementId`
     - `IsLive`
   - Studio shell now uses `DisplayElements` with runtime tree precedence when available.

3. Properties + toolbox metadata depth
   - Property metadata now includes:
     - `IsReadOnly`
     - `CanReset`
     - `EnumOptions`
   - Toolbox items now include `Tags`.
   - Metadata population wired in `XamlSourceGenHotDesignCoreTools`.

4. Remote design server
   - Added `XamlSourceGenStudioRemoteDesignServer`.
   - Implemented command protocol:
     - `ping`
     - `getStatus`
     - `getWorkspace`
     - `selectDocument`
     - `selectElement`
     - `applyDocumentText`
   - Added remote status contract:
     - `SourceGenStudioRemoteStatus`
     - `SourceGenStudioStatusSnapshot.Remote`
   - Host lifecycle now starts/stops remote server together with studio session.

5. VNC coordination
   - Added options:
     - `EnableRemoteDesign`
     - `RemoteHost`
     - `RemotePort`
     - `VncEndpoint`
     - `AutoOpenVncViewerOnDesktop`
   - Status payload includes VNC endpoint metadata.
   - Desktop host can auto-launch VNC endpoint when configured.

6. Mobile startup integration
   - Added `UseAvaloniaSourceGeneratedStudioFromEnvironment()` app-builder extension.
   - Wired mobile entry points:
     - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/ControlCatalog.iOS/AppDelegate.cs`
     - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/ControlCatalog.Android/MainActivity.cs`
   - Wired desktop entry point:
     - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/ControlCatalog.Desktop/Program.cs`

### Remote run configuration (environment)

Use these environment variables to enable remote mode:

- `AXSG_STUDIO_ENABLE=1`
- `AXSG_STUDIO_REMOTE_ENABLE=1`
- `AXSG_STUDIO_REMOTE_HOST=0.0.0.0`
- `AXSG_STUDIO_REMOTE_PORT=45831`
- `AXSG_STUDIO_VNC_ENDPOINT=vnc://127.0.0.1:5900` (optional)
- `AXSG_STUDIO_VNC_AUTO_OPEN=1` (desktop optional)

Optional behavior flags:

- `AXSG_STUDIO_OVERLAY_INDICATOR`
- `AXSG_STUDIO_EXTERNAL_WINDOW`
- `AXSG_STUDIO_AUTO_OPEN_WINDOW`
- `AXSG_STUDIO_TRACE`
- `AXSG_STUDIO_TIMEOUT_MS`
- `AXSG_STUDIO_WAIT_MODE`
- `AXSG_STUDIO_FALLBACK_POLICY`

### Validation status

1. `XamlToCSharpGenerator.Runtime.Avalonia` build: passing.
2. `ControlCatalog.Desktop` build: passing.
3. `ControlCatalog.iOS` build: passing.
4. Runtime test slice (`--filter FullyQualifiedName~Runtime`): passing (203/203).
5. `ControlCatalog.Android` build in this environment: blocked by missing Android workload (`NETSDK1147`).

### Validation extension (next phase completed)

Added focused guard tests for newly introduced runtime behaviors:

1. Environment-driven studio startup (`UseAvaloniaSourceGeneratedStudioFromEnvironment`)
   - file: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/AppBuilderExtensionsTests.cs`
   - verifies no-start when env is absent
   - verifies startup and remote option propagation when env vars are set

2. Studio manager remote status projection
   - file: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/XamlSourceGenStudioManagerTests.cs`
   - verifies enable-time remote snapshot initialization
   - verifies `UpdateRemoteStatus(...)` snapshot override behavior

3. Live tree projection service
   - file: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/XamlSourceGenStudioLiveTreeProjectionServiceTests.cs`
   - verifies hierarchical logical-tree projection
   - verifies source mapping resolution (`SourceBuildUri` / `SourceElementId`) for named live controls

Verification command:

- `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~AppBuilderExtensionsTests.UseAvaloniaSourceGeneratedStudioFromEnvironment|FullyQualifiedName~XamlSourceGenStudioManagerTests.Enable_With_RemoteDesign_Initializes_Remote_Status_Snapshot|FullyQualifiedName~XamlSourceGenStudioManagerTests.UpdateRemoteStatus_Overrides_Current_Remote_Status|FullyQualifiedName~XamlSourceGenStudioLiveTreeProjectionServiceTests" -v minimal`

Result: passing (6/6).

### Validation extension 2 (next phase completed)

Expanded remote command protocol guard coverage in runtime tests:

1. `getStatus` command payload validation:
   - verifies remote metadata projection (`isEnabled`, `isListening`, `port`, `vncEndpoint`) and option propagation.
2. `getWorkspace` command payload-shape validation:
   - verifies expected top-level workspace shape (`status`, `documents`, `elements`, `properties`, `toolbox`).
3. Request validation guards for command inputs:
   - `selectDocument` returns deterministic error when `buildUri` is missing.
   - `selectElement` returns deterministic error when `elementId` is missing.
   - `applyDocumentText` returns deterministic error when `buildUri` or `xamlText` is missing.

Files:

- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/XamlSourceGenStudioRemoteDesignServerTests.cs`

Verification commands:

- `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~XamlSourceGenStudioRemoteDesignServerTests"`
- `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~AppBuilderExtensionsTests|FullyQualifiedName~XamlSourceGenStudioRemoteDesignServerTests"`

Result: passing (8/8 remote protocol tests; 26/26 combined runtime env+remote slice).

### Validation extension 3 (next phase completed)

Added an end-to-end remote workflow success-path guard test:

1. Registers a real hot-design document with source path.
2. Runs remote command sequence over TCP:
   - `selectDocument`
   - `selectElement`
   - `applyDocumentText`
3. Verifies:
   - active document selection state is updated,
   - selected element id is propagated,
   - apply result reports success,
   - source file content is persisted with updated XAML.

Files:

- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/XamlSourceGenStudioRemoteDesignServerTests.cs`

Verification commands:

- `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~XamlSourceGenStudioRemoteDesignServerTests"`
- `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~AppBuilderExtensionsTests|FullyQualifiedName~XamlSourceGenStudioRemoteDesignServerTests"`

Result: passing (9/9 remote protocol tests; 27/27 combined runtime env+remote slice).

### Validation extension 4 (next phase completed)

Added focused hit-testing/selection guard coverage for deterministic logical/visual identity behavior and resolver ranking:

1. `XamlSourceGenStudioHitTestingService.CollectIdentityCandidates(...)`
   - verifies logical mode returns deterministic control -> ancestor identity ordering
   - verifies visual mode de-duplicates repeated names/types while preserving ranked order
2. `XamlSourceGenStudioSelectionResolver.TryFindBestMatchingElementNode(...)`
   - verifies name matches are ranked ahead of type-only matches
   - verifies deepest candidate wins for duplicate name matches

Files:

- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/XamlSourceGenStudioHitTestingSelectionTests.cs`

Verification commands:

- `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~XamlSourceGenStudioHitTestingSelectionTests"`
- `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~XamlSourceGenStudioLiveTreeProjectionServiceTests|FullyQualifiedName~XamlSourceGenStudioShellViewModelTests|FullyQualifiedName~XamlSourceGenStudioRemoteDesignServerTests|FullyQualifiedName~AppBuilderExtensionsTests.UseAvaloniaSourceGeneratedStudioFromEnvironment"`

Result: passing (4/4 hit-testing/selection tests; 24/24 combined Studio runtime slice).

### Validation extension 5 (next phase completed)

Hardened remote command payload handling for malformed request shapes while preserving session continuity:

1. `XamlSourceGenStudioRemoteDesignServer.TryGetString(...)`
   - now guards on `JsonElement.ValueKind == Object` before property access.
   - malformed `payload` values (for example string/array/null) now resolve through normal command validation instead of throwing and aborting the request loop.
2. Added regression test for non-object payload resilience:
   - sends malformed `selectDocument` payload (`payload` as string),
   - asserts deterministic validation error response,
   - sends follow-up `ping` on the same TCP connection and asserts success.

Files:

- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenStudioRemoteDesignServer.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/XamlSourceGenStudioRemoteDesignServerTests.cs`

Verification commands:

- `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~XamlSourceGenStudioRemoteDesignServerTests"`
- `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~XamlSourceGenStudioHitTestingSelectionTests|FullyQualifiedName~XamlSourceGenStudioLiveTreeProjectionServiceTests|FullyQualifiedName~XamlSourceGenStudioShellViewModelTests|FullyQualifiedName~XamlSourceGenStudioRemoteDesignServerTests|FullyQualifiedName~AppBuilderExtensionsTests.UseAvaloniaSourceGeneratedStudioFromEnvironment"`

Result: passing (16/16 remote protocol tests; 29/29 combined Studio runtime slice).

### Validation extension 6 (next phase completed)

Expanded remote workspace contract to include studio-remote endpoint metadata in `getWorkspace` responses:

1. `BuildWorkspacePayload(...)` now projects `SourceGenStudioStatusSnapshot.Remote` as a dedicated `remote` object:
   - `isEnabled`
   - `isListening`
   - `host`
   - `port`
   - `activeClientCount`
   - `lastError`
   - `vncEndpoint`
   - `updatedAtUtc`
2. Updated remote command handlers to pass current studio status snapshot when building workspace payloads for:
   - `getWorkspace`
   - `selectDocument`
   - `selectElement`
   - `applyDocumentText` (`payload.workspace`)
3. Added protocol regression test ensuring `getWorkspace` includes remote metadata with VNC endpoint propagation.

Files:

- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenStudioRemoteDesignServer.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/XamlSourceGenStudioRemoteDesignServerTests.cs`

Verification commands:

- `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~XamlSourceGenStudioRemoteDesignServerTests"`
- `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~XamlSourceGenStudioHitTestingSelectionTests|FullyQualifiedName~XamlSourceGenStudioLiveTreeProjectionServiceTests|FullyQualifiedName~XamlSourceGenStudioShellViewModelTests|FullyQualifiedName~XamlSourceGenStudioRemoteDesignServerTests|FullyQualifiedName~AppBuilderExtensionsTests.UseAvaloniaSourceGeneratedStudioFromEnvironment"`

Result: passing (17/17 remote protocol tests; 30/30 combined Studio runtime slice).
