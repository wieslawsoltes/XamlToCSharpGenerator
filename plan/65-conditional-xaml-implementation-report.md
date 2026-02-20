# Conditional XAML Implementation Report

## Scope Implemented
- Conditional namespace expression parsing and normalization (`?ApiInformation.*` suffix).
- Condition metadata propagation across parser/binder models (objects, properties, property elements, resources/templates/styles/control themes/includes/setters/events).
- Compile-time conditional evaluation in semantic binder with branch pruning.
- Named-element binding parity fix: named fields are now collected from the resolved object graph after conditional pruning.
- Diagnostic split/fix:
  - `AXSG0120`: invalid conditional expression.
  - `AXSG0200`: source emission failure (retained).
- Catalog sample integration with a dedicated Conditional XAML page.
- README updates documenting syntax, supported methods, and behavior.

## Key Behavior
- Conditional namespace URIs are normalized to their base XML namespace for regular type/property resolution.
- Condition-false branches are removed before semantic type/property binding.
- Unreachable branches no longer emit unknown-type noise (`AXSG0100`) for intentionally absent gated types.
- Invalid conditional expressions are reported as warnings (`AXSG0120`), and compilation continues.

## Tests Added/Updated
- Parser tests:
  - `Parse_Extracts_Conditional_Namespace_Metadata_For_Elements_And_Attributes`
  - `Parse_Reports_Invalid_Conditional_Namespace_Expression`
- Generator tests:
  - `ConditionalXaml_Skips_False_Branches_Before_Semantic_Type_Resolution`
  - `ConditionalXaml_Reports_Invalid_Conditional_Expression_Diagnostic`
- Existing pass-trace expectation updated for new transform ordering.

## Validation Commands
- `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj -v minimal`
- `dotnet build src/XamlToCSharpGenerator.Generator/XamlToCSharpGenerator.Generator.csproj -v minimal`
- `dotnet build samples/SourceGenXamlCatalogSample/SourceGenXamlCatalogSample.csproj -v minimal`
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -v minimal --filter "SimpleXamlDocumentParserTests|ConditionalXaml|Emits_Pass_Execution_Trace_When_Enabled"`

