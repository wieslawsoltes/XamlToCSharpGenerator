# Hot Design Studio Canvas Feedback And Adorner Plan (2026-02-24)

## Goal
Deliver first-class live-canvas visual feedback so design mode provides clear, immediate targeting and editing context while preserving runtime behavior and data binding integrity.

## Scope
- In-app design overlay experience.
- Element targeting and visible selection feedback on the live canvas.
- Visual tree discoverability in the Elements panel.
- Non-regression for runtime page rendering (especially data-bound collection controls).

## Current Gaps
- No persistent selection overlay in the live canvas after clicking a control.
- No hover affordance to confirm pointer target before selection.
- Elements panel can appear root-only because nested nodes are collapsed by default.
- Overlay host can accidentally alter live content binding context when wrapping window content.

## Target Behavior
- Design and agent modes show:
  - Hover adorner with lightweight border + label.
  - Selection adorner with stronger border + label.
  - Adorner bounds tracked as layout changes.
- Interactive mode shows no design adorners and does not intercept behavior.
- Elements panel expands hierarchy by default so nested nodes are visible immediately.
- Overlay host preserves live page/window DataContext and binding behavior.

## Implementation Phases

### Phase 1: Runtime Safety Guardrails
- Preserve live content DataContext when injecting studio overlay host.
- Ensure pointer handling only targets live content subtree, never studio chrome.
- Ensure interactive mode never blocks live interaction.

### Phase 2: Canvas Adorner Layer
- Add non-hit-test adorner canvas above live presenter.
- Add hover and selection adorner visuals:
  - Border + translucent fill.
  - Label chip with mode + control identity.
- Recompute adorner geometry from visual transforms on pointer movement and layout updates.
- Hide adorners when target is invalid, detached, collapsed, or out of viewport.

### Phase 3: Selection Synchronization
- Keep selected control highlight when selection is triggered from live click.
- Sync visual highlight when selection changes from workspace tree/properties.
- Resolve control candidates by x:Name first, then by control type with deterministic tie-breaking.

### Phase 4: Elements Discoverability
- Expand tree items by default so nested elements are visible without manual expansion.
- Keep workspace default selected element at first meaningful descendant instead of always root.

### Phase 5: Validation
- Runtime tests:
  - Cross-document selection resolution.
  - Default element selection behavior.
  - Interactive mode does not force selection.
- Build validation:
  - Runtime project build.
  - Sample app build.

## Acceptance Criteria
- Live canvas always shows a clear hover and selected target in design mode.
- Selecting controls does not break data-bound visuals (ListBox/ItemsControl content remains visible).
- Elements panel shows nested hierarchy immediately on load.
- Switching between design and interactive modes correctly toggles adorners and input interception.
- All targeted runtime tests pass.

## Follow-up Work
- Resize handles and direct manipulation grips for selected controls.
- Multi-select visualization and group operations.
- Keyboard navigation between visual neighbors.
- Contextual adorner actions (select parent, wrap in container, quick property toggles).
