# VS Code XAML Professional Parity Plan

Date: 2026-03-18

## Status Snapshot

- Phase 1: Editor Baseline - complete
- Phase 2: Navigation and Workspace Intelligence - complete
- Phase 3: Authoring Assistance - complete
- Phase 4: Preview and Inspector Professionalization - substantially complete

This plan has been updated to reflect the implementation status on the current branch rather than the original gap list.

## Implemented on This Branch

- full-document formatting via `textDocument/formatting`
- folding ranges via `textDocument/foldingRange`
- selection ranges via `textDocument/selectionRange`
- linked editing for matching tags via `textDocument/linkedEditingRange`
- workspace symbol search via `workspace/symbol`
- document highlights via `textDocument/documentHighlight`
- document links for XAML include and `Source="..."` navigation via `textDocument/documentLink`
- markup-extension signature help via `textDocument/signatureHelp`
- editor snippets for common Avalonia and XAML authoring patterns
- richer code actions for binding rewrite and attribute-to-property-element conversion
- diagnostic quick fixes for `AXSG0110`, `AXSG0111`, `AXSG0109`, `AXSG0104`, and `AXSG0105`
- diagnostic quick fix for routed event handler errors via compatible handler stub or overload generation for `AXSG0600`
- include quick fixes for adding missing project items and removing invalid include elements for deterministic `AXSG040x` cases
- namespace import quick fixes for unresolved element names
- namespace import quick fixes for owner-qualified property tokens, including attached-property attributes and `Setter.Property`
- namespace import quick fixes for unresolved type-valued attribute tokens such as `x:DataType` and `ControlTheme TargetType`
- preview and inspector readiness messaging, empty-state messaging, sync and reveal commands, and session-bootstrap improvements

## Phase Breakdown

### Phase 1: Editor Baseline

Status: complete

Delivered:

- XAML and AXAML document formatting
- structural folding
- selection expansion
- linked editing for start and end tags

Outcome:

- the extension now covers the baseline structural editor features expected from a professional XAML extension

### Phase 2: Navigation and Workspace Intelligence

Status: complete

Delivered:

- workspace symbols
- document highlights
- document links for include and URI-based navigation
- strong definition and reference flows for XAML types, properties, resources, bindings, selectors, and linked C# symbols

Outcome:

- cross-file XAML navigation is now in a professional state for normal authoring workflows

### Phase 3: Authoring Assistance

Status: complete

Delivered:

- markup-extension signature help
- Avalonia and XAML snippets
- binding conversion rewrite actions
- attribute-to-property-element refactoring
- namespace import quick fixes across element, property, and type-reference cases
- quick fixes for missing or invalid compiled bindings
- quick fix for non-`partial` `x:Class` companion types
- quick fixes for invalid and mismatched `x:ClassModifier`
- quick fixes for routed event handler diagnostics via handler stub or compatible overload generation
- quick fixes for include membership and invalid include removal across deterministic include cases

Outcome:

- the extension now covers the practical authoring and deterministic quick-fix surface expected from a professional XAML extension

### Phase 4: Preview and Inspector Professionalization

Status: substantially complete

Delivered:

- dedicated AXSG Inspector container
- improved preview and inspector readiness states
- clearer empty and disabled-state messaging
- explicit sync and reveal command surface
- improved inspector bootstrap from active preview panels
- session handoff improvements between editor, preview, and inspector

Remaining:

- continue reducing preview and inspector sensitivity to session focus and startup ordering
- harden recovery paths after preview disposal or connection resets
- improve design mutation discoverability beyond the current toolbox and property workflows
- add more explicit operational health surfacing for preview runtime, language service, and inspector sub-systems

Outcome:

- preview and inspector are now viable daily workflows, but they still need deeper resilience and discoverability polish to fully match mature commercial tooling

## Remaining Gaps

### Preview and Inspector Resilience

The preview and inspector stack is functionally much stronger than the original baseline, but this is now the primary remaining gap area:

- recovery after disposed preview hosts
- race reduction around preview startup and focus changes
- deeper workflow polish for live design mutations
- richer operational health surfacing for preview runtime, language service, and inspector sub-systems

## Recommended Next Implementation Order

1. Continue preview and inspector resilience work around disposed sessions and startup ordering.
2. Improve operational health surfacing for preview runtime, language service, and inspector sub-systems.
3. Expand design mutation discoverability and recovery workflows in the preview and inspector.

## Success Criteria for Remaining Work

- preview and inspector survive normal tab switching, startup churn, and preview restarts with less user intervention
- preview and inspector failures are surfaced with clearer subsystem-level status and recovery guidance
- live design workflows are discoverable without relying on output-log troubleshooting
