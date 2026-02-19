# Standalone Avalonia Source-Generator Compiler: Overview and Goals

## 1. Purpose
Create a standalone compiler backend for Avalonia XAML that uses Roslyn incremental source generation instead of XamlX IL weaving. The backend is designed to be shipped as NuGet packages and plugged into any C# Avalonia application through opt-in MSBuild properties.

## 2. Problem Statement
Current Avalonia compilation relies on XamlX and post-C# compile IL rewriting in build tasks. This creates coupling to IL toolchains and limits source-level traceability in generated behavior. The new backend shifts compilation to source generation with deterministic outputs and explicit generated contracts.

## 3. Product Goals
1. Replace build-time XamlX path for C# applications when opted in.
2. Keep default Avalonia behavior unchanged unless the backend switch is enabled.
3. Remove XamlX dependency from the new compiler implementation.
4. Produce generated code contracts for `InitializeComponent`, named elements, and URI-load registry.
5. Provide deterministic, incremental, diagnosable compilation with source-level outputs.

## 4. Scope (v1)
1. C# projects only.
2. Build-time source generation only (no general-purpose runtime dynamic compilation engine).
3. Opt-in backend switching via MSBuild (`AvaloniaXamlCompilerBackend=SourceGen`).
4. Runtime registry plumbing for URI compatibility through generated registration.
5. Parity program tracked against Avalonia XamlIl behaviors.

## 5. Non-Goals (v1)
1. F#/VB backend replacement.
2. Automatic migration of existing code-behind patterns.
3. Reflection-heavy fallback behaviors inside compiler pipeline.
4. Complete replacement of Avalonia runtime loader internals inside this repository.

## 6. Definition of Full Parity
Full parity means each functional behavior currently enforced by Avalonia XamlIl transforms has a defined source-generator equivalent with matching user-visible semantics, diagnostics shape, and compatibility guarantees for supported C# app scenarios.

## 7. Anchors to Existing Avalonia Seams
1. Build task orchestration: `/Users/wieslawsoltes/GitHub/Avalonia/packages/Avalonia/AvaloniaBuildTasks.targets`
2. Xaml compiler executor: `/Users/wieslawsoltes/GitHub/Avalonia/src/Avalonia.Build.Tasks/XamlCompilerTaskExecutor.cs`
3. Transformer stack: `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/AvaloniaXamlIlCompiler.cs`
4. Existing source generator pattern: `/Users/wieslawsoltes/GitHub/Avalonia/src/tools/Avalonia.Generators/NameGenerator/AvaloniaNameIncrementalGenerator.cs`

## 8. Migration Objective
Provide a path where an application can:
1. Install source-generator compiler packages.
2. Set `AvaloniaXamlCompilerBackend=SourceGen`.
3. Build without `CompileAvaloniaXamlTask` execution.
4. Keep stable runtime behavior for generated view loading and name resolution.
