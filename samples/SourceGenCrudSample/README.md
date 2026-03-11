# SourceGen CRUD Sample

Basic Avalonia desktop CRUD app wired to this repository's source-generator backend.

For shipping consumption, use a single package reference:

```xml
<PackageReference Include="XamlToCSharpGenerator" Version="0.1.0-alpha.3" />
```

## What this sample demonstrates

- `AvaloniaXamlCompilerBackend=SourceGen`
- `AvaloniaSourceGenCompilerEnabled=true` (implied by backend switch)
- `EnableAvaloniaXamlCompilation=false` (applied automatically when SourceGen is enabled)
- `AvaloniaNameGeneratorIsEnabled=false` is applied automatically by `XamlToCSharpGenerator.Build` when backend is `SourceGen` (prevents duplicate `InitializeComponent`)
- Local analyzer integration via project references to:
  - `src/XamlToCSharpGenerator.Core`
  - `src/XamlToCSharpGenerator.Avalonia`
  - `src/XamlToCSharpGenerator.Generator`
- Runtime integration via project reference to:
  - `src/XamlToCSharpGenerator.Runtime`
- Build contract integration via imports from:
  - `src/XamlToCSharpGenerator.Build/buildTransitive/*.props|*.targets`
- Runtime loader is source-generated via `UseAvaloniaSourceGeneratedXaml()`.
- Fluent theme is declared in `App.axaml` via `<Application.Styles><FluentTheme /></Application.Styles>` and materialized by source-generated object graph code.

## Run

Run from the repository root:

```bash
dotnet run --project samples/SourceGenCrudSample/SourceGenCrudSample.csproj
```

If the window appears empty after generator changes, force a rebuild and run without rebuilding:

```bash
dotnet msbuild samples/SourceGenCrudSample/SourceGenCrudSample.csproj /t:Build /p:Restore=false /m:1 /nr:false
dotnet run --project samples/SourceGenCrudSample/SourceGenCrudSample.csproj --no-build
```
