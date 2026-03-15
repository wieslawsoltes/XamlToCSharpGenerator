# 120) VS Code Design Preview Hot Reload Plan (2026-03-15)

## Revision Summary

This plan has been revised after the recent source-generated preview work on `feature/avalonia-preview-split-view`.

The original draft assumed two major gaps:

1. source-generated preview was not the default mode;
2. unsaved source-generated XAML edits required a new runtime design-apply contract.

Those assumptions are no longer true for the VS Code preview boundary.

## Decision Summary

Yes, VS Code design preview already supports source-generated live preview for unsaved XAML edits.

What is now delivered:

1. `sourceGenerated` is the default VS Code preview mode.
2. `auto` also prefers source-generated preview when AXSG runtime output is present.
3. Unsaved editor text is pushed into source-generated preview sessions.
4. The source-generated designer host now loads the generated baseline first, then applies the current in-memory XAML as a live runtime overlay.
5. The live overlay keeps last-known-good behavior for transiently invalid edits.
6. Preview-only expression markup is supported through a dedicated preview runtime path.
7. Preview sessions already preserve VS Code viewport size and DPI state across startup/restart paths.

What remains to be implemented is narrower:

1. save/build refresh should stop restarting the source-generated preview process when AXSG hot reload can update the running preview in place;
2. preview-specific live overlay behavior should be converged with the shared Hot Design / Hot Reload runtime infrastructure where that reuse is actually beneficial.

## Current State

### VS Code preview modes

1. Source-generated mode:
   - Default mode in `tools/vscode/axsg-language-server/package.json`.
   - Unsaved XAML edits are pushed live.
   - Uses the bundled AXSG designer host.
   - Rebuild/save refresh still restarts the preview helper and designer host.

2. Auto mode:
   - Prefers source-generated preview when AXSG runtime output is available.
   - Falls back to Avalonia/XamlX only when source-generated preview is not supported by the built output.

3. Avalonia mode:
   - Keeps Avalonia's official XamlX previewer path unchanged.
   - Still uses Avalonia `UpdateXaml` directly.

### Delivered source-generated live preview architecture

The current source-generated live preview path works as follows:

1. `tools/vscode/axsg-language-server/preview-support.js`
   - sends current in-memory XAML text for source-generated preview updates;
   - passes the real source file path to the preview helper;
   - treats source-generated preview as a live preview mode rather than a save-only mode.

2. `src/XamlToCSharpGenerator.PreviewerHost/PreviewSession.cs`
   - forwards `--axsg-source-file` into the designer host;
   - keeps the current preview transport/session model unchanged.

3. `src/XamlToCSharpGenerator.Previewer.DesignerHost/Program.cs`
   - uses Avalonia's official `RemoteDesignerEntryPoint`;
   - installs the AXSG runtime loader override and preview markup runtime before Avalonia designer startup.

4. `src/XamlToCSharpGenerator.Previewer.DesignerHost/SourceGeneratedRuntimeXamlLoader.cs`
   - reads the active in-memory XAML document coming from Avalonia designer transport;
   - loads a clean AXSG generated baseline;
   - decides whether a live overlay is needed by comparing:
     - in-memory XAML text,
     - on-disk source file,
     - source file timestamp,
     - source assembly timestamp;
   - applies the in-memory XAML through `AvaloniaRuntimeXamlLoader.Load(...)` onto a fresh generated baseline;
   - falls back to the last-known-good unsaved overlay or the last successful build output on transient invalid edits.

5. `src/XamlToCSharpGenerator.Previewer.DesignerHost/SourceGeneratedPreviewXamlPreprocessor.cs`
   - rewrites explicit expression markup for preview-only runtime evaluation;
   - respects inherited `x:DataType`;
   - stays parser-driven rather than string-shape hacks.

6. `src/XamlToCSharpGenerator.Previewer.DesignerHost/SourceGeneratedPreviewMarkupRuntime.cs`
   - provides preview-only runtime values for rewritten expression markup;
   - tracks binding dependencies for live updates;
   - is intentionally scoped to preview/design-time behavior.

### What is still missing

1. Save/build refresh in source-generated mode still restarts the preview process.
2. The live overlay path is preview-specific and does not yet reuse the shared Hot Design contracts.
3. Minimal-diff metadata is not yet propagated through the VS Code preview path; updates still send full document text.
4. Resize/DPI changes still use a restart path instead of an in-session viewport update path.

## Runtime Infrastructure Already Present

The shared AXSG runtime still provides important reuse opportunities:

1. `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotReloadManager.cs`
   - metadata update handling;
   - IDE polling fallback;
   - build URI and source-path registration;
   - mirrored registration into Hot Design.

2. `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotDesignManager.cs`
   - document registration;
   - apply orchestration;
   - file persistence and fallback policies.

3. `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotDesignCoreTools.cs`
   - document-oriented runtime tools and editing helpers.

The new preview overlay architecture does not replace this runtime infrastructure. It reduces the immediate VS Code parity gap and changes which work should happen next.

## Updated Source Analysis

### VS Code / helper / preview infrastructure reviewed

1. `tools/vscode/axsg-language-server/preview-support.js`
2. `tools/vscode/axsg-language-server/preview-utils.js`
3. `src/XamlToCSharpGenerator.PreviewerHost/Program.cs`
4. `src/XamlToCSharpGenerator.PreviewerHost/PreviewSession.cs`
5. `src/XamlToCSharpGenerator.Previewer.DesignerHost/Program.cs`
6. `src/XamlToCSharpGenerator.Previewer.DesignerHost/SourceGeneratedRuntimeXamlLoader.cs`
7. `src/XamlToCSharpGenerator.Previewer.DesignerHost/SourceGeneratedPreviewXamlPreprocessor.cs`
8. `src/XamlToCSharpGenerator.Previewer.DesignerHost/SourceGeneratedPreviewMarkupRuntime.cs`
9. `src/XamlToCSharpGenerator.Runtime.Avalonia/CSharp.cs`
10. `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenPreviewMarkupRuntime.cs`

### Shared hot reload / hot design runtime reviewed

1. `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotReloadManager.cs`
2. `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotDesignManager.cs`
3. `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotDesignCoreTools.cs`
4. `src/XamlToCSharpGenerator.Runtime.Core/SourceGenHotDesignUpdateContext.cs`
5. `src/XamlToCSharpGenerator.Runtime.Core/ISourceGenHotDesignUpdateApplier.cs`
6. `src/XamlToCSharpGenerator.Runtime.Core/SourceGenHotDesignOptions.cs`

## Recommended Architecture

## Phase A: In-Process Save/Build Refresh for Source-Generated Preview

### Goal

When the user saves and the built AXSG artifact changes, keep the existing preview process/session alive and update the running preview in place.

### Why this is now the first priority

1. Unsaved source-generated live preview already exists.
2. The most obvious remaining UX problem is the full process restart on save/build.
3. Solving this gives actual hot reload semantics instead of preview-session churn.

### Design

1. Keep the current live in-memory overlay path unchanged for unsaved typing.
2. Add preview-host capability and status events so the helper can distinguish:
   - in-process source-generated hot reload supported,
   - in-process source-generated hot reload applied,
   - timeout/failure requiring restart.
3. Register the running preview document/root with the AXSG hot reload manager using:
   - `BuildUri`,
   - source file path,
   - preview root identity,
   - reload action or tracked instance binding.
4. On save/build:
   - run the build when needed;
   - wait for the running preview process to apply AXSG hot reload;
   - if the document is still dirty after the build-triggered reload, reapply the current in-memory overlay on top of the refreshed generated baseline.
5. Keep process restart only as a deterministic fallback on:
   - hot reload unavailable,
   - timeout,
   - hard reload failure,
   - host app startup paths that do not enable AXSG reload support.

### Important implementation note

The new preview overlay path means save/build hot reload does not have to solve unsaved-text delivery. It only needs to refresh the generated baseline in place and then preserve/reapply the preview's current dirty text if present.

## Phase B: Converge Preview Overlay with Shared Hot Design Contracts

### Goal

Reduce duplication between:

1. preview-specific live overlay logic in the designer host;
2. Studio / Hot Design runtime orchestration in the shared runtime.

### Design

1. Factor common document state into reusable services:
   - source file path,
   - build URI,
   - last-known-good XAML,
   - update diagnostics,
   - current dirty-text snapshot.
2. Extend shared design-update contracts only where reuse is justified:
   - full updated text,
   - optional minimal-diff metadata,
   - update result/status payloads.
3. Keep the preview-only markup runtime as a design-time boundary unless and until it becomes useful outside preview.
4. Move last-known-good and invalid-edit fallback policies into shared helpers if Studio and preview genuinely need identical convergence behavior.

### Updated conclusion on runtime contract changes

The old conclusion was too strong. A new runtime live-update contract is not required to make VS Code source-generated preview support unsaved edits. That is already implemented inside the preview/design-time boundary.

A shared contract is still worthwhile if the goal is:

1. reuse with Studio Hot Design;
2. minimal-diff propagation across tools;
3. unified diagnostics and fallback behavior.

## Phase C: Viewport and Session Stability Improvements

### Goal

Reduce restart pressure from non-XAML changes while keeping the current rendering fidelity guarantees.

### Design

1. Replace resize/DPI-triggered preview restart with an in-session viewport update path where Avalonia transport behavior allows it.
2. Preserve:
   - viewport size,
   - DPI,
   - dirty overlay text,
   - last-known-good overlay
   across save/build hot reload and viewport changes.
3. Keep the current restart path as fallback while this stabilizes.

## File-Level Plan

### Already delivered

1. `tools/vscode/axsg-language-server/preview-support.js`
   - source-generated live update behavior;
   - default-mode wiring;
   - source file path forwarding.

2. `tools/vscode/axsg-language-server/preview-utils.js`
   - source-generated default/auto mode planning.

3. `src/XamlToCSharpGenerator.PreviewerHost/Program.cs`
4. `src/XamlToCSharpGenerator.PreviewerHost/PreviewSession.cs`
   - source file path flow into the designer host.

5. `src/XamlToCSharpGenerator.Previewer.DesignerHost/Program.cs`
6. `src/XamlToCSharpGenerator.Previewer.DesignerHost/SourceGeneratedRuntimeXamlLoader.cs`
7. `src/XamlToCSharpGenerator.Previewer.DesignerHost/SourceGeneratedPreviewXamlPreprocessor.cs`
8. `src/XamlToCSharpGenerator.Previewer.DesignerHost/SourceGeneratedPreviewMarkupRuntime.cs`
9. `src/XamlToCSharpGenerator.Runtime.Avalonia/CSharp.cs`
10. `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenPreviewMarkupRuntime.cs`
   - live overlay, preview-only markup evaluation, and last-known-good fallback.

### Next files to change for Phase A

1. `tools/vscode/axsg-language-server/preview-support.js`
   - stop forcing save/build restart when source-generated in-process hot reload succeeds;
   - preserve current dirty overlay text through build-triggered refresh.

2. `src/XamlToCSharpGenerator.PreviewerHost/Program.cs`
   - add commands/events for capability reporting and hot reload status.

3. `src/XamlToCSharpGenerator.PreviewerHost/PreviewSession.cs`
   - keep helper and designer host alive across save/build refresh;
   - wait for hot reload completion before falling back to restart.

4. New designer-host bridge files, for example:
   - `PreviewHotReloadBridge.cs`
   - `PreviewDocumentState.cs`
   - `PreviewHotReloadStatus.cs`

5. `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotReloadManager.cs`
   - ensure preview-host registration and event surfacing are supported and tested.

### Next files to change for Phase B

1. `src/XamlToCSharpGenerator.Runtime.Core/SourceGenHotDesignUpdateContext.cs`
2. `src/XamlToCSharpGenerator.Runtime.Core/SourceGenHotDesignRegistrationOptions.cs`
3. `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotDesignManager.cs`
4. `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotDesignCoreTools.cs`

## Protocol and UX Plan

### Current UX that should remain

1. Avalonia/XamlX preview remains unchanged.
2. Source-generated preview remains the default mode.
3. Unsaved source-generated XAML edits continue to update live.
4. Invalid transient edits continue to show last-known-good preview state instead of tearing down the session.

### New UX for Phase A

1. On save/build:
   - show `Waiting for source-generated hot reload...`
   - show `Source-generated hot reload applied.` when successful
   - show `Source-generated hot reload timed out; restarting preview.` only on fallback
2. Keep the AXSG output channel explicit about whether the refresh path was:
   - in-process hot reload,
   - overlay reapply,
   - full restart fallback.

## Risks and Constraints

1. The current unsaved live overlay path is preview-specific and intentionally design-time scoped.
2. Some sample/app startup paths may not enable AXSG hot reload consistently enough for Phase A without extra preview-host bootstrapping.
3. Save/build in-process refresh must preserve the current unsaved overlay, not just the saved generated baseline.
4. The preview-only expression runtime uses Roslyn/dynamic evaluation inside the preview host. That is acceptable for design-time preview, but should not leak into production runtime paths.
5. Shared-contract work should be justified by actual reuse, not by forcing Studio and VS Code into an artificially identical architecture.

## Test Plan

### Already covered

1. source-generated preview loader decision logic;
2. last-known-good overlay fallback behavior at the loader policy level;
3. preview-only expression runtime helper behavior;
4. VS Code preview mode/default resolution.

### Phase A runtime / designer-host tests

1. Running preview root is registered with hot reload using correct `BuildUri` and `SourcePath`.
2. Save/build refresh updates the running preview without process restart when hot reload succeeds.
3. Dirty unsaved overlay text is reapplied after build-triggered hot reload.
4. Hot reload timeout falls back to restart cleanly.
5. Invalid post-build overlay edits still converge to last-known-good preview state.

### Phase B shared-runtime tests

1. Preview and Studio share update result semantics where intended.
2. Minimal-diff metadata is preserved for replace/insert/delete cases.
3. Shared fallback behavior converges to baseline after apply/remove cycles.

### Manual validation

1. Open a source-generated preview.
2. Type unsaved changes and verify live preview updates.
3. Save and build a tracked XAML file.
4. Verify preview updates without full process restart once Phase A lands.
5. Verify the dirty overlay is preserved if the editor still differs from disk.
6. Verify viewport size and DPI remain correct after refresh.

## Acceptance Criteria

### Current baseline already met

1. Source-generated preview is the default VS Code preview mode.
2. Unsaved source-generated XAML edits update live in preview.
3. Invalid transient edits preserve last-known-good preview behavior.
4. Existing Avalonia/XamlX preview behavior remains available and unchanged.

### Remaining criteria for future work

1. Save/build refresh updates source-generated preview without full process restart when AXSG hot reload succeeds.
2. Restart remains only as a deterministic fallback on timeout/failure/unsupported hosts.
3. Unsaved live overlay text survives save/build hot reload.
4. Preview/session size and DPI state survive in-process refresh paths.
5. Shared Hot Design / preview contracts are only expanded where they provide concrete reuse and testable value.
