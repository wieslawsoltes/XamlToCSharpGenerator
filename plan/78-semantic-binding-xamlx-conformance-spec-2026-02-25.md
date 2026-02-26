# Semantic Binding + XamlX Conformance Spec (2026-02-25)

## Objective
- Eliminate heuristic/hack behavior in semantic binding and compiler integration.
- Align behavior with XAML semantics and XamlX/Avalonia integration patterns.
- Encode each feature behind a dedicated standalone service with explicit contracts.

## Primary References
- `/Users/wieslawsoltes/GitHub/Avalonia/external/XamlX/src/XamlX/Transform/Transformers/ResolveContentPropertyTransformer.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/external/XamlX/src/XamlX/Transform/Transformers/ResolvePropertyValueAddersTransformer.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/external/XamlX/src/XamlX/Transform/Transformers/XamlIntrinsicsTransformer.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/external/XamlX/src/XamlX/Transform/XamlTransformHelpers.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/AvaloniaXamlIlCompiler.cs`

## Feature Matrix

| Feature | XamlX / XAML semantic baseline | Pre-change heuristic/hack | Service contract | Implementation in this change | Status |
|---|---|---|---|---|---|
| `x:Type` expression resolution | `XamlIntrinsicsTransformer` resolves `x:Type` via explicit argument/value shape and namespace resolution | `ExtractTypeToken(...)` string slicing in binder | `XamlTypeExpressionResolutionService.ResolveTypeFromExpression(...)` parses markup AST and extracts semantic type token | Added `src/XamlToCSharpGenerator.Avalonia/Binding/XamlTypeExpressionResolutionService.cs`; binder now calls service only | Completed |
| Runtime XAML fragment fallback detection | XAML fragment must be well-formed XML fragment, not lexical shape | `IsLikelyXamlFragment(...)` checked only first/last char | `RuntimeXamlFragmentDetectionService.IsValidFragment(...)` performs XML fragment parse (`ConformanceLevel.Fragment`) | Added `src/XamlToCSharpGenerator.Avalonia/Binding/RuntimeXamlFragmentDetectionService.cs`; binder uses it in `TryBuildRuntimeXamlFragmentExpression(...)` | Completed |
| Hot-design artifact classification | Artifact kind must be derived from semantic document/root type contracts | Emitter inferred theme/style scope via class-name suffix (`EndsWith("Theme")`) | `HotDesignArtifactClassificationService.Classify(...)` classifies from root XML type + resolved semantic collections + type assignability | Added `src/XamlToCSharpGenerator.Avalonia/Binding/HotDesignArtifactClassificationService.cs`; binder stores explicit artifact kind/scope hints in `ResolvedViewModel`; emitter consumes model metadata | Completed |
| Collection content materialization for adders | Content property + collection adder resolution should be centralized and typed | Inline-only method naming implied special-case behavior | `CollectionAddBindingService.TryCreateCollectionContentValue(...)` generic conversion + typed adder instruction | Renamed and kept logic in collection service; binder call-site migrated | Completed |
| Collection add method selection | `ResolvePropertyValueAddersTransformer`/`FindPossibleAdders` discover and rank adders deterministically | Previously duplicated adder decisions across binder paths | `CollectionAddBindingService.ResolveCollectionAddInstructions*` and `TryResolveCollectionAddInstruction(...)` | Centralized in service and wired for child/content/property-element paths | Completed |
| Owner-qualified property token semantics | Property-element identity is by `Owner.Property` semantics, not suffix matching | Direct suffix checks for `.Value`, `.Styles`, `.MergedDictionaries`; duplicated owner-property split logic | `XamlPropertyTokenSemantics` (`TrySplitOwnerQualifiedProperty`, `IsPropertyElementName`) | Added `/src/XamlToCSharpGenerator.Core/Parsing/XamlPropertyTokenSemantics.cs`; wired in binder and Avalonia document feature enricher, including attached-property, static-member, enum-token, and setter property-element paths | Completed |
| Qualified token split semantics (`owner.member`, `prefix:type`, `key:value`, assembly-qualified type token) | Token splitting rules must be explicit and reusable across binder paths | Repeated ad-hoc `IndexOf`/`LastIndexOf` + `Substring` parsing in binder and markup helpers | `XamlTokenSplitSemantics` (`TrySplitAtFirstSeparator`, `TrySplitAtLastSeparator`, `TrimTerminalSuffix`) | Added `/src/XamlToCSharpGenerator.Core/Parsing/XamlTokenSplitSemantics.cs`; binder/markup-helper paths now use it for static member resolution, property-field suffix normalization, prefixed type-name parsing, style query parsing, and conditional type token assembly trimming | Completed |
| Text collection content materialization | Collection add path should prefer typed text adders and deterministic conversion; framework-specific coercions must be explicit | Implicit fallback to `Run` object creation in collection service | `CollectionAddBindingService.TryBuildCollectionTextValueNode(...)` with text-adder-preferred candidate resolution | Removed `Run` fallback heuristic and added deterministic text-candidate selection (`string` first, then semantic fallback) | Completed |
| De-hack guard policy enforcement | Contract-level guardrails should fail when banned helpers reappear | No guardrails for fragment/type/theme heuristics | Source-guard tests assert no banned helper patterns | Extended `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs` with new guards | Completed |
| Runtime binding cast-path normalization (`(Type).Path`) | Runtime binding path normalization should parse and validate cast-prefix syntax semantically | Inline ad-hoc cast-token parsing in binder (`IndexOf(')')` + `Substring`) | `XamlRuntimeBindingPathSemantics` (`TrySplitTypeCastPrefix`, `IsTypeCastToken`, `NormalizePath`) | Added `/src/XamlToCSharpGenerator.Core/Parsing/XamlRuntimeBindingPathSemantics.cs`; binder delegates normalization to service | Completed |
| Template directive materialization (`x:DataType`) | Template directives should map to runtime template contract where required | `x:DataType` was compile-time-only, not materialized to `DataTemplate.DataType` at runtime | Binder-level directive materialization in `TryAddTemplateDataTypeDirectiveAssignment(...)` | `AvaloniaSemanticBinder` now emits CLR assignment for `DataTemplate.DataType` when `x:DataType` is present and no explicit `DataType` property is set | Completed |
| Style include resolution in style collections | Include resolution should be deterministic and source-generated first, not deferred to runtime XAML loader | `StyleInclude` instances were added directly to style collections, causing runtime precompiled-XAML failures | Emitter include-application contract `__TryApplyStyleInclude(...)` | Emitter now resolves `StyleInclude` via `AvaloniaSourceGeneratedXamlLoader` (fallback `Loaded`) and adds resolved style atomically before collection fallback | Completed |
| XML namespace URI semantics (`clr-namespace:` / `using:`) | Namespace URI parsing should be centralized and reused by binders/type resolution | Repeated prefix/substring parsing in NoUI binder and deterministic type resolver | `XamlXmlNamespaceSemantics` (`TryExtractClrNamespace`, `TryBuildClrNamespaceMetadataName`) | Added `/src/XamlToCSharpGenerator.Core/Parsing/XamlXmlNamespaceSemantics.cs`; wired into NoUI binder and deterministic type resolution semantics | Completed |
| Type token qualifier semantics (`global::`, `x:`) | Type-token qualifier normalization should be centralized across resolution paths | Repeated inline qualifier trimming in binder (`StartsWith("global::")`, `StartsWith("x:")`) | `XamlTypeTokenSemantics` (`TrimGlobalQualifier`, `TrimXamlDirectivePrefix`) | Added `/src/XamlToCSharpGenerator.Core/Parsing/XamlTypeTokenSemantics.cs`; binder now uses service in metadata normalization, conditional type resolution, intrinsic type token resolution, and recursive type token flow | Completed |
| Merged dictionary include application semantics | `ResourceDictionary.MergedDictionaries` should attach providers to preserve nested include/theme behavior | Include helper flattened dictionaries by key copy, potentially dropping provider graph semantics | Emitter include application in `__TryApplyMergedResourceInclude(...)` | Updated helper to add loaded `IResourceDictionary`/`IResourceProvider` directly to `MergedDictionaries` (with fallback only for plain dictionaries) | Completed |
| Expression markup classification semantics (`{=...}` and implicit expression markup) | Expression-vs-markup-extension classification should be centralized, parser-first, and deterministic | Inline heuristic token-shape checks in binder partial (`StartsWith`, known-name checks, local parse gate) | `CSharpExpressionClassificationService.TryParseCSharpExpressionMarkup(...)` | Added `/src/XamlToCSharpGenerator.Avalonia/Binding/CSharpExpressionClassificationService.cs`; binder now delegates expression markup detection/classification to service | Completed |
| Type-resolution fallback policy orchestration | Fallback ordering (default namespace, project namespace, extension suffix) should be centralized as an explicit policy service | Duplicated fallback branch chains in `ResolveTypeToken(...)` and `ResolveTypeSymbol(...)` | `TypeResolutionPolicyService` (`TryResolveTokenFallback`, `TryResolveXmlNamespaceFallback`) | Added `/src/XamlToCSharpGenerator.Avalonia/Binding/TypeResolutionPolicyService.cs`; binder now calls policy service instead of inline fallback chains | Completed |
| List and member-path token semantics | List/token and member-path splitting should be centralized and shared between binder and event binding semantics | Inline `Split(...)` and local list/path parsing in binder and event-binding semantics | `XamlListValueSemantics` + `XamlMemberPathSemantics` | Added `/src/XamlToCSharpGenerator.Core/Parsing/XamlListValueSemantics.cs` and `/src/XamlToCSharpGenerator.Core/Parsing/XamlMemberPathSemantics.cs`; wired into binder class token/materialization paths, member-path resolution, and event-binding path semantics | Completed |

## New/Updated Contracts
- `src/XamlToCSharpGenerator.Core/Models/ResolvedHotDesignArtifactKind.cs`
- `src/XamlToCSharpGenerator.Core/Models/ResolvedViewModel.cs`:
  - `HotDesignArtifactKind`
  - `HotDesignScopeHints`

## Validation Additions
- `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`
  - bans lexical fragment heuristic helper
  - bans string-slicing `x:Type` helper
  - bans emitter theme class-name heuristic helper
- `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`
  - malformed fragment does not trigger runtime XAML fallback
  - `TargetType="{x:Type TypeName=...}"` resolves to expected CLR type
  - `DataTemplate x:DataType` materializes runtime `DataType` assignment
- `tests/XamlToCSharpGenerator.Tests/Generator/XamlPropertyTokenSemanticsTests.cs`
  - owner-qualified split and property-element matching rules
- `tests/XamlToCSharpGenerator.Tests/Generator/XamlRuntimeBindingPathSemanticsTests.cs`
  - cast-prefix split, token validation, and normalized runtime binding paths
- `tests/XamlToCSharpGenerator.Tests/Generator/XamlXmlNamespaceSemanticsTests.cs`
  - `clr-namespace:`/`using:` extraction and metadata-name construction
- `tests/XamlToCSharpGenerator.Tests/Generator/XamlTypeTokenSemanticsTests.cs`
  - `global::` and `x:` qualifier normalization

## Non-Goals for this change
- Runtime hot-design property categorization heuristics (outside semantic binder/compiler scope of this change).
