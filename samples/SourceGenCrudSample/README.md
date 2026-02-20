# SourceGen CRUD Sample

Basic Avalonia desktop CRUD app wired to this repository's source-generator backend.

For shipping consumption, use a single package reference:

```xml
<PackageReference Include="XamlToCSharpGenerator" Version="1.0.0" />
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
- Fluent theme is added programmatically in `App.Initialize()` (current SourceGen object-graph pass does not yet emit `Application.Styles` property elements).
- In-app `Hot Design Studio` tab demonstrates runtime core tool panels (toolbar/elements/toolbox/canvas/properties) and live source-backed edits.

## Run

```bash
dotnet run --project /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj
```

If the window appears empty after generator changes, force a rebuild and run without rebuilding:

```bash
dotnet msbuild /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj /t:Build /p:Restore=false /m:1 /nr:false
dotnet run --project /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj --no-build
```
