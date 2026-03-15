# Hot Design Lossless Editing Plan (2026-03-14)

## Goal
Replace Hot Design's current parse-mutate-reserialize workflow with a lossless, AST-backed source editor that preserves original whitespace, indentation, quote style, attribute ordering, and untouched text while still supporting undo/redo across editing sessions.

## Problem Statement
Current Hot Design source mutations in `XamlSourceGenHotDesignCoreTools`:
1. Parse source into `XDocument`.
2. Mutate attributes or elements.
3. Re-serialize the entire document with `document.ToString(SaveOptions.None)`.

That approach breaks editing fidelity:
1. Single-property edits rewrite unrelated whitespace and indentation.
2. Attribute quote style and alignment drift after each edit.
3. Insert/remove operations normalize surrounding markup instead of preserving local style.
4. Undo/redo restores snapshots, but those snapshots already contain formatter drift introduced by the serializer.

## Target Behavior
Hot Design editing must:
1. Preserve all untouched source bytes exactly.
2. Update existing attributes by replacing only the attribute value span.
3. Remove attributes/elements without leaving malformed spacing or blank structural lines.
4. Insert attributes/elements using local formatting inferred from the surrounding AST context.
5. Keep undo/redo snapshot semantics intact so round trips return byte-identical source text.

## Architecture

### 1. Lossless document editor
Add a runtime-local `XamlSourceGenHotDesignDocumentEditor` that owns:
1. Original XAML text.
2. `XDocument` parsed with `LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace`.
3. Source-range helpers for element start tags, attribute name/value ranges, full element spans, and indentation detection.

The XML tree is used only for node identity and structural lookup. Final output is produced by text edits applied to the original buffer, not by serializing the full document.

### 2. Text-edit operations
Support these lossless operations:
1. `SetOrRemoveProperty`
2. `InsertElement`
3. `RemoveElement`

Each operation computes one or more source edits:
1. Existing attribute update: replace only the value range, preserving quote character and surrounding spacing.
2. Attribute removal: remove the attribute token plus only the whitespace that belongs to that attribute occurrence.
3. Attribute insertion: infer single-line vs multiline attribute layout from the owning start tag and insert only the new attribute text.
4. Element insertion: preserve parent formatting style, expand self-closing parents only when required, and indent inserted markup relative to local siblings.
5. Element removal: remove the exact element span plus a structural line wrapper when the element occupies its own line.

### 3. History model
Keep the existing document history stacks, but ensure they store exact lossless text snapshots:
1. Before edit: push current exact text.
2. After edit: store exact updated text returned by the lossless editor.
3. Undo/redo remain full-text snapshot based.

No serializer-generated normalization should appear in any history entry.

## Implementation Phases

### Phase 1: Editor core
1. Add source-range and text-edit helpers in `Runtime.Avalonia`.
2. Implement lossless property update logic.
3. Implement lossless insert/remove element logic.

### Phase 2: Hot Design integration
1. Route `ApplyPropertyUpdateAsync` through the lossless editor.
2. Route `InsertElementAsync` through the lossless editor.
3. Route `RemoveElementAsync` through the lossless editor.
4. Preserve no-op behavior without forcing unnecessary hot reload updates.

### Phase 3: Validation
Add regression coverage for:
1. Existing attribute update preserves formatting exactly except the changed value.
2. Attribute removal preserves surrounding layout.
3. Element insertion preserves sibling indentation and local newline style.
4. Element removal preserves surrounding structure.
5. Undo/redo round trips are byte-identical for messy-formatted documents.

## Acceptance Criteria
1. Single-property updates do not rewrite unrelated whitespace.
2. Insert/remove operations preserve nearby formatting conventions.
3. Undo restores the exact original file bytes.
4. Redo restores the exact edited file bytes.
5. Existing Hot Design and Studio runtime tests stay green.
