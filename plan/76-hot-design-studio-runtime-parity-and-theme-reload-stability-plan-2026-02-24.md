# Hot Design Studio Runtime Parity And Theme Reload Stability Plan (2026-02-24)

## Goals
- Make theme/control-theme hot reload deterministic and crash-safe under repeated metadata updates.
- Keep legacy hot-design startup API compatibility without forcing full studio host startup.
- Make design overlay preserve live app data context and bindings while active.
- Improve live selection resolution to avoid cross-document ambiguity.
- Ensure template editor always edits the selected template document text.

## Scope
- Runtime hot reload manager (`XamlSourceGenHotReloadManager`).
- Studio host and overlay runtime shell (`XamlSourceGenStudioHost`, `XamlSourceGenStudioOverlayView`, `XamlSourceGenStudioShellViewModel`).
- Hot design tool/core APIs (`XamlSourceGenHotDesignTool`, `XamlSourceGenHotDesignCoreTools`).
- Targeted regression tests in runtime and generator test suites.

## Work Breakdown

### 1. Theme Reload Safety (P0)
- Replace direct control theme assignment path with safe apply/reapply helpers.
- For `ContentControl` targets, temporarily detach visual content during theme swap/reapply to avoid duplicate visual-parent attachment failures.
- Preserve explicit user-authored theme overrides and only manage implicit theme overrides created by runtime refresh.

### 2. Legacy API Compatibility (P0)
- Keep `UseAvaloniaSourceGeneratedXamlHotDesign(...)` as manager-only compatibility entrypoint.
- Do not implicitly start/stop app-level studio host from legacy compatibility API.

### 3. Overlay Live Surface DataContext Reliability (P0)
- Replace one-time DataContext snapshot with live one-way binding from host content/window DataContext source.
- Ensure design overlay mode does not break sample bindings/pages.

### 4. Studio Template And Selection Correctness (P1)
- Add explicit API to retrieve current document text by build URI.
- Bind template editor text to selected template document text (not active document text).
- Add stricter live selection resolution:
  - Prefer active document when possible.
  - Allow configurable ambiguous type fallback.
  - Reject ambiguous cross-document type-only matches in shell live selection path.

### 5. Regression Test Expansion (P1)
- Update compatibility API tests to reflect manager-only legacy behavior.
- Add ambiguous type-match resolution tests.
- Add template document text synchronization tests.
- Update profile generator test assumptions to accept multiple generated sources while asserting expected hint/token presence.

## Acceptance Criteria
- Repeated control-theme edits do not throw visual-parent attachment exceptions in runtime.
- Legacy hot-design API enables hot-design manager without booting app-level studio host.
- Design overlay keeps bound controls visible/functional (no list/data-context regression).
- Template editor applies text from the selected template document consistently.
- Live selection does not jump to arbitrary documents on ambiguous type-only matches.
- Updated runtime/generator tests pass for touched areas.

## Risk Controls
- Keep all runtime hot reload side effects best-effort and non-fatal.
- Avoid reflection in runtime/emitted paths.
- Keep existing public API behavior backward compatible by overloading instead of replacing signatures.

## Delivery Notes
- Apply in small verifiable commits grouped by runtime safety, studio behavior, and tests.
- Validate with focused `Runtime` and `FrameworkPipelineProfileTests` slices before broad suite.
