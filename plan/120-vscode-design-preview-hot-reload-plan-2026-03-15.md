# 120) VS Code Design Preview Hot Reload Plan (2026-03-15)

## Decision Summary

Yes, but the work splits into two distinct capabilities:

1. Source-generated preview hot reload on save/build without restarting the preview process is feasible with the current runtime architecture and should be the first delivery.
2. Source-generated live preview for unsaved editor text is also feasible, but it is not a simple extension-side change. It requires a new in-memory design-apply contract in the AXSG runtime/design host because the current hot-design fallback callback does not receive updated XAML text.

The existing Avalonia/XamlX preview path already supports unsaved live updates through Avalonia's `UpdateXaml` protocol. The real gap is AXSG source-generated preview parity.

## Current State

### VS Code preview modes

1. Avalonia mode:
   - Uses Avalonia's designer transport.
   - Unsaved XAML edits are pushed directly with `UpdateXaml`.
   - Does not exercise AXSG source-generated runtime behavior.

2. Source-generated mode:
   - Uses the bundled AXSG designer host and loads the last successful build.
   - Refreshes by rebuilding and restarting the preview process.
   - Does not currently participate in AXSG hot reload or hot design runtime apply.

### Runtime hot reload and hot design infrastructure already present

The repo already has the runtime pieces needed for a proper design-preview hot reload architecture:

1. `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotReloadManager.cs`
   - Metadata update entry point.
   - IDE polling fallback for source-path changes.
   - Build URI and source-path registration.
   - Mirrored registration into Hot Design.

2. `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotDesignManager.cs`
   - Document registration and document-scoped apply pipeline.
   - Source persistence, minimal diff persistence, hot-reload waiting, and fallback policies.

3. `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotDesignCoreTools.cs`
   - Document-oriented editing helpers and runtime tool entry points.

### Primary gap

The current hot-design runtime fallback path can call a runtime apply action per tracked instance, but that callback only has the shape `Action<object>`. It does not receive the updated XAML text. That means:

1. save/build-driven hot reload can reuse the existing hot reload manager;
2. unsaved in-memory AXSG preview cannot be implemented cleanly with the current contract.

## Source Analysis

### Current preview implementation reviewed

1. `tools/vscode/axsg-language-server/preview-support.js`
2. `src/XamlToCSharpGenerator.PreviewerHost/PreviewSession.cs`
3. `src/XamlToCSharpGenerator.PreviewerHost/Protocol/AvaloniaDesignerTransport.cs`
4. `src/XamlToCSharpGenerator.Previewer.DesignerHost/Program.cs`
5. `src/XamlToCSharpGenerator.Previewer.DesignerHost/SourceGeneratedRuntimeXamlLoaderInstaller.cs`
6. `src/XamlToCSharpGenerator.Previewer.DesignerHost/PreviewSizingRootDecorator.cs`

### Current hot reload / hot design runtime reviewed

1. `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotReloadManager.cs`
2. `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotDesignManager.cs`
3. `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotDesignCoreTools.cs`
4. `src/XamlToCSharpGenerator.Runtime.Core/SourceGenHotDesignUpdateContext.cs`
5. `src/XamlToCSharpGenerator.Runtime.Core/ISourceGenHotDesignUpdateApplier.cs`
6. `src/XamlToCSharpGenerator.Runtime.Core/SourceGenHotDesignOptions.cs`

## Recommended Architecture

## Phase A: Save/Build Hot Reload for Source-Generated Preview

### Goal

When the user saves a XAML file and the AXSG runtime can refresh the affected artifact, the running preview process should update in place instead of being torn down and restarted.

### Design

1. Keep the source-generated preview host process alive across saves.
2. Ensure the preview host explicitly enables AXSG hot reload and IDE polling fallback, even if the sample app startup path does not do it consistently.
3. Register the previewed root document with:
   - tracking type,
   - build URI,
   - source path,
   - runtime reload action,
   - artifact refresh registration.
4. When a saved source file changes, let `XamlSourceGenHotReloadManager` drive the reload pipeline inside the running preview host.
5. The VS Code helper should stop treating save refresh as a mandatory full restart. Instead it should:
   - trigger the build when needed,
   - wait for a hot-reload completion event from the preview host,
   - restart only on timeout or explicit hot-reload failure.

### Why this is the first step

1. It reuses the existing AXSG runtime contracts.
2. It gives true source-generated hot reload semantics.
3. It removes the largest UX problem in source-generated preview without requiring a new in-memory compiler/apply pipeline.

## Phase B: In-Memory Live Design Apply for Unsaved Source-Generated Preview

### Goal

Support unsaved editor changes in source-generated preview without falling back to Avalonia/XamlX preview mode.

### Design

1. Add a new runtime design-apply contract that can carry updated XAML text into a tracked preview instance.
2. Keep the current `Action<object>` fallback for existing studio/runtime compatibility.
3. Add a preview-specific in-memory applier in the AXSG designer host that:
   - receives the updated XAML text,
   - applies it on the UI thread,
   - replaces or updates the preview root,
   - preserves `DataContext`, viewport sizing, DPI, and document identity when possible,
   - keeps last-known-good behavior on invalid XAML.
4. Route unsaved VS Code `update` operations to this in-memory apply path when source-generated live design apply is supported.

### Required contract change

This phase needs a new runtime abstraction. A minimal shape is:

1. extend hot-design registration with an optional live-update delegate or service;
2. pass `context.Request.XamlText` into that live-update path;
3. keep file-system persistence and wait-for-hot-reload as separate policy choices.

### Important constraint

This phase must not introduce reflection or ad-hoc string heuristics into emitted/runtime hot paths. The preview-only designer host may remain a design-time boundary, but the reusable runtime contract should stay strongly typed and AOT-safe.

## Phase C: Shared Preview/Studio Design Pipeline

### Goal

Avoid maintaining two unrelated design-update systems: one for Studio and one for VS Code preview.

### Design

1. Factor the common document update orchestration into reusable runtime services.
2. Share:
   - document identity (`BuildUri`, `SourcePath`),
   - minimal diff metadata,
   - last-known-good behavior,
   - apply result/status contracts,
   - diagnostics and tracing.
3. Keep the VS Code preview UI lightweight; do not embed the full Studio tree/property/toolbox UX into the preview webview.

## File-Level Plan

### VS Code extension

1. `tools/vscode/axsg-language-server/preview-support.js`
   - add source-generated hot-reload session states and wait logic;
   - stop forcing process restart on every save when hot reload is active;
   - keep restart as fallback on timeout/error.

2. `tools/vscode/axsg-language-server/preview-utils.js`
   - add preview capability resolution and hot-reload mode planning.

### Preview helper

1. `src/XamlToCSharpGenerator.PreviewerHost/Program.cs`
   - add protocol commands/events for hot-reload status and capability reporting.

2. `src/XamlToCSharpGenerator.PreviewerHost/PreviewSession.cs`
   - keep the preview host alive for source-generated hot reload;
   - wait for hot-reload completion instead of unconditional restart;
   - surface timeout/failure diagnostics.

### AXSG designer host

1. `src/XamlToCSharpGenerator.Previewer.DesignerHost/Program.cs`
   - enable AXSG hot reload / hot design services for preview-host sessions.

2. `src/XamlToCSharpGenerator.Previewer.DesignerHost/SourceGeneratedRuntimeXamlLoaderInstaller.cs`
   - register previewed roots with the hot reload / hot design bridge.

3. New designer-host service, for example:
   - `PreviewHotReloadBridge.cs`
   - `PreviewDocumentRegistration.cs`
   - `PreviewLiveUpdateApplier.cs`

### Runtime contracts

1. `src/XamlToCSharpGenerator.Runtime.Core/SourceGenHotDesignUpdateContext.cs`
   - extend the runtime apply surface so in-memory apply can consume updated XAML text.

2. `src/XamlToCSharpGenerator.Runtime.Core/SourceGenHotDesignRegistrationOptions.cs`
   - add an optional live-update contract for tracked preview roots.

3. `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotDesignManager.cs`
   - choose between:
     - file persistence + wait for hot reload,
     - preview in-memory live apply,
     - existing runtime fallback.

4. `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotReloadManager.cs`
   - ensure preview-host registration path is supported and test-covered.

## Protocol and UX Plan

1. Preserve the current Avalonia live-preview path unchanged.
2. For source-generated preview:
   - on save/build, prefer in-process hot reload;
   - if hot reload is unavailable or times out, fall back to restart;
   - surface the chosen path in the AXSG output channel.
3. Add explicit status text such as:
   - `Waiting for source-generated hot reload...`
   - `Source-generated hot reload applied.`
   - `Source-generated hot reload timed out; restarting preview.`

## Risks and Constraints

1. Some host apps may not consistently enable AXSG hot reload during preview startup.
2. IDE polling fallback depends on source-path registration and artifact refresh coverage.
3. Unsaved live source-generated preview is not achievable with the current runtime apply delegate shape.
4. A preview-only in-memory apply path must preserve last-known-good behavior when the XAML is transiently invalid.
5. Preview reloads must preserve viewport size and DPI state so hot reload does not regress rendering fidelity.

## Test Plan

### Runtime tests

1. Preview-host registration mirrors documents into hot design with correct `BuildUri` and `SourcePath`.
2. IDE polling fallback updates the running preview instance without process restart.
3. Hot reload timeout falls back to restart cleanly.
4. Invalid edits keep the last known good preview surface.

### Designer-host tests

1. Source-generated preview root survives in-process hot reload and preserves `DataContext`.
2. Preview width/height/DPI remain correct after hot reload.
3. Preview registration is cleaned up when the session stops.

### Helper / extension tests

1. Source-generated save refresh does not restart when hot reload succeeds.
2. Restart fallback triggers only on timeout/failure.
3. Unsaved source-generated live updates remain disabled until the in-memory apply phase is implemented.

### Manual validation

1. Open source-generated preview.
2. Edit and save a tracked XAML file.
3. Verify preview updates without preview-session teardown.
4. Verify output channel shows hot-reload status rather than restart status.
5. Move VS Code between displays and confirm size/DPI remain correct after subsequent hot reloads.

## Acceptance Criteria

1. Source-generated preview updates on save/build without full process restart when AXSG hot reload succeeds.
2. Restart remains only as a deterministic fallback on timeout or failure.
3. Preview process preserves document identity, size, and DPI through hot reload.
4. Existing Avalonia/XamlX live preview behavior remains unchanged.
5. A follow-up phase enables unsaved source-generated live preview through a dedicated in-memory design-apply contract.
