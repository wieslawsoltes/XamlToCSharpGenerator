# Inline C# in XAML

## Overview

AXSG supports valid-XAML inline C# through an explicit `CSharp` surface in the
default Avalonia XML namespace.
This feature is intended for cases where compact binding or event syntax is no
longer expressive enough and the author needs normal C# expressions or event
statements with access to the local XAML context.

The feature is deliberately explicit:

- The XAML stays valid XML and valid XAML.
- Inline code is opt-in through `CSharp`.
- Existing shorthand expression forms remain supported, but `CSharp` is the
  canonical form for multi-line or context-rich code.

## Namespace

`CSharp` is available directly in the default Avalonia XML namespace. No
additional `xmlns` prefix is required.

## Supported forms

### Compact attribute form

Use this form for short expressions or short event lambdas.

```xaml
<TextBlock Text="{CSharp Code=source.ProductName}" />
<Button Click="{CSharp Code='(sender, e) => source.ClickCount++'}" />
```

Notes:

- The compact form is best for short single-line code.
- Complex expressions with commas, nested braces, or long interpolated strings
  should use the object-element form below.

### Object-element form

Use this form for multi-line expressions or event statement blocks.

```xaml
<TextBlock.Text>
  <CSharp><![CDATA[
source.ProductName + " #" + source.ClickCount
  ]]></CSharp>
</TextBlock.Text>

<Button.Click>
  <CSharp><![CDATA[
source.ClickCount++;
source.LastAction = "inline code";
  ]]></CSharp>
</Button.Click>
```

## Context variables

Inline C# is compiled against explicit context variables.

### Value code

Value code is compiled as a C# expression and can access:

- `source`: the ambient `x:DataType` instance when available, otherwise `object`
- `root`: the root `x:Class` instance when available, otherwise `object`
- `target`: the current target object for the assignment

Example:

```xaml
<TextBlock.Text>
  <CSharp><![CDATA[
$"{source.Quantity}x {source.ProductName}"
  ]]></CSharp>
</TextBlock.Text>
```

### Event code

Event code can be either:

- a lambda expression, or
- a statement block body

Event code can access:

- `source`
- `root`
- `target`
- `sender`
- `e`
- `arg0`, `arg1`, ... for delegate parameters beyond the common two-parameter form

Example:

```xaml
<Button.Click>
  <CSharp><![CDATA[
source.ClickCount++;
source.LastAction = $"Clicked by {sender}";
  ]]></CSharp>
</Button.Click>
```

## Semantics

### Value positions

- Value code must evaluate to a value compatible with the target property.
- AXSG emits a generated binding expression and reevaluates it when tracked
  `source` members change.
- Dependency tracking is based on instance property and field reads from
  `source`.
- `root` and `target` are available to the code, but they are treated as
  contextual values, not dependency-tracked binding inputs.

### Event positions

- Event code is compiled into a generated wrapper method.
- For lambda form, the lambda is compiled against the event delegate type.
- For statement-block form, AXSG generates a method body with the context
  variables in scope.
- Async lambdas or async statement handlers are not supported in this feature.

## Valid placement

Supported placements:

- property attributes through the compact form
- property elements through the object-element form
- event attributes through the compact form
- event property elements through the object-element form
- style and control-theme setter values through property elements or compact form

Unsupported placements:

- inline code as a standalone object in the visual tree
- inline code inside selector strings
- inline code inside resource keys

## Language service behavior

The language service treats `CSharp` content as real C#.

Supported tooling:

- completion
- hover
- semantic colorization
- go to definition
- find references
- inlay type hints where applicable

For object-element code blocks, the language service resolves the local XAML
context first and then analyzes the block against the generated C# context.

## Diagnostics

Representative diagnostics:

- invalid value expression for the target property
- event code that does not match the event delegate shape
- async event code not supported
- inline code block used outside a value or event property context
- inline code that depends on unresolved `source`, `root`, or `target` members

## Design constraints

- The feature must not require invalid XAML syntax.
- Runtime fallback paths may skip direct runtime reload when inline C# is present;
  generated reload remains the supported path.
- The compiler, runtime, and language service must share the same context model
  so navigation and generated code stay consistent.
