# EditorAvaloniaBrowserSample

`EditorAvaloniaBrowserSample` hosts `XamlToCSharpGenerator.Editor.Avalonia` in an Avalonia Browser application.

The sample uses the `net10.0-browser` editor and language-service assets. Those assets use the same parser, diagnostics, folding, and completion pipeline as the desktop language server, but use an in-process browser-safe compilation provider instead of MSBuild project loading.

Build it with:

```bash
dotnet workload restore samples/EditorAvaloniaBrowserSample/EditorAvaloniaBrowserSample.csproj
dotnet build samples/EditorAvaloniaBrowserSample/EditorAvaloniaBrowserSample.csproj -c Release
```
