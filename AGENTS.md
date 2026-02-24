# Engineering Guide (AGENTS)

This document defines the architectural and coding rules for the project. It is
authoritative for all new code and refactors.

## 1) Core principles (non-negotiable)

### SOLID (strict)
- Single Responsibility: every class has exactly one reason to change.
- Open/Closed: extend behavior via composition and interfaces; avoid modifying
  stable code paths when adding features.
- Liskov Substitution: derived types must be safely substitutable without
  altering expected behavior or contract.
- Interface Segregation: prefer small, focused interfaces; avoid "god" interfaces.
- Dependency Inversion: depend on abstractions; wire concrete types in the
  composition root only.

### MVVM (strict)
- Views are passive. No UI logic in code-behind beyond `InitializeComponent()`.
- All inputs are routed to ViewModels via bindings, commands, and behaviors.
- ViewModels are UI-framework agnostic and unit-testable.
- Models and services contain business logic and data access; ViewModels orchestrate
  them via DI.
- Prefer composition in ViewModels/services/code over inheritance wherever possible,
  except where framework base types are required (e.g., `ReactiveObject` for ViewModels).

## 2) Architecture

### Layering
- UI (Avalonia Views + XAML): visual composition only.
- Presentation (ViewModels): state, commands, reactive composition.
- Domain/Services: business logic, parsing, validation, domain rules.
- Infrastructure: file system, persistence, external integrations.

### Boundaries
- UI depends on Presentation; Presentation depends on Domain; Infrastructure is
  depended on by Domain or Presentation via interfaces.
- No reference from Domain to UI or Avalonia types.

## 3) Avalonia UI best practices (aligned with Avalonia codebase)

Reference: https://github.com/AvaloniaUI/Avalonia

### Views and styling
- Use XAML for layout and visuals; avoid creating controls in code.
- Define styles and resources in dedicated resource dictionaries and merge them
  in `App.axaml` to keep styling consistent and maintainable.
- Prefer `StaticResource` for immutable resources and `DynamicResource` when
  runtime updates are required.

### Data binding
- Use compiled bindings only (no reflection bindings) with explicit `x:DataType` on
  all binding scopes (views, DataTemplates, control themes, and resources).
- Keep bindings one-way unless user input must update the ViewModel.
- Use `DataTemplates` or a `ViewLocator` (custom, non-reflection) for view lookup.

### Custom controls
- Use `StyledProperty` only for values that must participate in styling.
- Prefer `DirectProperty` for non-styled properties to avoid extra overhead.
- For best UI/UX, prefer custom control creation or re-templating using control themes
  instead of CRUD-style UI.

## 4) ReactiveUI (required)

Reference: https://github.com/reactiveui/ReactiveUI

### ViewModel base
- All ViewModels inherit from `ReactiveObject`.
- Use `ReactiveCommand` for commands; never use event handlers in code-behind.
- Use `WhenAnyValue`, `ObservableAsPropertyHelper`, and `Interaction<TIn,TOut>`
  to model state, derived values, and dialogs.
- Use `ReactiveUI.SourceGenerators` for INPC/ReactiveObject boilerplate where applicable.
  https://github.com/reactiveui/ReactiveUI.SourceGenerators

### Navigation (ReactiveUI routing)
- Use `IScreen` with a single `RoutingState` as the navigation root.
- All navigable ViewModels implement `IRoutableViewModel`.
- Views host navigation via `RoutedViewHost`.
- Use route segments that are stable, explicit, and testable.

### Avalonia integration
- Use `ReactiveUI.Avalonia` (latest) and do not use `Avalonia.ReactiveUI` directly.
- If a third-party dependency requires `Avalonia.ReactiveUI` (e.g., Dock integration),
  isolate it to the docking layer and do not reference it from app UI code.
  https://github.com/reactiveui/ReactiveUI.Avalonia

## 5) Input and interaction via Xaml.Behaviors (required)

Reference: https://github.com/wieslawsoltes/Xaml.Behaviors

- All UI input and events are handled via behaviors/triggers.
- Prefer source-generator-based behaviors/actions (no reflection) wherever available.
- Use trigger behaviors (property, data, loaded/unloaded, routed event) for
  lifecycles and state transitions.
- Code-behind must not contain event handlers or direct ViewModel calls.

## 6) Docking layout with Dock for Avalonia (required)

Reference: https://github.com/wieslawsoltes/Dock

- Use Dock.Model.* to represent the docking layout state.
- Use Dock.Avalonia for the view layer and Dock.Model.ReactiveUI for MVVM
  integration.
- Persist layout state to user settings and restore on startup.
- Keep layout logic in ViewModels; Views only render the Dock model.

## 7) Text editing with AvaloniaEdit (required)

Reference: https://github.com/AvaloniaUI/AvaloniaEdit

- Use AvaloniaEdit `TextEditor` for all code/text editing surfaces.
- Enable syntax highlighting using TextMate grammars/themes.
- Keep editor configuration in ViewModels (options, text, selection) and bind to
  the view.

## 8) Data presentation with ProDataGrid (required)

Reference: https://github.com/wieslawsoltes/ProDataGrid

- Use ProDataGrid `DataGrid` for all tabular data, tree views, and list displays.
- Always use the ProDataGrid model approach with code-based column bindings and
  fast paths.
- Always enable full filtering, searching, and sorting support.

## 9) Dependency Injection (Microsoft.Extensions.DependencyInjection)

- Configure services in a single composition root (App startup).
- Use `AddSingleton`, `AddScoped`, `AddTransient` correctly:
  - Singleton: thread-safe, shared, expensive-to-create services.
  - Scoped: per-document or per-operation services created within explicit scopes.
  - Transient: stateless lightweight services.
- Never resolve scoped services from singletons without creating a scope.
- Do not dispose services resolved from the container manually.

## 10) Performance (required)

- Prefer allocation-free APIs: `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>`,
  `ValueTask`, `ArrayPool<T>`, and `System.Buffers`.
- Use SIMD (`System.Numerics.Vector<T>` or hardware intrinsics) where it provides
  measurable wins and keeps code maintainable.
- Avoid LINQ in hot paths; use loops and pre-sized collections.
- Minimize boxing, virtual dispatch in tight loops, and avoid unnecessary
  allocations in render/update loops.
- Profile before and after optimizations; document expected gains.

## 11) Reflection, source generation, and AOT compatibility (required)

These rules are non-negotiable for production compiler/runtime paths.

### Reflection usage policy (strict)
- No reflection in source-generator emitted code. This is mandatory.
- No reflection in runtime helpers executed by emitted code (loader, markup-extension
  runtime, binding/runtime assignment helpers, hot-reload apply paths).
- Forbidden in emitted/runtime paths: `Type.GetType`, `GetProperty`/`GetField`/`GetMethod`,
  `PropertyInfo.SetValue`, `MethodInfo.Invoke`, `Activator.CreateInstance`, `dynamic`,
  runtime expression compilation, runtime IL emit, or any equivalent late-bound invocation.
- Implement behavior through compile-time semantic binding and strongly typed emitted calls,
  delegates, and registries.
- Any change introducing reflection into emitted/runtime paths must be rejected in review.

### AOT and trimming policy (strict)
- Generated code and runtime support code must be fully NativeAOT/trimming compatible.
- Do not depend on dynamic code generation or runtime assembly scanning/loading in emitted/runtime paths.
- Do not add production-path dependencies on APIs requiring dynamic code or unreferenced
  member preservation unless the call site is fully compile-time guarded away from AOT targets.
- Compiler parity work must preserve this contract: feature completeness cannot be achieved by
  falling back to reflection.

### Allowed scope for reflection
- Reflection is allowed only in tests, diagnostics tooling, or development-only utilities that
  are outside production runtime and emitted-code execution paths.

## 12) Testing and validation

References:
- https://github.com/AvaloniaUI/Avalonia
- https://docs.avaloniaui.net/docs/concepts/headless/

- All production code must be covered by unit tests; xUnit is required for unit testing.
- UI tests must use Avalonia Headless (xUnit integration) and follow the headless testing
  guidance and helpers for input simulation.
- Unit-test ViewModels and Domain services.
- Use integration tests for parsing, IO, and docking layout persistence.
- UI tests should validate navigation flows, docking, and editor behaviors.

## 13) Code conventions

- No code-behind event handlers.
- Avoid static state (except truly immutable constants).
- Prefer explicit types where clarity is improved; avoid `var` in public APIs.
- All public APIs must be documented and unit-tested.

## 14) Fluent icons path data (required)

References:
- https://github.com/microsoft/fluentui-system-icons
- https://www.npmjs.com/package/@fluentui/svg-icons

- Use Microsoft Fluent System Icons for all `PathIcon`/`IconPathData` geometry.
- Do not hand-draw or invent SVG path strings for product UI icons.
- Prefer `regular` variants for standard UI chrome.
- Prefer `20` size source icons for toolbar usage; render using the existing control size in XAML.
- Keep icon path data in named constants (for example `OpenFolderIconPath`) and reuse it.

### How to get Fluent icon path data

1. Pick an icon name from the Fluent icon catalog (for example `folder_open_20_regular`).
2. Download the SVG from npm/unpkg, for example:
   `https://unpkg.com/@fluentui/svg-icons@<version>/icons/<icon-name>.svg`
3. Copy the value of the SVG `<path d="..."/>` attribute exactly.
4. Use that value directly in `PathIcon.Data` or extension `IconPathData`.
5. If an icon contains multiple `<path>` elements, choose a Fluent icon variant with a single path for `PathIcon.Data`, or compose a geometry only when needed.

### CLI workflow (example)

```bash
npm view @fluentui/svg-icons version
npm pack @fluentui/svg-icons@<version>
tar -xzf fluentui-svg-icons-<version>.tgz
cat package/icons/folder_open_20_regular.svg
```

Then copy the `d` attribute from the `<path>` element into code.

## 15) SourceGen XAML compiler parity guardrails (required)

These rules prevent regressions in the source-generator compiler pipeline.

### Parser and semantic model rules
- Preserve owner-qualified property tokens exactly as authored in XAML (`Owner.Property`).
  Do not strip owner prefixes in the parser.
- Keep design-time-only members (`Design.*`, ignored design namespaces) out of runtime
  semantic binding and runtime code emission.
- Keep diagnostics location-accurate (file, line, column) and deterministic.

### Property element binding order (strict)
- Property-element binding must follow this order:
  1. Skip design-only members.
  2. Handle explicit child-attachment aliases (`Content`, `Children`, `Items`).
  3. Bind object values.
  4. Resolve attached-property elements from owner-qualified tokens.
  5. Handle dictionary-merge semantics.
  6. Handle collection-add semantics.
  7. Handle Avalonia-property assignment.
  8. Fall back to CLR settable-property assignment.
- Never run scalar CLR setter cardinality checks before collection/add and Avalonia-property
  checks, otherwise valid multi-item collection property elements regress.

### Style and ControlTheme setter resolution
- For setter `Property` tokens, resolve owner-qualified attached-property tokens first.
- If attached Avalonia property resolution succeeds, do not emit missing CLR-property
  diagnostics (`AXSG0301`/`AXSG0303`) for that setter.
- Use resolved Avalonia property metadata for value type conversion and duplicate
  setter detection.

### Binding emission safety rules
- If a value is binding-like (`Binding`, `MultiBinding`, compiled/runtime binding expression),
  emit Avalonia binding assignment via indexer descriptor path, not CLR casts.
- Do not emit direct typed CLR assignment for binding-like values (`(bool)binding`,
  `(string)binding`, etc.).
- Treat both inline markup-extension bindings and object-element binding nodes as binding-like.

### Warning policy and parity intent
- Treat warning reduction as semantic parity work, not blanket suppression.
- Preserve strict diagnostics where parity requires explicit author intent
  (`x:DataType` for compiled bindings, template validation, etc.).
- Do not downgrade warnings to hide unsupported behavior; either implement behavior
  or keep the warning actionable.

## 16) Hot reload and incremental generator reliability (required)

### Incremental generator stability
- Generated hint names must be unique and stable per logical XAML input.
- Repeated runs and partial rebuilds must not emit duplicate hint names.
- AdditionalFiles discovery must avoid duplicate logical inputs and editor backup files.

### Hot reload error resilience
- Hot reload path must be tolerant to transient invalid XAML while editing.
- On parse/semantic failure during hot reload, keep last known good generated output;
  do not apply partial invalid graph updates.
- Resume normal hot reload application automatically once XAML becomes valid again.

### Hot reload apply/revert semantics
- Runtime hot reload must track and clean up removable graph artifacts (styles, resources,
  templates, merged dictionaries, theme entries) when they are removed from XAML.
- Applying an edit and then removing it must converge to the same runtime state as the
  original baseline.
- For app-specific side effects outside generated graph, use explicit hot-reload handler
  policies rather than implicit best-effort cleanup.

### Minimal-diff live editing (strict)
- Hot design and related editing tools must compute and propagate minimal text-diff metadata
  (replace start offset, removed length, inserted length) for each source update.
- Source persistence paths must support no-op diff detection and avoid rewriting source files
  when there is no effective text change.
- Minimal-diff behavior must be deterministic and test-covered for replace/insert/delete cases.

### Mandatory hot reload and hot design coverage
- Any new parser/binder/emitter feature is incomplete unless hot reload and hot design behavior
  is explicitly implemented and verified.
- For each new feature, define live-edit semantics for:
  - apply while editing,
  - revert/removal convergence to baseline,
  - failure handling with last-known-good behavior when applicable.
- Add tests that exercise both compile-time emission and runtime live-edit behavior for the feature.

## 17) XAML compiler and semantic-binder implementation standard (required)

These rules are mandatory for parser, semantic model, transforms, binder, emitter, and
runtime contracts. They apply to all new features and all refactors.

### Normative behavior baseline
- Language semantics must follow the XAML standard and preserve behavioral parity with mature
  compile-time XAML compilers.
- Framework-specific behavior (for example Avalonia property systems, selectors, templates)
  must be implemented as framework adapters over a framework-neutral semantic core.
- Never implement feature parity using string-shape hacks, reflection, or runtime guesswork.

### Mandatory anti-hack rules (pattern -> required implementation)
- Markup extension detection:
  - Forbidden: lexical head-token heuristics on raw text.
  - Required: parser-first structured markup parsing, then semantic dispatch by parsed
    extension name/type.
- Value classification:
  - Forbidden: inferring semantic kind from emitted C# expression text.
  - Required: typed conversion results carrying explicit value kind and runtime requirements.
- Template/style/control-theme classification:
  - Forbidden: suffix checks such as `EndsWith("Template")`.
  - Required: symbol-based or node-kind-based classification.
- Static resource resolver usage:
  - Forbidden: deciding resolver requirements by scanning generated expression strings.
  - Required: explicit semantic flags propagated from conversion/binding results.
- Template validation:
  - Forbidden: reparsing `RawXaml` in binder validation paths.
  - Required: validate from parsed document-model nodes with source line/column from nodes.
- Property-element and setter resolution:
  - Forbidden: early missing-property diagnostics before attached/Avalonia-property resolution.
  - Required: resolve owner-qualified/attached/Avalonia property metadata first, then emit
    missing-property diagnostics only when all typed resolution paths fail.
- Fallback conversion policy:
  - Forbidden: untracked implicit fallback that changes behavior silently.
  - Required: policy-driven fallback (`strict` vs `compatibility`) with explicit diagnostics
    where applicable and deterministic emission behavior.
- Type resolution:
  - Forbidden: unordered probing and unstable candidate selection.
  - Required: deterministic ordered resolution with ambiguity diagnostics and explicit
    compatibility switches for legacy fallback behavior.

### Semantic pipeline contracts
- Pipeline stages must stay explicit and ordered:
  1. Parse to immutable model.
  2. Semantic bind to typed symbols/contracts.
  3. Run transforms on typed model.
  4. Emit deterministic C# from typed model only.
- Every stage must be deterministic for identical inputs (including diagnostics ordering).
- Binder output models must be sufficient for emission without reparsing raw XAML text.
- Any framework-specific feature must be represented in typed semantic models first, then
  projected by framework emitter/runtime adapters.

### Reusability and framework-neutral design
- Keep core semantic abstractions free of Avalonia runtime types whenever possible.
- Isolate framework-specific constructs in adapter layers (`*.Avalonia`), not in core parser
  or core semantic model.
- When adding a new XAML feature, define:
  - framework-neutral semantic representation in Core,
  - framework adapter mapping in Avalonia layer,
  - runtime contract only if strictly required.
- Do not introduce Avalonia-only assumptions into generic parsing/tokenization logic.

### Parser infrastructure reuse (strict)
- All mini-language parsing/tokenization must use the shared parser infrastructure project
  (`XamlToCSharpGenerator.MiniLanguageParsing`) instead of ad-hoc binder/runtime parsing.
- Add new parsers/tokenizers there first (framework-neutral), then consume them from framework
  adapters and runtime tooling.
- Live-edit tooling (hot reload/hot design) must reuse the same parser infrastructure whenever
  structural parsing is required, so behavior stays aligned across compile-time and runtime paths.

### Diagnostics quality requirements
- Diagnostics must be actionable, deterministic, and source-accurate.
- Ambiguity diagnostics must list candidates and deterministic selection result.
- Strict-mode diagnostics must not be downgraded to hide unsupported semantics.
- Compatibility-mode behavior must be explicit and test-covered.

### Test gates (mandatory for each semantic change)
- Add/adjust unit tests for:
  - parser output shape,
  - binder semantic resolution,
  - emitted C# contract,
  - diagnostics IDs/messages/locations.
- Add differential tests for representative framework cases (templates, resources, bindings,
  setters, includes) against expected runtime behavior.
- Add guard tests preventing regression to prohibited patterns (raw-XAML reparse in binder,
  suffix heuristics, expression-text scanning for semantic decisions).
