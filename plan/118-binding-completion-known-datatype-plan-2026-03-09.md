# Binding Completion For Known `x:DataType`

## Problem

Binding completion must surface source members from the current `x:DataType` context even when the target property is not text-oriented, for example:

```xaml
<Window
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:vm="using:TestApp.Controls"
    x:DataType="vm:MainWindowViewModel"
    Width="{Binding }" />
```

Expected behavior:

- completion returns current source properties
- completion returns applicable parameterless methods
- behavior is identical to `Text="{Binding }"`
- the same result is available through both the in-process engine and the LSP server

## Analysis

The current completion implementation is already target-property agnostic:

- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Completion/XamlBindingCompletionService.cs`
  - parses the current binding markup and resolves path/member completion
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Completion/XamlSemanticSourceTypeResolver.cs`
  - resolves ambient `x:DataType` regardless of the target property
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Completion/XamlClrMemberCompletionFactory.cs`
  - produces property and parameterless method completions for the resolved source type

The real gap was missing regression coverage for non-string target properties, which made the feature easy to regress without noticing.

## Plan

1. Add engine regression coverage for `Width="{Binding }"`.
2. Add LSP regression coverage for `Width="{Binding }"`.
3. Keep the implementation unchanged unless those tests expose a real target-property-specific failure.
4. Validate the focused completion slice.

## Implementation Result

Focused regression tests were added for:

- engine completion on `Width="{Binding }"`
- LSP completion on `Width="{Binding }"`

No production code change was required because the current binding completion pipeline already supports this scenario correctly.
