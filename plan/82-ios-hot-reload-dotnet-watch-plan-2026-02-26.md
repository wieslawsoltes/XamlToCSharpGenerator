# 82 - iOS Hot Reload (`dotnet watch`) Plan (Simulator + Device) - 2026-02-26

## 1) Goal

Make AXSG hot reload reliable on iOS for:

1. Simulator (`ControlCatalog.iOS`, `SourceGenXamlCatalogSample`).
2. Physical device.

This must work with `dotnet watch` and remain AOT-safe for release builds.

## 2) Current failure signature (reproduced)

From current runs:

1. `dotnet watch` reports: `Waiting for application to connect to pipe ...`.
2. iOS app launches in simulator, but no hot reload connection is established.
3. No edit-to-apply cycle occurs for AXSG runtime.

Observed in `/tmp/axsg_ios_watch.log`:

1. `dotnet watch` launches with `DOTNET_STARTUP_HOOKS` and `DOTNET_WATCH_HOTRELOAD_NAMEDPIPE_NAME`.
2. iOS launch happens through `xcrun simctl launch`.
3. App renders UI, but watch pipe connection never completes.

## 3) Baseline comparison (MAUI + Uno)

### MAUI patterns to mirror

1. Debug mobile builds force interpreter/dynamic-code-friendly settings (`UseInterpreter=True`).
2. iOS debug linker profile is relaxed to avoid trimming hot reload plumbing.
3. Mobile build targets explicitly configure run/debug behavior for hot reload sessions.

### Uno patterns to mirror

1. Do not depend only on local named-pipe startup-hook flow for mobile.
2. Use app-initiated remote control channel (device/simulator connects out) with explicit endpoint config.
3. Keep operation/status protocol explicit and observable for diagnostics.

## 4) Root cause decomposition (AXSG)

| Area | Current state | Impact | Required fix |
|---|---|---|---|
| iOS run/runtime mode | No AXSG iOS watch profile in build-transitive targets | App runs but watch hot reload channel is not guaranteed | Add iOS watch profile and defaults in AXSG build props/targets |
| Startup hook and runtime capabilities | iOS SDK defaults are conservative for dynamic code/hotreload | Delta applier hook path can be non-functional | Explicitly enable required debug settings for watch sessions |
| Environment propagation | `dotnet watch` env is passed to `dotnet run`, but iOS launch indirection can lose/ignore critical vars | App never connects to watch pipe | Deterministically map env vars into `MlaunchEnvironmentVariables`/launch args |
| Transport model | AXSG runtime is coupled to metadata handler + local watch path | Device scenarios are brittle | Add transport abstraction and mobile-capable remote channel |
| Diagnostics | No startup handshake timeline for iOS-specific hot reload path | Root-cause analysis is slow and ambiguous | Add structured startup/transport diagnostics |

## 5) Target architecture

Use a hybrid model:

1. **Primary (simulator):** keep `dotnet watch` metadata update path when available.
2. **Fallback/portable (device + simulator):** AXSG remote hot reload channel (app connects to host endpoint).
3. **Runtime policy:** choose transport by capability probe at startup and log selected mode.

### Transport policy

1. `Auto` (default):
   1. Try metadata update handler pathway.
   2. If not active by timeout, switch to AXSG remote channel.
2. `MetadataOnly` (strict desktop-like behavior).
3. `RemoteOnly` (forced mobile/CI troubleshooting mode).

## 6) Implementation plan

## Phase A - iOS watch setup parity (build + launch)

### A1. Add AXSG iOS hot reload properties (build-transitive)

File targets:

1. `src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.props`
2. `src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.targets`

Add new properties:

1. `AvaloniaSourceGenIosHotReloadEnabled` (default `true` for Debug iOS).
2. `AvaloniaSourceGenIosHotReloadUseInterpreter` (default `true` for Debug iOS + watch).
3. `AvaloniaSourceGenIosHotReloadTransportMode` (`Auto|MetadataOnly|RemoteOnly`, default `Auto`).
4. `AvaloniaSourceGenIosHotReloadHandshakeTimeoutMs` (default `3000`).

### A2. Deterministic iOS env forwarding for watch

In `Build.targets`, when:

1. `TargetFramework` is iOS,
2. `Configuration=Debug`,
3. `DotNetWatchBuild=true`,
4. `AvaloniaSourceGenHotReloadEnabled=true`,

inject:

1. `UseInterpreter=true` (or `MtouchInterpreter=all` if explicitly needed).
2. `StartupHookSupport=true`.
3. `MlaunchEnvironmentVariables` entries for:
   1. `DOTNET_STARTUP_HOOKS`
   2. `DOTNET_MODIFIABLE_ASSEMBLIES`
   3. `DOTNET_WATCH_HOTRELOAD_NAMEDPIPE_NAME`
   4. `DOTNET_HOTRELOAD_NAMEDPIPE_NAME` (if present)
   5. AXSG transport mode/env overrides

### A3. Guardrails

1. Never change Release defaults.
2. Provide opt-out flags per property.
3. Emit a single startup banner with effective iOS hot reload settings.

## Phase B - Runtime transport abstraction

### B1. Introduce transport contracts

New runtime abstractions in `src/XamlToCSharpGenerator.Runtime.Core`:

1. `ISourceGenHotReloadTransport`
2. `SourceGenHotReloadTransportCapabilities`
3. `SourceGenHotReloadHandshakeResult`

### B2. Implement transports in `src/XamlToCSharpGenerator.Runtime.Avalonia`

1. `MetadataUpdateTransport` (current behavior, wrapped).
2. `RemoteSocketTransport` (mobile-compatible outbound channel).

### B3. Integrate with manager

Update `src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotReloadManager.cs`:

1. Startup capability probe.
2. Transport selection policy.
3. Timeout-based fallback.
4. Structured status events:
   1. `TransportSelected`
   2. `HandshakeStarted`
   3. `HandshakeCompleted`
   4. `HandshakeFailed`

## Phase C - Compiler and linker hardening

### C1. Preserve hot reload entrypoints on mobile debug

Update generation/hints in:

1. `src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs`

Ensure:

1. Metadata update handler hooks are emitted once.
2. Required manager members survive trimming in Debug iOS configurations.
3. No reflection-based fallback is introduced.

### C2. Runtime loader resilience

Verify hot reload artifacts are resolvable under iOS bundle rules:

1. resource uri handling,
2. include resolution,
3. theme/resource graph updates.

## Phase D - `dotnet watch` mobile bridge (device-ready path)

### D1. AXSG remote endpoint configuration

Add MSBuild + env configuration:

1. `AvaloniaSourceGenHotReloadRemoteEndpoint` (host:port or ws url).
2. Auto host selection for simulator.
3. Explicit host/IP requirement for physical devices.

### D2. Device transport behavior

1. App initiates outbound connection to host endpoint.
2. Host service relays change operations and payloads.
3. Operation IDs and acknowledgements are tracked (same status model as studio/hot design).

### D3. Failure policy

1. If remote endpoint unavailable, app keeps running.
2. Clear actionable diagnostics are emitted.
3. No crash/no blocking of normal app startup.

## Phase E - Developer workflow and docs

Add iOS-specific guidance:

1. simulator quickstart (`dotnet watch` command + required workload/runtime settings),
2. device quickstart (endpoint/network requirements),
3. troubleshooting matrix keyed by startup logs.

Primary docs:

1. `README.md` (hot reload section),
2. `samples/ControlCatalog.iOS/README` (new),
3. optional `docs/hot-reload-ios.md`.

## 7) Test and acceptance plan

## A. Simulator acceptance

1. `AXSG_HOTRELOAD_TRACE=1 dotnet watch ./samples/ControlCatalog.iOS/ControlCatalog.iOS.csproj`
2. Verify handshake reaches `Connected` state within timeout.
3. Edit AXAML in ControlCatalog and confirm one apply operation per edit.
4. 50 sequential edits without disconnect/crash.

## B. Device acceptance

1. Run with remote endpoint configured.
2. Verify outbound app connection and edit apply loop.
3. 50 sequential edits over Wi-Fi.
4. App startup remains successful if host unavailable.

## C. Regression acceptance

1. Desktop `dotnet watch` behavior unchanged.
2. Android/macOS samples unaffected.
3. Release iOS build does not enable debug hot reload settings.

## D. Observability acceptance

1. Logs always show selected transport and handshake result.
2. On failure, logs include exact missing capability/env requirement.

## 8) Delivery order (granular PRs/commits)

1. PR1: Build-transitive iOS watch profile + env forwarding + docs for effective settings.
2. PR2: Runtime transport abstraction + metadata transport wrapper.
3. PR3: Remote mobile transport + manager fallback policy.
4. PR4: Compiler/linker hardening + debug trim safety.
5. PR5: End-to-end simulator/device tests + troubleshooting docs.

## 9) Risks and mitigations

1. iOS SDK behavior differences across versions.
   1. Mitigation: keep feature flags and log all effective properties.
2. Device networking variability.
   1. Mitigation: explicit endpoint override and connectivity diagnostics.
3. Duplicate apply signals from mixed transports.
   1. Mitigation: reuse operation correlation and dedupe in hot reload manager.

## 10) Definition of done

This plan is complete when:

1. iOS simulator hot reload is deterministic under `dotnet watch`.
2. iOS device hot reload works via remote transport.
3. No regressions in desktop/runtime hot reload.
4. Setup requires no ad-hoc manual hacks beyond documented flags.

## 11) Phase A progress (2026-02-27)

### Completed

1. Added build-transitive iOS hot reload property surface (A1) with defaults and compiler-visible exposure.
2. Added deterministic iOS launch env forwarding through `MlaunchAdditionalArgumentsProperty` (A2).
3. Added iOS startup banner with effective settings and forwarded/suppressed env diagnostics (A3).
4. Added safety guard: startup hooks are no longer forwarded by default on iOS (`AvaloniaSourceGenIosHotReloadForwardStartupHooks=false`), preventing common AOT mismatch regressions.
5. Tightened iOS hot-reload launch mutation scope to watch sessions only (`DotNetWatchBuild=true` or `DOTNET_WATCH=1`).

### Open from later phases

1. Dotnet watch metadata pipe handshake (`Waiting for application to connect to pipe`) still needs transport fallback work from Phase B+.
2. Device-ready remote channel remains planned work under Phase D.

## 12) Phase B progress (2026-02-27)

### Completed

1. Added runtime transport contracts in `Runtime.Core`:
   1. `ISourceGenHotReloadTransport`
   2. `SourceGenHotReloadTransportCapabilities`
   3. `SourceGenHotReloadHandshakeResult`
   4. `SourceGenHotReloadTransportMode`
   5. `SourceGenHotReloadTransportStatus` + status kind enum
2. Added transport implementations in `Runtime.Avalonia`:
   1. `MetadataUpdateTransport` (metadata path capability + pending handshake model)
   2. `RemoteSocketTransport` (outbound endpoint handshake scaffold)
3. Integrated `XamlSourceGenHotReloadManager` with:
   1. startup transport capability probe,
   2. mode selection (`Auto|MetadataOnly|RemoteOnly`) from env,
   3. handshake timeout from env (`AXSG_HOTRELOAD_HANDSHAKE_TIMEOUT_MS`),
   4. auto fallback from metadata handshake timeout to remote transport in `Auto` mode,
   5. structured status stream:
      1. `TransportSelected`
      2. `HandshakeStarted`
      3. `HandshakeCompleted`
      4. `HandshakeFailed`
4. Extended hot reload event bus to publish transport status changes.
5. Added runtime tests for:
   1. metadata handshake completion on first metadata delta,
   2. auto-mode timeout fallback attempt to remote transport.
6. Post-review hardening:
   1. transport initialization no longer runs from registration when hot reload may be disabled,
   2. manager `Disable()` now tears down active transport state,
   3. auto-timeout fallback to remote is now gated to mobile/explicit remote-endpoint context (prevents desktop false fallback),
   4. added guard test to ensure no auto fallback happens outside mobile/remote context,
   5. remote endpoint parser now requires explicit TCP port (`tcp://host:port`) and no longer applies implicit default port fallback.

### Open for later phases

1. Remote transport currently performs connection handshake only; operation payload protocol remains Phase D.
2. Full device bridge host relay and operation ACK correlation remain Phase D.

## 13) Phase C progress (2026-02-27)

### Completed

1. Compiler/linker hardening (C1):
   1. Extended compiler-visible generator options usage for iOS hot reload mode.
   2. Hardened generated hot-reload assembly hook emission to remain single-source/single-hook per assembly.
   3. Added iOS Debug linker preservation hints (module-initializer + `DynamicDependency`) for metadata update entrypoints:
      1. `XamlSourceGenHotReloadManager.ClearCache`
      2. `XamlSourceGenHotReloadManager.UpdateApplication`
2. Runtime loader/include resilience (C2):
   1. Normalized markup-extension base URI handling so rooted relative paths resolve to absolute `avares://{assembly}/...` URIs instead of `file:///...`.
   2. Added static-resource resolver URI normalization against `IUriContext` (and root-assembly fallback) before include graph traversal.
   3. Added source-generated loader fallback candidate resolution via `IRootObjectProvider` assembly context for rooted relative URIs.
   4. Added non-fatal static-resource recovery path for `XamlLoadException` to re-run source-gen include/resource fallback before failing.
3. Added guard tests for Phase C behavior:
   1. metadata update hook emitted once,
   2. iOS debug linker-preservation hints emitted,
   3. relative base URI normalization in markup-extension service provider,
   4. static-resource relative URI normalization via `IUriContext`,
   5. source-generated loader root-assembly relative URI resolution,
   6. static-resource relative URI normalization via `IRootObjectProvider` assembly context.

### Validation

1. Focused runtime/generator guard tests pass (`7/7` targeted new tests).
2. Broader impacted test slice pass (`85/85` for affected runtime/generator/profile/transport classes).
3. Sample builds pass:
   1. `ControlCatalog.Desktop` (Debug)
   2. `ControlCatalog.iOS` (Debug, simulator)

## 14) Phase D progress (2026-02-27)

### Completed

1. AXSG remote endpoint configuration (D1):
   1. Added build-transitive remote endpoint property surface:
      1. `AvaloniaSourceGenHotReloadRemoteEndpoint`
      2. `AvaloniaSourceGenHotReloadRemotePort`
      3. `AvaloniaSourceGenHotReloadRemoteAutoSimulatorEndpointEnabled`
      4. `AvaloniaSourceGenHotReloadRemoteRequireExplicitDeviceEndpoint`
   2. Added iOS watch-time endpoint resolution:
      1. simulator auto-endpoint defaults to `tcp://127.0.0.1:{port}` when no explicit endpoint is provided,
      2. physical-device path emits an explicit warning when endpoint is required and not configured.
   3. Added deterministic env forwarding for remote endpoint:
      1. `AXSG_HOTRELOAD_REMOTE_ENDPOINT` is now forwarded when resolved.
   4. Extended iOS startup banner diagnostics with:
      1. simulator/device context,
      2. resolved endpoint,
      3. auto-selection flag,
      4. explicit-device-endpoint requirement flag.

2. Device transport behavior and operation protocol (D2):
   1. Added remote operation contracts in `Runtime.Core`:
      1. `ISourceGenHotReloadRemoteOperationTransport`
      2. `SourceGenHotReloadRemoteUpdateRequest`
      3. `SourceGenHotReloadRemoteUpdateResult`
      4. `SourceGenHotReloadRemoteOperationStatus`
   2. Extended `RemoteSocketTransport` from handshake scaffold to active channel:
      1. persistent receive loop,
      2. JSON apply-request parsing (`messageType=apply`),
      3. operation ACK publishing (`messageType=ack`),
      4. endpoint parsing for `host:port`, `tcp://...`, and `ws(s)://...`.
   3. Integrated manager-level remote operation flow:
      1. remote transport subscription lifecycle bound to active transport,
      2. remote request type/build-uri resolution,
      3. operation-id dedupe history,
      4. operation context propagation (`OperationId`, `RequestId`, `CorrelationId`) into update context,
      5. operation status stream (`Applying` -> terminal state),
      6. ACK emission with success/failure diagnostics.
   4. Extended hot reload event bus with remote operation status publication.

3. Failure policy hardening (D3):
   1. Remote transport listener/ACK failures are non-fatal and logged.
   2. Invalid/missing remote operation payloads return failure results instead of crashing app.
   3. Manager-disabled and unresolved-target cases produce actionable failure diagnostics.
   4. Startup remains non-blocking when remote endpoint is unavailable.

### Validation

1. Targeted runtime tests pass (`35/35`):
   1. `XamlSourceGenHotReloadManagerTests`
   2. `RemoteSocketTransportTests`
2. Broader impacted slice pass (`88/88`):
   1. runtime hot reload/transport/static-resource tests,
   2. generator hot-reload profile tests.
3. Sample build validation:
   1. `ControlCatalog.Desktop` Debug build succeeds (`0 errors`, `0 warnings`).
   2. `ControlCatalog.iOS` Debug build succeeds (`0 errors`; trim warnings remain expected for current Avalonia/mobile profile).
4. Watch smoke:
   1. `AXSG_HOTRELOAD_TRACE=1 dotnet watch ./ControlCatalog.iOS.csproj` launches simulator app successfully and keeps session running.

### Post-review fixes (2026-02-27)

1. Resolved iOS startup crash for cross-assembly include load path:
   1. symptom: `No precompiled XAML found for avares://ControlSamples/HamburgerMenu/HamburgerMenu.xaml` during `App.xaml` resource merge under iOS `dotnet watch`.
   2. root cause: assembly load during include lookup did not guarantee module-constructor execution, so source-generated artifact registration for classless include documents could be missing at first include probe.
   3. fix: `AvaloniaSourceGeneratedXamlLoader` now executes module constructors (`RuntimeHelpers.RunModuleConstructor`) for loaded `avares://{assembly}/...` candidates before registry lookup.
2. Preserved reflection guard constraints:
   1. implementation avoids introducing tracked `System.Reflection` API patterns in `Runtime.Avalonia`.
   2. `ReflectionGuardTests` remain passing.
3. Re-validated watch startup after fix:
   1. `AXSG_HOTRELOAD_TRACE=1 dotnet watch ./ControlCatalog.iOS.csproj` launches app in simulator without the previous include crash.

## 15) Phase E progress (2026-02-27)

### Completed

1. Added iOS hot reload developer guide:
   1. `docs/hot-reload-ios.md`
   2. covers simulator quickstart, device quickstart, property/env matrix, endpoint formats, and troubleshooting by startup-log signature.
2. Added sample-local guide:
   1. `samples/ControlCatalog.iOS/README.md`
   2. includes simulator command, remote override examples, and device baseline flow.
3. Updated repository README hot-reload documentation:
   1. new `iOS Hot Reload (dotnet watch)` section in `README.md`,
   2. links to detailed guide and sample README.

### Notes

1. Troubleshooting matrix now explicitly documents known non-blocking signals (`127.0.0.1:10000` IDE socket warning) vs actionable failures.
2. Device setup docs assume user-provided reachable remote endpoint bridge/service compatible with AXSG remote protocol payloads.

## 16) Phase E review closure (2026-02-27)

### Validation completed

1. Runtime/generator impacted test slice is passing (`89/89`) including:
   1. transport and remote operation manager coverage,
   2. static-resource and loader fallback coverage,
   3. iOS profile/compiler option guard coverage.
2. Sample build validation:
   1. `ControlCatalog.Desktop` Debug build succeeds (`0 errors`, `0 warnings`).
   2. `ControlCatalog.iOS` Debug simulator build succeeds (`0 errors`; trim warnings remain expected for current Avalonia/mobile profile).
3. No additional runtime regressions were found in the changed Phase A-E code paths during this review pass.

### Remaining scope notes

1. Full end-to-end device bridge host service remains external to this repository and is still a required prerequisite for physical-device remote transport scenarios.
2. iOS trim warnings are currently dominated by upstream Avalonia reflection-binding and dynamic-dependency patterns and are not introduced by this Phase A-E implementation slice.
