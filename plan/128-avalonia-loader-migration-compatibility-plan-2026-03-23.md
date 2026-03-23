# 128) Avalonia Loader Migration Compatibility Plan (2026-03-23)

Date: 2026-03-23

## 1. Goal

Reduce or eliminate the manual migration step where users must remove or guard direct `AvaloniaXamlLoader.Load(this)` usage when switching an Avalonia project to the AXSG backend.

Desired outcome:

1. changing the project file and `Program.cs` should be enough for the common C# migration case;
2. AXSG must stay AOT/trimming friendly;
3. AXSG must not introduce reflection into emitted/runtime execution paths;
4. existing AXSG `InitializeComponent(bool loadXaml = true)` behavior must not regress.

Issue / prototype context:

- issue: <https://github.com/wieslawsoltes/XamlToCSharpGenerator/issues/28>
- prototype PR: <https://github.com/wieslawsoltes/XamlToCSharpGenerator/pull/29>

## 2. Current State

### 2.1 What AXSG already disables

When AXSG is enabled, the build package already disables Avalonia's default XAML compiler and name generator:

- `src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.targets`
  - `EnableAvaloniaXamlCompilation=false`
  - `AvaloniaNameGeneratorIsEnabled=false`
  - `AXAML_SOURCEGEN_BACKEND` is defined

This means AXSG already cleanly disables Avalonia's build-time XAML backend paths.

What AXSG does not disable is the public `Avalonia.Markup.Xaml.AvaloniaXamlLoader` type that ships inside Avalonia's runtime assembly.

### 2.2 What Avalonia does today

In Avalonia itself:

- `Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(object)` throws immediately when no compiled XAML path is available.
- `Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(IServiceProvider?, object)` does the same.
- only the URI-based overload has a runtime fallback seam, and that seam is an internal interface (`IRuntimeXamlLoader`).

Relevant source files:

- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml/AvaloniaXamlLoader.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Avalonia.Build.Tasks/XamlCompilerTaskExecutor.cs`

Avalonia's default XamlIl path relies on post-compile IL rewriting:

1. user code calls `AvaloniaXamlLoader.Load(this)` or `Load(sp, this)`;
2. `XamlCompilerTaskExecutor` finds those calls in IL;
3. Avalonia rewrites them to generated trampoline methods.

That is why the object-instance loader path works in stock Avalonia without users thinking about it.

### 2.3 What AXSG runtime already bridges

AXSG already has a URI/runtime-loader bridge concept, but it is intentionally inactive in AOT-safe mode because Avalonia does not expose a public registration seam for replacing the runtime loader:

- `src/XamlToCSharpGenerator.Runtime.Avalonia/SourceGenRuntimeXamlLoaderBridge.cs`

This bridge does not solve `AvaloniaXamlLoader.Load(this)`. It only concerns runtime document/URI loading.

## 3. Hard Constraints

### 3.1 Cleanly disabling `AvaloniaXamlLoader` from NuGet is not feasible

There is no clean consumer-side NuGet switch that removes or disables a public type compiled into Avalonia's referenced assemblies.

AXSG can:

- disable Avalonia build tasks and generators;
- shadow or redirect user source references at compile time;
- rewrite produced IL;
- ask Avalonia upstream for a new public seam.

AXSG cannot:

- make the `Avalonia.Markup.Xaml.AvaloniaXamlLoader` type disappear from the referenced Avalonia package;
- change Avalonia's public static object-loader behavior without either compile-time redirection, IL rewriting, or an upstream Avalonia change.

### 3.2 Repository rules that matter here

From this repo's engineering rules:

1. no reflection in emitted/runtime execution paths;
2. NativeAOT/trimming compatibility is mandatory;
3. fixes should be root-cause based, not heuristic string hacks;
4. hot reload and hot design semantics must remain correct.

This rules out reflection-heavy production shims as the default answer.

## 4. Evaluation Criteria

Each option below is evaluated against:

1. migration friction for end users;
2. compatibility with existing `InitializeComponent()` users;
3. compatibility with `AvaloniaXamlLoader.Load(this)` users;
4. coverage for `Load(sp, this)`;
5. coverage for fully qualified `global::Avalonia.Markup.Xaml.AvaloniaXamlLoader...` call sites;
6. AOT/trimming safety;
7. architectural fit with AXSG as a source-generated compiler stack;
8. implementation and maintenance cost.

## 5. Feasible Options

### 5.1 Option A: keep current model, improve docs and add analyzer/code fix

### Summary

Keep the current requirement:

- AXSG users call `InitializeComponent();`
- legacy loader code stays only behind `#if !AXAML_SOURCEGEN_BACKEND`

Add tooling to reduce pain:

1. analyzer detects direct `AvaloniaXamlLoader.Load(this)` in AXSG-enabled projects;
2. code fix rewrites to `InitializeComponent();` or wraps fallback with `#if !AXAML_SOURCEGEN_BACKEND`;
3. docs become explicit that this is a one-time migration step.

### Pros

1. lowest implementation risk;
2. no changes to runtime or generator contracts;
3. fully aligned with current docs and emitted code;
4. no reflection, no shadowing tricks, no IL rewrite.

### Cons

1. does not meet the "project file + `Program.cs` only" migration goal;
2. users still edit code-behind;
3. not as smooth as issue #28 requests.

### Recommendation

Useful as baseline tooling, but not sufficient as the main answer.

### 5.2 Option B: generate a per-class compatibility `AvaloniaXamlLoader` inside the partial type

### Summary

For each class-backed AXAML document, keep generating:

- `public void InitializeComponent(bool loadXaml = true)`

and additionally emit a nested compatibility type inside the generated partial class:

```csharp
private static class AvaloniaXamlLoader
{
    public static void Load(RootType obj) => __InitializeComponentCompat(obj, null);
    public static void Load(IServiceProvider? sp, RootType obj) => __InitializeComponentCompat(obj, sp);
}
```

The nested type would call a shared generated initialization core that preserves:

1. object graph population;
2. `x:Bind` reset semantics;
3. named-element rebinding;
4. hot reload / hot design registration;
5. optional service-provider-aware population when needed.

### Why it works

In C#, a nested type inside the partial class wins simple-name resolution for code inside that class body.

Local scratch validation on 2026-03-23 confirmed:

1. nested-type shadowing compiles cleanly;
2. no warning is produced;
3. user code can keep `AvaloniaXamlLoader.Load(this)` if it is not fully qualified.

### Pros

1. best near-term UX improvement for common C# Avalonia code-behind;
2. no upstream Avalonia change required;
3. no reflection in emitted/runtime paths;
4. keeps existing `InitializeComponent()` flow intact;
5. scope is local to each class, so it does not globally disturb unrelated code.

### Cons

1. does not intercept fully qualified calls such as `global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this)`;
2. only helps inside the class partial itself;
3. needs careful handling of `Load(sp, this)` and `x:Bind`/hot-reload state so no behavior regresses.

### Notes on PR #29

PR #29 demonstrates this family of approach, but it should not be merged as-is.

The prototype currently:

1. replaces `InitializeComponent` instead of preserving both surfaces;
2. does not preserve the current `x:Bind` reset path in the shown emission diff;
3. narrows behavior instead of adding a pure compatibility layer;
4. does not solve fully qualified call sites.

### Recommendation

Recommended near-term AXSG implementation.

### 5.3 Option C: generate a project-wide `global using AvaloniaXamlLoader = ...` alias

### Summary

Emit one generated file that introduces a project-wide alias:

```csharp
global using AvaloniaXamlLoader = XamlToCSharpGenerator.Runtime.AvaloniaSourceGeneratedCompatLoader;
```

Then implement a compat loader with overloads matching the common Avalonia loader surface.

That compat loader would dispatch by root object type into generated registries/delegates.

### Local validation

Local scratch validation on 2026-03-23 confirmed that a `global using` alias can cleanly override simple-name uses of `AvaloniaXamlLoader` in source files without warnings.

### Pros

1. one project-wide hook instead of per-class emission;
2. simple-name `AvaloniaXamlLoader.Load(this)` calls anywhere in the project are redirected;
3. can preserve `InitializeComponent` and existing docs for non-compat users.

### Cons

1. still does not intercept fully qualified `global::Avalonia.Markup.Xaml.AvaloniaXamlLoader...`;
2. needs a new global object-type-to-loader registry;
3. more cross-cutting and surprising than the nested-type approach;
4. can collide conceptually with user-defined aliases and makes symbol resolution less obvious.

### Recommendation

Feasible, but less contained than Option B. Use only if broader project-wide interception is required.

### 5.4 Option D: generate a local `Avalonia.Markup.Xaml.AvaloniaXamlLoader` shadow type in the consumer assembly

### Summary

Generate a type with the same full name as Avalonia's loader inside the user's compilation:

```csharp
namespace Avalonia.Markup.Xaml
{
    public static class AvaloniaXamlLoader { ... }
}
```

### Local validation

Local scratch validation on 2026-03-23 confirmed:

1. this compiles;
2. it even intercepts fully qualified calls in the current compilation;
3. but it produces `CS0436` warnings because the local type conflicts with the imported Avalonia type.

### Pros

1. broadest compile-time interception;
2. handles even fully qualified consumer calls.

### Cons

1. noisy and confusing type-conflict warnings;
2. requires mirroring enough of Avalonia's loader API to avoid breaking legitimate usage;
3. very invasive from a DX perspective;
4. easy to confuse tooling, docs, and future maintainers.

### Recommendation

Technically feasible but not clean. Reject for productized use.

### 5.5 Option E: add an AXSG post-compile IL rewrite step

### Summary

Add a build task after Roslyn compilation that scans the produced assembly and rewrites:

- `AvaloniaXamlLoader.Load(this)`
- `AvaloniaXamlLoader.Load(sp, this)`

to AXSG-generated initialization methods, similar in spirit to Avalonia's current XamlIl task.

### Pros

1. closest behavioral match to Avalonia's existing mechanism;
2. can cover fully qualified calls;
3. can potentially cover more languages than C# source-generator tricks.

### Cons

1. materially changes AXSG's architecture from "source-generated compiler stack" to "source generator plus assembly rewriter";
2. higher complexity for PDB handling, incremental build correctness, debugging, and hot reload;
3. more moving parts in MSBuild;
4. harder to reason about than a plain source-generated compatibility layer.

### Recommendation

Feasible, but only as a last-resort fallback if compile-time compatibility layers prove insufficient.

### 5.6 Option F: upstream Avalonia change to expose a public object-loader seam

### Summary

Add a public extension point in Avalonia so `AvaloniaXamlLoader.Load(object)` and `Load(IServiceProvider?, object)` can delegate to a registered compiled/object loader instead of immediately throwing.

Two shapes are plausible:

1. a public loader registration interface/service for object-instance loading;
2. a public compiled dispatcher contract such as `TryPopulate(IServiceProvider?, object)` or equivalent.

AXSG could then register generated delegates cleanly without shadowing names or rewriting IL.

### Pros

1. architecturally cleanest long-term answer;
2. removes the need for compile-time shadowing tricks;
3. could benefit other alternate compilers, previewers, and tooling;
4. consistent with the existing URI loader concept, but public and object-oriented.

### Cons

1. requires Avalonia upstream buy-in;
2. not available on current Avalonia releases;
3. likely spans API design, runtime contract design, and compatibility discussion.

### Recommendation

Recommended long-term direction, in parallel with a near-term AXSG-local solution.

## 6. Options Matrix

| Option | User code edits | Fully qualified call coverage | AOT-safe | Upstream dependency | Architectural risk | Recommended |
| --- | --- | --- | --- | --- | --- | --- |
| A. docs + analyzer/code fix | required once | yes, via rewrite | yes | no | low | supporting only |
| B. nested compat loader per class | none for common simple-name calls | no | yes | no | low to medium | yes, near-term |
| C. global alias compat loader | none for common simple-name calls | no | yes | no | medium | maybe |
| D. full-name shadow type | none | yes | yes | no | high DX risk | no |
| E. IL rewrite task | none | yes | yes | no | high build complexity | maybe, last resort |
| F. upstream Avalonia seam | none | yes | yes if designed typed/public | yes | medium | yes, long-term |

## 7. Recommended Strategy

Use a two-track strategy.

### 7.1 Near-term product answer

Implement Option B and pair it with analyzer support from Option A.

That means:

1. keep current AXSG-generated `InitializeComponent(bool loadXaml = true)`;
2. add a nested compatibility `AvaloniaXamlLoader` inside each generated class-backed partial;
3. emit both `Load(this)` and `Load(sp, this)` overloads;
4. share one generated initialization core so the compatibility path and `InitializeComponent` path cannot drift;
5. add an analyzer/code fix for fully qualified loader calls and mixed-backend guard suggestions.

This gives the common C# migration path requested in issue #28 without changing Avalonia upstream first.

### 7.2 Long-term platform answer

Pursue Option F with Avalonia upstream:

1. propose a public object-loader extension seam;
2. keep it AOT-safe and typed;
3. let AXSG register generated instance-population delegates instead of relying on symbol shadowing.

If Avalonia accepts such a contract, AXSG can later simplify or retire the nested compatibility layer.

## 8. Implementation Plan For The Recommended Near-Term Path

### 8.1 Generator changes

Update:

- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`

Plan:

1. factor current class-backed initialization body into a single generated helper;
2. keep `public void InitializeComponent(bool loadXaml = true)`;
3. emit nested compatibility `AvaloniaXamlLoader` with:
   - `Load(RootType obj)`
   - `Load(IServiceProvider? sp, RootType obj)`
4. make both paths call the same core helper;
5. preserve:
   - `SourceGenMarkupExtensionRuntime.ResetXBind(this)` when `HasXBind`
   - named-element rebinding
   - hot reload / hot design registration
   - current generated URI/source-path metadata

### 8.2 Analyzer/code fix

Add diagnostics for AXSG-enabled projects when source contains:

1. `global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this)`
2. `global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(sp, this)`
3. manual parameterless `InitializeComponent()` wrappers that still call the global Avalonia loader

Code fix options:

1. rewrite to `InitializeComponent();`
2. or wrap fallback in `#if !AXAML_SOURCEGEN_BACKEND`

### 8.3 Tests

Add generator/build/runtime coverage for:

1. class-backed view with `InitializeComponent();`
2. class-backed view with `AvaloniaXamlLoader.Load(this);`
3. class-backed view with `AvaloniaXamlLoader.Load(sp, this);`
4. app root `App.axaml.cs` using loader call instead of `InitializeComponent`;
5. existing `x:Bind` page to prove no regression;
6. hot reload / hot design registration still occurs;
7. analyzer diagnostics for fully qualified call sites.

### 8.4 Docs

Update:

1. `README.md`
2. `site/articles/getting-started/installation.md`
3. `site/articles/getting-started/initializecomponent-and-loader-fallback.md`
4. `site/articles/advanced/class-backed-xaml-and-initializecomponent.md`

New guidance should become:

1. simple `AvaloniaXamlLoader.Load(this)` is now compatible in AXSG-backed class code-behind;
2. fully qualified `global::Avalonia.Markup.Xaml.AvaloniaXamlLoader...` still requires rewrite or guard;
3. mixed-backend projects may still prefer the explicit `#if !AXAML_SOURCEGEN_BACKEND` pattern.

## 9. Explicit Non-Recommendations

Do not pursue the following as the primary answer:

1. "disable the Avalonia loader from NuGet" as a consumer-side feature, because that is not technically available in a clean way;
2. shadowing `Avalonia.Markup.Xaml.AvaloniaXamlLoader` with a same-full-name generated type, because the type-conflict warnings make it a poor user experience;
3. a reflection-based production bridge to Avalonia's internal runtime loader interface, because that conflicts with this repo's AOT/trimming rules.

## 10. Decision

Recommended choice set:

1. ship nested per-class compatibility loader generation in AXSG;
2. preserve the current `InitializeComponent` surface;
3. add analyzer/code-fix coverage for the remaining global-qualified/manual-wrapper edge cases;
4. separately open an Avalonia upstream design discussion for a public object-loader seam.

This is the best balance of:

1. migration ergonomics;
2. AOT safety;
3. low implementation risk;
4. alignment with AXSG architecture.
