# EditorAvaloniaSample

`EditorAvaloniaSample` showcases how to embed `XamlToCSharpGenerator.Editor.Avalonia` inside a desktop Avalonia application.

It demonstrates:

- binding `AxamlTextEditor.SourceText` two-way to a viewmodel
- switching `DocumentUri` between real `.axaml` files inside the sample project
- pointing `WorkspaceRoot` at the sample project so the editor can use project-aware language services
- TextMate-based AXAML syntax highlighting
- editor folding for multi-line XAML regions and comments
- surfacing live diagnostics in a bottom `Problems` panel
- surfacing sample activity in a bottom `Output` panel
- browsing the workspace from a left-hand explorer tree

Run it with:

```bash
dotnet run --project samples/EditorAvaloniaSample/EditorAvaloniaSample.csproj
```

The sample uses compact Fluent styling and includes the AvaloniaEdit Fluent resource dictionary in `App.axaml`, which is required for the embedded editor surface to render correctly.

Use the left-hand explorer tree to switch documents, `Restore From Disk` to reload the saved file, and `Introduce Error` to inject a deliberate language-service error into the active editor buffer so the `Problems` view updates live.
