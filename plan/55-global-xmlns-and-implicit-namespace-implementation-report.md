# Global XMLNS + Implicit Namespace Implementation Report

## Completed

1. Parser pre-seeded namespace context
- `SimpleXamlDocumentParser` now supports global prefix maps and optional implicit default namespace through parser constructor settings.
- AXAML parsing now uses `XmlReader` + `XmlParserContext` when global/implicit settings are active.
- Document namespace map now merges global mappings with local declarations (local declarations override globals).

2. Generator pipeline integration
- Added parser namespace context build in `AvaloniaXamlSourceGenerator` from:
  - assembly attributes (`Avalonia.Metadata.XmlnsPrefixAttribute`, `SourceGenGlobalXmlnsPrefixAttribute`),
  - MSBuild property `AvaloniaSourceGenGlobalXmlnsPrefixes`,
  - implicit mode flags (`AvaloniaSourceGenAllowImplicitXmlnsDeclaration`, `AvaloniaSourceGenImplicitDefaultXmlns`),
  - assembly attribute `SourceGenAllowImplicitXmlnsDeclarationAttribute`.
- Parser stage now consumes this context for each AXAML file.

3. Binder parity upgrades
- Added `using:` URI support in `TryBuildMetadataName`.
- Added generic URI->CLR namespace resolution cache based on `XmlnsDefinition` attributes (not only Avalonia default URI special-case).
- Retained Avalonia default namespace candidate fallback logic.

4. Runtime/public contract additions
- Added new assembly attributes in runtime:
  - `SourceGenXmlnsDefinitionAttribute`
  - `SourceGenGlobalXmlnsPrefixAttribute`
  - `SourceGenAllowImplicitXmlnsDeclarationAttribute`

5. Build contract additions
- Added and exposed compiler-visible properties:
  - `AvaloniaSourceGenGlobalXmlnsPrefixes`
  - `AvaloniaSourceGenAllowImplicitXmlnsDeclaration`
  - `AvaloniaSourceGenImplicitDefaultXmlns`
- `AvaloniaSourceGenGlobalXmlnsPrefixes` supports comma/semicolon/newline separators (`prefix=xmlNamespace` entries). Comma-separated values are recommended for MSBuild property usage.

6. Test coverage
- Added generator tests:
  - custom URI resolution through `XmlnsDefinition` (`urn:demo` scenario),
  - global prefix resolution from assembly attribute without local `xmlns:prefix`,
  - implicit default namespace + global prefixes via MSBuild,
  - `x:DataType` resolution from global `vm` prefix MSBuild setting.
- Added parser tests:
  - global prefix + implicit default parse,
  - global prefix used by directive values (`x:DataType`).

7. Docs + samples
- Updated root README with a new "Global XMLNS Imports" section, including MSBuild and assembly-attribute examples.
- Updated catalog sample project with global prefix and implicit namespace settings.
- Added new catalog tab/page: `Global XMLNS`.

## Validation

- Full test suite: Passed (`246 passed`, `1 skipped`).
- Catalog sample build: Passed (`0 warnings`, `0 errors`).

## Integration note

- Avalonia resource preprocessing still parses AXAML as XML before source generation in standard project wiring.
- Because of that preprocessing, full omission of XML namespace declarations for prefixed element/attribute names may still fail in standard builds even though SourceGen parser context supports global prefixes/implicit default namespace.
- Current sample demonstrates global-prefix behavior in directive/markup-extension value contexts under standard Avalonia build integration.
