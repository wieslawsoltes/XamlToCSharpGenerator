# SourceGen IL Weaving Sample

This sample intentionally keeps legacy `AvaloniaXamlLoader.Load(...)` calls in code-behind while the AXSG backend is active.

It exists to verify that AXSG IL weaving rewrites:

- `AvaloniaXamlLoader.Load(this)`
- `AvaloniaXamlLoader.Load(serviceProvider, this)`

The sample also enables AXSG hot reload bootstrap so integration tests can confirm that woven initialization still registers tracked documents.

The sample disables the IDE polling fallback when a debugger is attached. That avoids debugger-session instability while keeping normal sample runs and automated coverage on the woven path.

Disable weaving for a build with:

```xml
<PropertyGroup>
  <XamlSourceGenIlWeavingEnabled>false</XamlSourceGenIlWeavingEnabled>
</PropertyGroup>
```

The Avalonia-prefixed alias also works:

```xml
<PropertyGroup>
  <AvaloniaSourceGenIlWeavingEnabled>false</AvaloniaSourceGenIlWeavingEnabled>
</PropertyGroup>
```

Select the scan backend explicitly with:

```xml
<PropertyGroup>
  <XamlSourceGenIlWeavingBackend>Metadata</XamlSourceGenIlWeavingBackend>
</PropertyGroup>
```

Use `Metadata` for the default `System.Reflection.Metadata` scan path or `Cecil` to force the legacy Mono.Cecil scan path.
