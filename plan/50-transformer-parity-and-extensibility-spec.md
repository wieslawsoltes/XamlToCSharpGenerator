# Transformer Parity + Extensibility Spec (2026-02-20)

## 1. Objective

Deliver SourceGen transformer-pipeline parity against Avalonia XamlIl transformer intent, while making the pipeline user-extendable for custom type/property semantics without XamlX dependency.

This spec defines:

- current parity mapping between Avalonia XamlIl transformers and SourceGen passes,
- a concrete extension contract (MSBuild + JSON rules + assembly attributes),
- diagnostics and acceptance criteria for custom transformations.

## 2. Parity Mapping (XamlIl -> SourceGen Passes)

| SourceGen Pass | XamlIl Transformer Intent (primary) | Status |
|---|---|---|
| `AXSG-P000-BindCustomTransforms` | Extension pre-canonicalization stage (new in SourceGen) | Implemented |
| `AXSG-P001-BindNamedElements` | `XNameTransformer` | Implemented |
| `AXSG-P010-BindRootObject` | `AvaloniaXamlIlClassesTransformer`, `AvaloniaXamlIlResolveClassesPropertiesTransformer`, `AvaloniaXamlIlAvaloniaPropertyResolver`, `AvaloniaXamlIlTransformInstanceAttachedProperties`, `AvaloniaXamlIlTransformRoutedEvent` | Implemented |
| `AXSG-P020-BindResources` | `AvaloniaXamlResourceTransformer`, `AvaloniaXamlIlEnsureResourceDictionaryCapacityTransformer` | Implemented |
| `AXSG-P030-BindTemplates` | `AvaloniaXamlIlControlTemplateTargetTypeMetadataTransformer`, `AvaloniaXamlIlControlTemplatePartsChecker`, `AvaloniaXamlIlControlTemplatePriorityTransformer`, `AvaloniaXamlIlDataTemplateWarningsTransformer` | Implemented |
| `AXSG-P040-BindStyles` | `AvaloniaXamlIlSelectorTransformer`, `AvaloniaXamlIlSetterTransformer`, `AvaloniaXamlIlSetterTargetTypeMetadataTransformer`, `AvaloniaXamlIlDuplicateSettersChecker`, `AvaloniaXamlIlStyleValidatorTransformer` | Implemented |
| `AXSG-P050-BindControlThemes` | `AvaloniaXamlIlControlThemeTransformer` + setter/duplicate checker family | Implemented |
| `AXSG-P060-BindIncludes` | `AvaloniaXamlIncludeTransformer`, `XamlMergeResourceGroupTransformer` | Implemented |
| `AXSG-P900-Finalize` | `AddNameScopeRegistration`, `AvaloniaXamlIlRootObjectScope`, `AvaloniaXamlIlAddSourceInfoTransformer` | Implemented |

## 3. Extensibility Contract

### 3.1 MSBuild Contract

- Property: `AvaloniaSourceGenTransformRules`
  - Semicolon-separated transform rule files.
- Item: `AvaloniaSourceGenTransformRule`
  - Explicit item group alternative to property list.
- Build targets project these files into `AdditionalFiles` with:
  - `SourceItemGroup="AvaloniaSourceGenTransformRule"`.

### 3.2 Rule File Format (JSON)

Supported sections:

- `typeAliases`
  - Maps `(xmlNamespace, xamlType)` -> CLR type.
- `propertyAliases`
  - Maps `(targetType, xamlProperty)` -> CLR property, or
  - Maps `(targetType, xamlProperty)` -> Avalonia property owner+field.

Example:

```json
{
  "typeAliases": [
    {
      "xmlNamespace": "https://github.com/avaloniaui",
      "xamlType": "FancyAlias",
      "clrType": "MyApp.Controls.FancyAliasControl"
    }
  ],
  "propertyAliases": [
    {
      "targetType": "Avalonia.Controls.UserControl",
      "xamlProperty": "AccentText",
      "clrProperty": "Foreground"
    },
    {
      "targetType": "*",
      "xamlProperty": "GridRow",
      "avaloniaPropertyOwnerType": "Avalonia.Controls.Grid",
      "avaloniaPropertyField": "RowProperty"
    }
  ]
}
```

### 3.3 Assembly Attribute Contract

Assembly-level aliases are supported using runtime attributes:

- `SourceGenXamlTypeAliasAttribute(xmlNamespace, xamlTypeName, clrTypeName)`
- `SourceGenXamlPropertyAliasAttribute(targetTypeName, xamlPropertyName, clrPropertyName)`
- `SourceGenXamlAvaloniaPropertyAliasAttribute(targetTypeName, xamlPropertyName, avaloniaPropertyOwnerTypeName, avaloniaPropertyFieldName)`

Rule precedence:

1. assembly attributes,
2. transform rule files (override earlier duplicates with diagnostics).

## 4. Binder/Emitter Integration Requirements

1. Type alias canonicalization must participate in:
   - object node type resolution,
   - selector/control-theme/template token resolution,
   - generic type-token paths.
2. Property alias canonicalization must participate in:
   - object property assignments,
   - property elements,
   - style setters,
   - control-theme setters.
3. Alias-aware Avalonia property mapping must support explicit owner+field mapping.
4. Emission must preserve binding-vs-non-binding assignment safety.

## 5. Diagnostics

New diagnostics:

- `AXSG0900` transform rule parse/shape failure,
- `AXSG0901` invalid alias entry shape,
- `AXSG0902` transform rule type resolution failure,
- `AXSG0903` duplicate/override alias declarations.

## 6. New Transformer Opportunities

1. **Deprecated API rewrite pass**:
   - configurable rewrite map for deprecated property/type tokens to modern equivalents.
2. **Strict opt-in policy pass**:
   - enforce project-level allowlists/denylists for controls/properties.
3. **Typed literal canonicalization pass**:
   - rule-driven literal parser extensions for domain-specific value syntaxes.
4. **Design-time model pass**:
   - richer design-preview token retention with runtime exclusion guarantees.
5. **Cross-assembly namespace policy pass**:
   - constrain/validate implicit namespace candidate expansion to avoid ambiguous matches.

## 7. Acceptance Criteria

- Rule files are ingested incrementally as AdditionalFiles and produce deterministic output.
- Alias rules resolve in runtime codegen paths with no regressions in existing parity tests.
- Invalid/duplicate rules produce deterministic `AXSG09xx` diagnostics.
- Existing corpus/runtime differential tests remain green.
