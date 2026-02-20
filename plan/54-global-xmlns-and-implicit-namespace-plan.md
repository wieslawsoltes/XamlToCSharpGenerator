# Global XMLNS and Implicit Namespace Support Plan

## Goal
Implement improved ergonomics for Avalonia SourceGen XAML so projects can avoid repeating per-file `xmlns:*` declarations and optionally omit the default namespace declaration, while preserving deterministic compile-time behavior and diagnostics.

## Scope
- Add global xmlns prefix support for parser + binder + generator pipeline.
- Add optional implicit default xmlns behavior (opt-in).
- Support `using:` URI resolution parity for type binding.
- Extend URI->CLR mapping beyond Avalonia default URI by honoring `XmlnsDefinition` attributes for arbitrary URIs.
- Document feature usage and add catalog sample coverage.

## Non-Goals
- No runtime dynamic parser feature toggles in v1.
- No automatic migration/rewrite of existing AXAML.
- No breaking change to existing behavior when feature flags are not enabled.

## Reference Findings (Applied)
- Pre-seed parser namespace context from global attributes before XML parsing.
- Support implicit namespace declarations behind an explicit flag.
- Aggregate global mappings through assembly-level attributes.

Applied design in this repo:
- Pre-seed parser namespace manager with global prefixes before parsing AXAML.
- Make implicit default namespace opt-in and controllable by MSBuild/assembly attribute.
- Read global prefixes from assembly attributes + explicit MSBuild override.

## Public Contract Additions
### MSBuild properties
- `AvaloniaSourceGenGlobalXmlnsPrefixes`
  - Semicolon/comma/newline separated entries in `prefix=xmlNamespace` form.
  - Example: `x=http://schemas.microsoft.com/winfx/2006/xaml;vm=using:MyApp.ViewModels`
- `AvaloniaSourceGenAllowImplicitXmlnsDeclaration` (default `false`)
- `AvaloniaSourceGenImplicitDefaultXmlns` (default `https://github.com/avaloniaui`)

### Assembly attributes
- Existing supported source: `Avalonia.Metadata.XmlnsPrefixAttribute`.
- New sourcegen attributes:
  - `[assembly: SourceGenGlobalXmlnsPrefixAttribute(prefix, xmlNamespace)]`
  - `[assembly: SourceGenAllowImplicitXmlnsDeclarationAttribute(bool allow = true)]`

## Implementation Tasks
1. Parser context
- Add parser configuration for global prefix map + implicit default namespace.
- Parse AXAML using `XmlReader` with `XmlParserContext` namespace manager.
- Merge global namespaces with per-file root `xmlns` declarations (file-local declarations win).

2. Generator pipeline
- Build parser namespace context from:
  - compilation assembly attributes,
  - referenced assembly attributes,
  - MSBuild override property (`AvaloniaSourceGenGlobalXmlnsPrefixes`).
- Respect `AvaloniaSourceGenAllowImplicitXmlnsDeclaration` and `AvaloniaSourceGenImplicitDefaultXmlns`.

3. Binder resolution parity
- Add `using:` namespace URI resolution in `TryBuildMetadataName`.
- Add generic URI->CLR lookup using `Avalonia.Metadata.XmlnsDefinitionAttribute` cache for any URI.
- Keep existing Avalonia default candidate scan as fallback.

4. Tests
- Generator test: no local xmlns declarations, global prefixes via MSBuild property, implicit default enabled.
- Generator test: global prefixes via assembly attribute.
- Generator test: arbitrary URI with `XmlnsDefinition` resolves custom control.
- Parser test: global namespace map merges with file declarations and parser succeeds with missing local prefix declarations.

5. Docs + samples
- README section with both assembly attribute and MSBuild usage.
- Catalog sample update:
  - enable global prefix + implicit settings in project,
  - add a dedicated page demonstrating AXAML without local xmlns declarations.

## Acceptance Criteria
- Source generator compiles XAML files that omit local xmlns declarations when global mappings are provided.
- Opt-in implicit mode allows root/type usage without explicit default xmlns.
- `using:` URI prefixes resolve correctly in generated code.
- Existing projects without new properties/attributes continue unchanged.
- Tests added and passing for new coverage.
