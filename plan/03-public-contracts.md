# Public Contracts

## 1. MSBuild Contract

### 1.1 Backend Switch
- `AvaloniaXamlCompilerBackend`
  - Values: `XamlIl` (default), `SourceGen`

### 1.2 Enable Flag
- `AvaloniaSourceGenCompilerEnabled`
  - Default: `false`
  - Implicitly set to `true` when backend is `SourceGen`

### 1.3 Configuration Flags
- `AvaloniaSourceGenUseCompiledBindingsByDefault` (bool)
- `AvaloniaSourceGenCreateSourceInfo` (bool)
- `AvaloniaSourceGenStrictMode` (bool)

### 1.4 AdditionalFiles Metadata
- `SourceItemGroup` (expected: `AvaloniaXaml` when present)
- `TargetPath` (used for stable URI generation)

## 2. Runtime API Contract

### 2.1 AppBuilder Extension
```csharp
AppBuilder UseAvaloniaSourceGeneratedXaml(this AppBuilder builder)
```

### 2.2 Registry API
```csharp
void Register(string uri, Func<IServiceProvider?, object> factory)
bool TryCreate(IServiceProvider? serviceProvider, string uri, out object? value)
```

### 2.3 Loader Helper API
```csharp
bool TryLoad(IServiceProvider? serviceProvider, Uri uri, out object? value)
```

## 3. Generated Source Contract
For each valid `x:Class` document the generator emits a partial class that includes:
1. Named element fields with resolved CLR type.
2. `InitializeComponent(bool loadXaml = true)`.
3. `[ModuleInitializer]` registration function to register URI factory.

## 4. Diagnostics Contract
Namespace prefix: `AXSG`

1. `AXSG0001` parse failure (error)
2. `AXSG0002` missing `x:Class` (warning)
3. `AXSG0100` unresolved named element type fallback (warning)
4. `AXSG0200` source emission failure (error)
5. `AXSG9000` internal error (error)

## 5. Compatibility Guarantees
1. Source generation is disabled by default unless explicitly enabled.
2. Existing Avalonia backend remains active when switch is not enabled.
3. Generated contracts are additive partial-class members.
4. C#-only support is explicit in v1; non-C# remains on existing backend.

## 6. Packaging Contract
1. Primary consumer package: `XamlToCSharpGenerator` (single-package install path).
2. Analyzer package: `XamlToCSharpGenerator.Generator` (can be consumed directly for advanced scenarios).
3. Runtime package: `XamlToCSharpGenerator.Runtime`.
4. Build package: `XamlToCSharpGenerator.Build` with `buildTransitive` props/targets.
