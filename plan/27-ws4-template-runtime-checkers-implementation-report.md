# WS4 Template Runtime Checker Slice Implementation Report

Date: 2026-02-19

## Scope Implemented
1. Added template-family runtime checker parity slice for `ItemTemplate`/`DataTemplates` misuse diagnostics (item container inside template content).
2. Added template content root-type validation for `ItemsPanelTemplate`, `DataTemplate`, `TreeDataTemplate`, and `ControlTemplate` where template content exists.
3. Added full regression fixtures for the new diagnostics.

## Implemented Diagnostics
1. `AXSG0505` (`ItemContainerInsideTemplate`): warns when a known item container control is used as direct data-template content under `ItemsControl.ItemTemplate` or `ItemsControl.DataTemplates` context.
2. `AXSG0506` (`TemplateContentTypeInvalid`): warns when template content root type is incompatible with expected runtime template result type:
   - `ItemsPanelTemplate` -> `Panel`
   - `DataTemplate`/`TreeDataTemplate`/`ControlTemplate` -> `Control`

## Binder Changes
1. Added `ValidateItemContainerInsideTemplateWarning(...)` and known container mapping parity derived from Avalonia behavior:
   - `ListBox` -> `ListBoxItem`
   - `ComboBox` -> `ComboBoxItem`
   - `Menu`/`MenuItem` -> `MenuItem`
   - `TabStrip` -> `TabStripItem`
   - `TabControl` -> `TabItem`
   - `TreeView` -> `TreeViewItem`
2. Added `ValidateTemplateContentRootType(...)` with XML line-info mapping for template content-root diagnostics.

## Generator/Contracts
1. Added `AXSG0505` and `AXSG0506` to diagnostic catalog and generator mapping.

## Tests Added
1. `Reports_Diagnostic_For_ItemContainer_Inside_DataTemplate_ItemTemplate`
2. `Reports_Diagnostic_For_ItemContainer_Inside_TreeDataTemplate_ItemTemplate`
3. `Reports_Diagnostic_For_ItemsPanelTemplate_Content_Root_Not_Panel`
4. `Reports_Diagnostic_For_TreeDataTemplate_Content_Root_Not_Control`

## Validation
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
   - Passed: `101`, Failed: `0`.
2. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.sln -m:1 /nodeReuse:false --disable-build-servers`
   - Build succeeded.

## Remaining Beyond This Slice
1. Full include/merge/resource precedence parity (`WS5`) remains pending.
2. ControlTheme runtime materialization parity (`WS3.3`) remains pending.
