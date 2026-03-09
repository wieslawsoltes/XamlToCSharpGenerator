# Qualified Property-Element Language Service Plan

## Problem
Owner-qualified property-element syntax like `<Window.IsVisible>` is tokenized as an element name. That breaks completion after the dot and also causes hover, definition, references, and rename to treat the token as a type instead of a property.

## Findings
- `XamlCompletionContextDetector` only distinguishes `ElementName`, `AttributeName`, `AttributeValue`, and `MarkupExtension`.
- `XamlCompletionService` resolves `ElementName` tokens as types only.
- `XamlDefinitionService`, `XamlReferenceService`, `XamlHoverService`, and `XamlRenameService` also interpret `ElementName` tokens as type references.
- The core parser already has the required owner-qualified property split helpers in `XamlPropertyTokenSemantics`.

## Scope
Implement full language-service support for owner-qualified property-element names:
- completion after `.` in `<Type.Property...`
- prefixed owners like `<controls:MyControl.Property...`
- regular and attached properties
- hover, definition, references, and rename on the property-element token
- keep normal element-name completion unchanged when the token is not owner-qualified

## Design
1. Add `QualifiedPropertyElement` completion context kind.
2. Teach `XamlCompletionContextDetector` to classify owner-qualified element-name tokens as property-element tokens.
3. Add property-element completion generation that:
   - resolves the owner type from the token prefix
   - lists matching properties on that owner type
   - inserts the full owner-qualified property token
4. Route hover/definition/reference/rename through property resolution when the context kind is `QualifiedPropertyElement`.
5. Add focused engine and LSP tests for:
   - `<Window.`
   - `<Window.IsV`
   - `<controls:CustomControl.`
   - definition/reference/hover on `<Window.IsVisible>`

## Acceptance Criteria
- `<Window.` offers `IsVisible` and other `Window` properties.
- `<Window.IsV` narrows to `IsVisible`.
- Hover on `Window.IsVisible` describes the property, not the type.
- Definition/references/rename on `Window.IsVisible` target the CLR property symbol.
- Existing element-name completion for `<Win` and open-tag attribute completion remain unchanged.
