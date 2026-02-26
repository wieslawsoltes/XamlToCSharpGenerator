# Semantic De-Hack Remaining Plan (2026-02-26)

## Scope
- Repository: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator`
- Focus: semantic binder + parsing/runtime binding semantics still carrying compatibility heuristics.
- Baseline references:
  - `/Users/wieslawsoltes/GitHub/Avalonia/external/XamlX/src/XamlX/Compiler/XamlCompiler.cs`
  - `/Users/wieslawsoltes/GitHub/Avalonia/external/XamlX/src/XamlX/Transform/Transformers/ResolveContentPropertyTransformer.cs`
  - `/Users/wieslawsoltes/GitHub/Avalonia/external/XamlX/src/XamlX/Transform/Transformers/ResolvePropertyValueAddersTransformer.cs`
  - `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/AvaloniaXamlIlCompiler.cs`
  - `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/Transformers/AvaloniaXamlIlResolveByNameMarkupExtensionReplacer.cs`
  - `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/Transformers/AddNameScopeRegistration.cs`
  - `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/Transformers/AvaloniaXamlIlBindingPathParser.cs`
  - `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/Transformers/AvaloniaXamlIlSetterTransformer.cs`

## Progress
- 2026-02-26 (slice A):
  - Implemented shared static-resource key parsing service:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.MiniLanguageParsing/Bindings/StaticResourceReferenceParser.cs`
  - Replaced duplicated lexical key extraction in:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.StylesTemplates.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime.Avalonia/XamlControlThemeRegistry.cs`
  - Added/updated guard tests:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/StaticResourceReferenceParserTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`
- 2026-02-26 (slice B):
  - Fixed fluent theme parity blocker by correcting `MergeResourceInclude` compile-time semantics in emitter helper generation.
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
  - Fixed runtime static-resource missing-resource contract:
    - non-Avalonia targets now rethrow `KeyNotFoundException`,
    - Avalonia targets keep `UnsetValue` behavior.
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenMarkupExtensionRuntime.cs`
  - Validation:
    - `FluentTheme_Runtime_Probe_Matches_Selected_SourceGen_And_XamlIl_Behavior` passes.
    - `SourceGenMarkupExtensionRuntimeTests` passes.
- 2026-02-26 (slice C):
  - Introduced strict-default markup parser mode and explicit compatibility opt-in.
    - parser default now strict:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/MarkupExpressionParser.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/MarkupExpressionParserOptions.cs`
    - new generator option + build property:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Models/GeneratorOptions.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.props`
    - binder/services now resolve parser behavior via active options instead of hard-coded legacy mode:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/Services/CSharpExpressionClassificationService.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/Services/XamlTypeExpressionResolutionService.cs`
  - Validation:
    - parser/framework/guard/runtime/fluent probe targeted tests pass.
- 2026-02-26 (slice D):
  - Replaced control-type name-scope registration heuristic with dedicated semantics service using typed `Avalonia.INamed` contract checks.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/Services/NameScopeRegistrationSemanticsService.cs`
    - binder integration:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.StylesTemplates.cs`
  - Added de-hack guard coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`
  - Validation:
    - guard tests and focused name-scope tests pass.
- 2026-02-26 (slice E):
  - Added canonical binding-source query parser model/service for `#name`, `$self`, `$parent[...]`:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.MiniLanguageParsing/Bindings/BindingSourceQuerySemantics.cs`
  - Refactored `BindingEventMarkupParser` to consume centralized query semantics wrappers instead of local token slicing logic.
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/BindingEventMarkupParser.cs`
  - Added tests + guard coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/BindingSourceQuerySemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`
  - Validation:
    - `BindingSourceQuerySemanticsTests`, `BindingEventMarkupParserTests`, guard suite, and key runtime/fluent parity tests pass.
- 2026-02-26 (slice F):
  - Replaced runtime binding deferral exception-message matching with typed deferred-failure classification.
    - updated:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenMarkupExtensionRuntime.cs`
    - classification reasons:
      - `DataContextUnavailable`
      - `TemplatedParentUnavailable`
      - `None`
  - Added guard coverage to prevent reintroduction of exception-message string matching.
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`
  - Validation:
    - runtime tests, guard tests, and fluent parity probe test pass.
- 2026-02-26 (slice G):
  - Implemented strict-first compatibility-fallback policy for strict mode defaults while preserving explicit opt-in escape hatch.
    - build/property defaults updated:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.props`
    - option materialization updated to honor strict-mode default when property is not explicitly set:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Models/GeneratorOptions.cs`
  - Added file-level compile metrics exposure for type-resolution compatibility fallback usage count.
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs`
  - Added/updated validation tests:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`
  - Validation:
    - focused fallback + metrics test subsets pass.
- 2026-02-26 (slice H):
  - Introduced centralized quoted-value semantics service and removed duplicated local quote helpers.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlQuotedValueSemantics.cs`
    - parser/list integrations:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/BindingEventMarkupParser.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlListValueSemantics.cs`
  - Added guard + unit coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlQuotedValueSemanticsTests.cs`
  - Validation:
    - parser/list/guard suites pass.
- 2026-02-26 (slice I):
  - Replaced studio property-grid markup-extension detection brace-shape heuristic with canonical markup-envelope semantics service.
    - new shared envelope service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.MiniLanguageParsing/Bindings/MarkupExpressionEnvelopeSemantics.cs`
    - parser/runtime adoption:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/MarkupExpressionParser.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime.Avalonia/XamlSourceGenHotDesignCoreTools.cs`
  - Added coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/MarkupExpressionEnvelopeSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/XamlSourceGenHotDesignCoreToolsTests.cs`
  - Validation:
    - focused parser/runtime/guard test subset passes.
- 2026-02-26 (slice J):
  - Completed global default flip for type-resolution compatibility fallback (`false` by default, explicit opt-in preserved).
    - default property value:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.props`
    - option materialization default:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Models/GeneratorOptions.cs`
  - Updated/expanded tests for new default contract and explicit opt-in behavior:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`
  - Validation:
    - fallback-focused test subset passes.
    - sample build validation passes:
      - `samples/SourceGenXamlCatalogSample/SourceGenXamlCatalogSample.csproj`
      - `samples/ControlCatalog.Desktop/ControlCatalog.Desktop.csproj`
  - Broad sweep status:
    - full `tests/XamlToCSharpGenerator.Tests` run still reports large pre-existing failure surface outside this slice.
- 2026-02-26 (slice K):
  - Added deterministic markup object-element type-resolution service for canonical extension object-node semantics (`StaticResource`/`DynamicResource`) without compatibility fallback dependence.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/Services/MarkupObjectElementTypeResolutionService.cs`
    - binder wiring:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.TypeResolution.cs`
  - Strengthened coverage for strict mode default path:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`
- 2026-02-26 (slice L):
  - Introduced canonical markup-extension name semantics service and removed duplicated manual casing/prefix/suffix matching in binder paths.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlMarkupExtensionNameSemantics.cs`
    - binder/service integrations:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.MarkupHelpers.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.Includes.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/Services/CSharpExpressionClassificationService.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/Services/XamlTypeExpressionResolutionService.cs`
  - Extended quoted-value semantics with canonical wrapped-literal detection and routed binder usage to the shared service.
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlQuotedValueSemantics.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
  - Added guard and unit coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlMarkupExtensionNameSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`
- 2026-02-26 (slice M):
  - Extracted Avalonia binding enum token mapping and style query token parsing into dedicated semantics services.
    - new services:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/Services/AvaloniaBindingEnumSemantics.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/Services/AvaloniaStyleQuerySemantics.cs`
    - binder integration:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
  - Replaced manual `x:Null` / `x:Reference` / `ResolveByName` string checks in binding/event parsing with canonical markup-extension classification.
    - parser integration:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/BindingEventMarkupParser.cs`
  - Added centralized accessibility modifier semantics and removed duplicated class/field modifier normalization switches.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlAccessibilityModifierSemantics.cs`
    - parser/binder integration:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/SimpleXamlDocumentParser.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.TypeResolution.cs`
  - Added/updated coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlAccessibilityModifierSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/BindingEventMarkupParserTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`
  - Validation:
    - focused de-hack/guard/parser suite passes.
    - full test suite passes (`661` passed, `1` skipped).
- 2026-02-26 (slice N):
  - Removed remaining manual markup-extension name string checks in `BindingEventMarkupParser` for:
    - `Binding` / `CompiledBinding`
    - `ReflectionBinding`
    - `EventBinding`
    - `RelativeSource`
  - Standardized these paths on `XamlMarkupExtensionNameSemantics.Classify(...)`.
    - updated:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/BindingEventMarkupParser.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlMarkupExtensionNameSemantics.cs`
  - Added/updated coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/BindingEventMarkupParserTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlMarkupExtensionNameSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`
  - Validation:
    - focused parser/semantics/guard tests pass.
    - full test suite passes (`663` passed, `1` skipped).
- 2026-02-26 (slice O):
  - Extracted EventBinding `Source` token mapping into dedicated semantics service.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/EventBindingSourceModeSemantics.cs`
    - parser integration:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/BindingEventMarkupParser.cs`
  - Extended parser + guard coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/EventBindingSourceModeSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/BindingEventMarkupParserTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`
  - Validation:
    - focused parser/guard semantics tests pass.
    - full test suite passes (`670` passed, `1` skipped).
- 2026-02-26 (slice P):
  - Extracted centralized scalar literal semantics service and replaced duplicated literal parsing in binder conversion paths.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlScalarLiteralSemantics.cs`
    - binder integrations:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
      - replaced repeated `null/bool/int/long/float/double/decimal` parsing in method-argument conversion, untyped conversion, typed conversion, array/index conversion, and enum numeric conversion paths.
  - Extracted TimeSpan literal parsing (including numeric-seconds fallback) into dedicated semantics service.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlTimeSpanLiteralSemantics.cs`
    - binder integration:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
  - Added/updated coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlScalarLiteralSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlTimeSpanLiteralSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`
  - Validation:
    - focused semantics/guard/generator subset passes.
    - full test suite passes (`684` passed, `1` skipped).
- 2026-02-26 (slice Q):
  - Replaced remaining manual markup-envelope slicing in expression classification with canonical envelope semantics.
    - updated:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/Services/CSharpExpressionClassificationService.cs`
    - now uses:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.MiniLanguageParsing/Bindings/MarkupExpressionEnvelopeSemantics.cs`
  - Added guard coverage for envelope semantics usage in expression classification:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`
  - Validation:
    - focused guard/generator/parser subset passes.
    - full test suite passes (`685` passed, `1` skipped).
- 2026-02-26 (slice R):
  - Extracted compiled-binding member segment parsing semantics from `CompiledBindingPathParser` into dedicated service.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.MiniLanguageParsing/Bindings/CompiledBindingPathSegmentSemantics.cs`
    - parser integration:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.MiniLanguageParsing/Bindings/CompiledBindingPathParser.cs`
    - de-hack details:
      - removed inline attached-property probing based on direct `IndexOf`/`Substring` scanning.
      - removed inline cast token scanning loops and routed cast parsing through semantic helper using balanced-content parser.
  - Added coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/CompiledBindingPathSegmentSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/MiniLanguageDeHackGuardTests.cs`
  - Validation:
    - focused mini-language/guard/generator subset passes.
    - full test suite passes (`691` passed, `1` skipped).
- 2026-02-26 (slice S):
  - Extracted conditional namespace method-call parsing semantics from `SimpleXamlDocumentParser`.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlConditionalExpressionSemantics.cs`
    - parser integration:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/SimpleXamlDocumentParser.cs`
    - de-hack details:
      - removed inline method-call token slicing (`IndexOf`, `LastIndexOf`, `Substring`) and inline argument tokenization for conditional expressions.
      - centralized method-name normalization (`ApiInformation.` prefix), argument normalization/unquoting, and arity validation.
  - Added coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlConditionalExpressionSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/CoreParsingDeHackGuardTests.cs`
  - Validation:
    - focused parser/conditional/guard/generator subset passes.
    - full test suite passes (`697` passed, `1` skipped).
- 2026-02-26 (slice T):
  - Extracted `x:TypeArguments` tokenization into dedicated semantics service and removed parser-local token scanner.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlTypeArgumentListSemantics.cs`
    - parser integration:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/SimpleXamlDocumentParser.cs`
  - Added guard + unit coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlTypeArgumentListSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/CoreParsingDeHackGuardTests.cs`
  - Validation:
    - focused parser/guard subset passes.
- 2026-02-26 (slice U):
  - De-hacked runtime binding cast-prefix parsing by replacing manual parenthesis slicing with centralized compiled-binding cast segment semantics.
    - updated:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlRuntimeBindingPathSemantics.cs`
  - Added guard coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/CoreParsingDeHackGuardTests.cs`
  - Validation:
    - focused runtime-path/guard subset passes.
- 2026-02-26 (slice V):
  - De-hacked `$parent[...]` binding-source query descriptor parsing by replacing manual bracket/index slicing with balanced-content parsing and top-level separator semantics.
    - updated:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.MiniLanguageParsing/Bindings/BindingSourceQuerySemantics.cs`
  - Added coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/BindingSourceQuerySemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/MiniLanguageDeHackGuardTests.cs`
  - Validation:
    - focused binding-source/parser/guard subset passes.
    - full test suite passes (`705` passed, `1` skipped).
- 2026-02-26 (slice W):
  - Extracted enum-flag and collection literal tokenization into reusable delimiter semantics service and removed binder-local split heuristics.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlDelimitedValueSemantics.cs`
    - binder integration:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
    - de-hack details:
      - replaced manual `Split`/trim/remove-empty loops and ad-hoc top-level comma wrapper with centralized semantic tokenization methods.
  - Added/updated coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlDelimitedValueSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`
  - Validation:
    - focused list-semantics/guard/parser subset passes.
    - full test suite passes (`709` passed, `1` skipped).
- 2026-02-26 (slice X):
  - Extracted markup-extension head/argument classification semantics into a dedicated parser service and removed parser-local argument slicing heuristics.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlMarkupArgumentSemantics.cs`
    - parser integration:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/MarkupExpressionParser.cs`
    - de-hack details:
      - removed inline head token extraction and inline named-argument splitting from `MarkupExpressionParser`.
      - centralized top-level argument tokenization and named-argument status classification (`Parsed` / `LeadingEquals` / `EmptyName` / `None`).
  - Added/updated coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlMarkupArgumentSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/CoreParsingDeHackGuardTests.cs`
  - Validation:
    - focused markup parser + guard subset passes.
- 2026-02-26 (slice Y):
  - Replaced conditional namespace URI shape heuristics with canonical conditional method-call semantics classification.
    - updated:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlConditionalNamespaceUtilities.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlConditionalExpressionSemantics.cs`
    - de-hack details:
      - `TrySplitConditionalNamespaceUri(...)` now uses top-level separator parsing + `TryParseMethodCallShape(...)` instead of ad-hoc `EndsWith(')')`/`IndexOf('(')` checks.
      - preserves parser diagnostic flow for unsupported conditional methods by splitting on valid method-call shape, not only fully supported conditions.
  - Added/updated coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlConditionalNamespaceUtilitiesTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlConditionalExpressionSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/CoreParsingDeHackGuardTests.cs`
  - Validation:
    - focused conditional/parser/guard subset passes.
    - full test suite passes (`721` passed, `1` skipped).
- 2026-02-26 (slice Z - review findings remediation):
  - Fixed named-argument parsing normalization contract in markup semantics service.
    - issue:
      - `TryParseNamedArgument(...)` did not trim input consistently before classifying leading `=` cases.
    - fix:
      - normalize/trim token before top-level `=` classification; preserve deterministic output for fallback paths.
    - updated:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlMarkupArgumentSemantics.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlMarkupArgumentSemanticsTests.cs`
  - Fixed split-options contract in delimited collection semantics for empty/null separators.
    - issue:
      - `SplitCollectionItems(...)` forced trim/remove-empty when separators were empty, ignoring caller `StringSplitOptions`.
    - fix:
      - honor `StringSplitOptions` consistently for all separator modes.
    - updated:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlDelimitedValueSemantics.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlDelimitedValueSemanticsTests.cs`
  - Validation:
    - focused semantics/parser/guard subset passes.
    - full test suite passes (`722` passed, `1` skipped).
- 2026-02-26 (slice AA - review findings remediation follow-up):
  - Fixed split-options parity for comma/top-level path in delimiter semantics service.
    - issue:
      - comma-separator branch was still globally trimming input before tokenization, which could violate `StringSplitOptions.None` semantics.
    - fix:
      - removed global pre-trim and whitespace-empty fast-path from `SplitCollectionItems(...)` and `SplitTopLevelTokens(...)` so caller `StringSplitOptions` fully controls trim/remove-empty behavior.
    - updated:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlDelimitedValueSemantics.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlDelimitedValueSemanticsTests.cs`
  - Validation:
    - focused semantics/parser/guard subset passes.
    - full test suite passes (`723` passed, `1` skipped).
- 2026-02-26 (slice AB):
  - Added canonical property-element semantics service and removed parser-local owner-qualified token heuristics.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlPropertyElementSemantics.cs`
    - parser integration:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/SimpleXamlDocumentParser.cs`
    - de-hack details:
      - replaced `IndexOf('.') >= 0` checks for attached/property-element classification with typed token semantics.
  - Added canonical event-handler name semantics service and removed binder-local string-shape checks.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlEventHandlerNameSemantics.cs`
    - binder integration:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
    - de-hack details:
      - replaced local `StartsWith("{")`/`IndexOf('.')`/manual identifier checks with centralized semantics service.
  - Added/updated coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlPropertyElementSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlEventHandlerNameSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/CoreParsingDeHackGuardTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`
  - Validation:
    - focused parser/semantics/guard subset passes.
    - full test suite passes (`732` passed, `1` skipped).
- 2026-02-26 (slice AC):
  - Added centralized identifier token semantics service and removed parser-local identifier checks.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlIdentifierSemantics.cs`
    - integrations:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/EventBindingPathSemantics.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlEventHandlerNameSemantics.cs`
    - de-hack details:
      - replaced duplicated `char.IsLetter`/`char.IsLetterOrDigit` identifier loops with canonical `XamlIdentifierSemantics`.
  - Added centralized resolve-by-name reference token normalization service and removed parser-local whitespace-shape checks.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlReferenceNameSemantics.cs`
    - integrations:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/BindingEventMarkupParser.cs`
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlWhitespaceTokenSemantics.cs` (new `ContainsWhitespace(...)` helper).
  - Added/updated coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlIdentifierSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlReferenceNameSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/BindingEventMarkupParserTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlWhitespaceTokenSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/CoreParsingDeHackGuardTests.cs`
  - Validation:
    - focused semantics/parser/guard subset passes (`68` passed, `0` failed).
    - full test suite passes (`759` passed, `1` skipped).
- 2026-02-26 (slice AD):
  - Added centralized property-reference token normalization semantics service and removed selector binder-local parenthesized token slicing.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlPropertyReferenceTokenSemantics.cs`
    - integrations:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.SelectorPropertyReferences.cs`
    - de-hack details:
      - replaced local `Trim` + `token[0] == '('` + `Substring` normalization with canonical `TryNormalize(...)`.
  - Removed selector predicate resolver-local quote-unwrapping helper and standardized on canonical quoted-value semantics.
    - updated:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSelectorPropertyPredicateResolver.cs`
    - de-hack details:
      - replaced local `Unquote(...)` method with `XamlQuotedValueSemantics.UnquoteWrapped(...)`.
  - Added/updated coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlPropertyReferenceTokenSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`
  - Validation:
    - focused semantics/parser/guard subset passes (`83` passed, `0` failed).
    - full test suite passes (`768` passed, `1` skipped).
- 2026-02-26 (slice AE):
  - Added centralized selector-property predicate semantics service and removed selector predicate syntax local parsing heuristics.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.MiniLanguageParsing/Selectors/SelectorPropertyPredicateSemantics.cs`
    - integration:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.MiniLanguageParsing/Selectors/SelectorPropertyPredicateSyntax.cs`
    - de-hack details:
      - replaced local `Substring`/`LastIndexOf`/`StartsWith`/`EndsWith` parsing for predicate split and attached-property token parsing with canonical semantic helpers.
      - attached-property envelope parsing now uses balanced-content parsing + top-level separator handling.
  - Added/updated coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/SelectorPropertyPredicateSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/MiniLanguageDeHackGuardTests.cs`
  - Validation:
    - focused mini-language/guard subset passes (`78` passed, `0` failed).
    - full test suite passes (`775` passed, `1` skipped).
- 2026-02-26 (slice AF):
  - Added centralized conditional method-call parsing semantics and removed conditional expression-local method-call split heuristics.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlConditionalMethodCallSemantics.cs`
    - integration:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlConditionalExpressionSemantics.cs`
    - de-hack details:
      - replaced local `IndexOf('(')` + `Substring` + prefix slicing with canonical balanced-content method-call parser.
      - centralized `ApiInformation.` method prefix normalization under semantic service contract.
  - Added/updated coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlConditionalMethodCallSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/CoreParsingDeHackGuardTests.cs`
  - Validation:
    - focused conditional/parser/guard subset passes (`22` passed, `0` failed).
    - full test suite passes (`779` passed, `1` skipped).
- 2026-02-26 (slice AG):
  - Added centralized conditional-namespace URI split semantics and removed conditional namespace utility-local split heuristics.
    - new service:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlConditionalNamespaceUriSemantics.cs`
    - integration:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/XamlConditionalNamespaceUtilities.cs`
    - de-hack details:
      - replaced local `TopLevelTextParser.IndexOfTopLevel(..., '?')` + `Substring` token slicing with canonical split service.
      - kept method-call shape validation in `XamlConditionalExpressionSemantics` unchanged.
  - Added/updated coverage:
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/XamlConditionalNamespaceUriSemanticsTests.cs`
    - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/CoreParsingDeHackGuardTests.cs`
  - Validation:
    - focused conditional/parser/guard subset passes (`25` passed, `0` failed).
    - full test suite passes (`785` passed, `1` skipped).
- 2026-02-26 (slice AH - review remediation):
  - Review finding fixed in selector predicate semantics:
    - issue:
      - `FindLastTopLevelSeparator(...)` used reverse scanning with quote/depth state, which is harder to reason about and more error-prone around nested token shapes.
    - fix:
      - replaced reverse scanner with deterministic forward top-level separator tracking.
    - updated:
      - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.MiniLanguageParsing/Selectors/SelectorPropertyPredicateSemantics.cs`
  - Validation:
    - focused mini-language subset passes (`37` passed, `0` failed).
    - full test suite passes (`785` passed, `1` skipped).

## Current Findings (Ordered)
- No open de-hack regressions are currently reproduced in `tests/XamlToCSharpGenerator.Tests`.
- Current baseline validation status:
  - `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj` passes (`785` passed, `1` skipped).

## Remaining Implementation Plan

## Phase 1: Re-baseline parity and unblock (P0)
1. Fix fluent theme resource-count parity before further semantic de-hack refactors.
2. Restore/clarify missing-resource behavior contract in runtime static resource tests.
3. Keep this phase limited to parity bugs and test stabilization (no API churn yet).

## Phase 2: Canonical parse/quote contracts
1. Add `XamlQuotedValueSemantics` in Core and remove local `Unquote` duplicates from binder/runtime parsers.
2. Introduce strict parser mode default in `MarkupExpressionParser`; move legacy invalid-argument parsing behind explicit compatibility option.
3. Thread parser mode through binder options (strict mode => strict parser; compatibility explicit opt-in).

## Phase 3: Name/Reference semantics parity
1. Add `NameScopeRegistrationSemanticsService` based on resolved property symbol contract (`Name` + `INamed`-compatible target), mirroring upstream `AddNameScopeRegistration`.
2. Replace `SupportsNameScopeRegistrationFromNameProperty` control-type heuristic.
3. Keep `ResolveByName` conversion on typed markup/reference token model only.

## Phase 4: Resource key semantics unification
1. Add `StaticResourceKeySemanticsService` (parse `StaticResource`/`DynamicResource` key expression via canonical parser).
2. Replace both:
   - binder `TryExtractControlThemeBasedOnKey(...)`
   - runtime `TryExtractBasedOnKey(...)`
3. Add differential tests asserting compile/runtime key extraction equivalence.

## Phase 5: Binding source grammar service
1. Add `BindingSourceQuerySemantics` in MiniLanguage/Core parser with explicit node model for:
   - element source query
   - self source query
   - parent source query (type + level)
2. Refactor `BindingEventMarkupParser` to consume this service instead of manual `Substring/IndexOf/Split`.
3. Keep existing syntax compatibility but make parse deterministic and testable by AST node shape.

## Phase 6: Runtime binding deferral de-heuristic
1. Replace exception-message classification in `SourceGenMarkupExtensionRuntime` with typed precondition/classification service:
   - target kind (styled/non-styled)
   - binding source mode
   - anchor availability
   - templated-parent availability
2. Keep retries but drive scheduling policy from typed classification result.
3. Add tests for DataContext-missing and TemplatedParent-missing paths without message matching.

## Phase 7: Strict-first type resolution rollout
1. Add explicit diagnostics when compatibility fallback path is used (already partially present) and expose count metrics.
2. Switch default for compatibility fallback to `false` in strict CI profile first, then globally after sample validation.
3. Keep escape hatch property for existing apps.

## Phase 8: Guard tests expansion
1. Add de-hack guard tests for:
   - no exception-message substring checks in runtime binding deferral path,
   - no duplicate resource-key parsing helpers in binder/runtime,
   - no local quote/unquote helpers in semantic paths,
   - no manual `#/$parent` parsing outside `BindingSourceQuerySemantics`.
2. Keep guards narrow and file-targeted to avoid false positives.

## Acceptance Criteria
1. Full `tests/XamlToCSharpGenerator.Tests` passes, including:
   - `FluentThemeComparisonTests`
   - `SourceGenMarkupExtensionRuntimeTests`
   - `ReflectionGuardTests`
2. Theme resource parity (`Theme.ResourceCount`, merged dictionaries, key lookup) matches XamlIl probe.
3. NameScope/ResolveByName behavior matches typed contract scenarios (attribute, property assignment, template scope).
4. No semantic decisions in targeted paths rely on string-shape heuristics or exception-message text.

## Granular Delivery Sequence
1. PR1: parity unblock fixes (Fluent/resource/static-resource regression).
2. PR2: canonical quote + parser strict/compat mode.
3. PR3: namescope/reference semantic service.
4. PR4: static-resource key semantics shared service.
5. PR5: binding source query grammar service.
6. PR6: runtime binding deferral classifier service.
7. PR7: strict-first fallback default + diagnostics + guard tests.
