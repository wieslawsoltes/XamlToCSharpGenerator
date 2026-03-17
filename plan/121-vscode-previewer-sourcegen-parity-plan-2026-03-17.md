# 121) VS Code Previewer Source-Gen Parity Plan (2026-03-17)

## Goal

Enable the VS Code source-generated preview path to understand the same AXSG-specific XAML surface that the built source-generator pipeline already supports, with emphasis on:

1. implicit and explicit C# expression markup;
2. shorthand expression forms that rely on `x:DataType` and/or `x:Class`;
3. compact and object-element inline `CSharp` value code;
4. inline event lambda/code forms that must not break preview loading.

The target is parity for the preview boundary that handles unsaved editor text. Saved builds already use the generated baseline.

## Root Cause

The current VS Code `sourceGenerated` preview mode is split into two stages:

1. load the generated baseline from the built assembly;
2. apply the current in-memory XAML through Avalonia's runtime XAML loader as a live overlay.

That second stage is where parity is lost.

Today the live overlay only compensates for a narrow subset of AXSG-only syntax:

1. `SourceGeneratedPreviewXamlPreprocessor` rewrites explicit `{= ...}` markup only;
2. `SourceGeneratedPreviewMarkupRuntime` can evaluate preview `CSharp` markup, but only after the XAML has already been rewritten into a runtime-understandable form;
3. compact/object-element inline `CSharp` is not normalized to tracked preview bindings;
4. implicit expression markup such as `{Name}`, `{!Flag}`, `{(s, e) => ...}`, `{this.Title}` and related shorthand never reaches the preview runtime in AXSG form.

Result:

1. built output works;
2. dirty in-memory preview falls back to Avalonia runtime XAML parsing;
3. AXSG-specific syntax is either ignored, misparsed as markup extensions, or loses dependency tracking.

## Implementation Strategy

Do not replace the preview transport or restart model.

Instead, make the preview overlay normalization stage source-gen aware enough that dirty editor text is translated into preview-runtime-compatible XAML before Avalonia runtime XAML loading runs.

## Design

### A. Expand preview analysis context

Extend the preview analysis helper so it can:

1. parse expressions, lambdas, and event statement blocks with Roslyn syntax APIs;
2. derive `source`/`root`/`target` member sets from reflection types, including inherited members;
3. rewrite bare identifiers into explicit `source`/`root`/`target` access while tracking source dependencies;
4. avoid assembly-accessibility failures for internal preview types.

The preview runtime already evaluates rewritten property expressions through dynamic `source`/`root`/`target` objects, so the overlay path does not need metadata-bound compile validation to normalize code correctly.

### B. Replace explicit-only rewrite with source-gen-aware rewrite

Upgrade `SourceGeneratedPreviewXamlPreprocessor` so it can rewrite:

1. explicit expression markup: `{= ...}`;
2. implicit expression markup that AXSG recognizes and which is not a real markup extension;
3. compact inline `CSharp` markup: `{CSharp Code=...}` and prefixed variants;
4. object-element inline `CSharp` blocks, including property-element and CDATA cases.

The rewrite should emit preview runtime markup in a single normalized form:

1. `CodeBase64Url=...`
2. `DependencyNamesBase64Url=...` when available

That keeps the preview runtime path deterministic and avoids fragile quoting.

### C. Carry root/data/target context through the XML walk

The preprocessor needs enough semantic context to rewrite expressions the same way the compiler does today.

Track:

1. inherited `x:DataType`;
2. document root type from `x:Class` or resolved root element type;
3. current owner/target type for attributes and property-element text;
4. inline `CSharp` object-element owner target type.

Property elements must keep the parent owner type as the preview `target` context.

### D. Preserve markup-extension safety

Do not rewrite real markup extensions such as:

1. `{Binding ...}`
2. `{CompiledBinding ...}`
3. `{StaticResource ...}`
4. custom prefixed markup extensions

Use structured markup parsing and markup-extension type checks instead of string heuristics.

### E. Keep preview runtime behavior stable

Do not replace the existing preview runtime callback model.

Continue to use:

1. `SourceGeneratedPreviewMarkupRuntime` for preview-only value provision;
2. binding-return behavior for bindable targets;
3. no-op delegates for event targets;
4. last-known-good fallback in the designer host loader.

The change is primarily about feeding that runtime the right normalized markup.

## Files To Change

### Primary implementation

1. `src/XamlToCSharpGenerator.Previewer.DesignerHost/PreviewExpressionAnalysisContext.cs`
   - add syntax-driven inline-expression/lambda/statement rewrite support for `source`/`root`/`target`;
   - add reflection-backed member discovery and dependency tracking.

2. `src/XamlToCSharpGenerator.Previewer.DesignerHost/SourceGeneratedPreviewXamlPreprocessor.cs`
   - rewrite implicit expressions;
   - normalize compact/object-element `CSharp`;
   - carry root/data/target context through traversal;
   - keep real markup extensions intact.

### Optional supporting change if needed

3. `src/XamlToCSharpGenerator.Previewer.DesignerHost/XamlToCSharpGenerator.Previewer.DesignerHost.csproj`
   - add a direct project reference only if the richer markup parser/type helpers are needed at compile time.

### Tests

4. add preview-host unit tests covering:
   - implicit expression rewrite;
   - root-scoped shorthand rewrite;
   - explicit inline `CSharp` dependency normalization;
   - object-element `CSharp` normalization;
   - regular `Binding` markup remains unchanged.

## Validation Plan

1. run preview-host/unit tests for the new rewrite coverage;
2. run the full test project if the touched areas ripple further than preview-host;
3. if the JS extension surface is untouched, no Node patch is needed beyond existing packaging assumptions.

## Acceptance Criteria

The work is complete when all of the following are true:

1. dirty VS Code source-generated preview accepts AXSG implicit/explicit expression markup instead of failing in the runtime overlay;
2. compact and object-element inline `CSharp` value code participates in preview dependency tracking;
3. inline event lambda/code no longer blocks preview load for dirty documents;
4. normal Avalonia/XamlX markup extensions are not misclassified as AXSG expressions;
5. preview-host tests prove the rewritten XAML shape for the representative parity cases.
