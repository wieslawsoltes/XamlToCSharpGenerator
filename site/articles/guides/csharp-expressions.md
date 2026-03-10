# C# Expressions in XAML

## Purpose

AXSG supports C#-based expressions directly in XAML attribute values while keeping the
XAML document valid XML and preserving the local XAML binding context.

This feature exists for scenarios where plain property bindings are too limited and the
user needs lightweight logic such as interpolation, arithmetic, boolean conditions,
ternaries, or method calls without introducing converters, multibinding scaffolding, or
extra code-behind glue.

## Supported forms

### Explicit expression form

Use the explicit expression marker when the value should always be interpreted as C#:

```xaml
<TextBlock Text="{= FirstName + ' ' + LastName}" />
<TextBlock Text="{= (Price * Quantity).ToString('0.00')}" />
<Button IsEnabled="{= HasAccount && AgreedToTerms}" />
```

### Shorthand expression form

Simple attribute values can also be interpreted as expressions when the target site is an
expression-capable value position:

```xaml
<TextBlock Text="{ProductName}" />
<TextBlock Text="{IsVip ? 'Gold' : 'Standard'}" />
<Button IsEnabled="{HasAccount && AgreedToTerms}" />
```

### Interpolated string form

Interpolated expressions are supported directly:

```xaml
<TextBlock Text="{$'{Quantity}x {ProductName}'}" />
<TextBlock Text="{$'Total: ${Price * Quantity:F2}'}" />
```

## Resolution model

Expression binding resolution uses the current XAML semantic context.

Resolution order is:

1. current binding/data context source (`x:DataType`, inferred compiled-binding source, or
   surrounding typed source)
2. root type when the current scope is class-backed
3. target object when the expression site requires target access
4. event delegate parameters for inline event expressions and lambdas
5. normal C# local/member/static lookup inside the generated expression wrapper

This lets the same expression syntax work across:

- value properties
- setters
- control themes
- templates
- event handlers

## Supported operations

The current implementation supports normal C# expression semantics for:

- member access
- method calls
- indexers
- arithmetic
- comparison operators
- logical operators
- null-coalescing
- ternary expressions
- string interpolation
- format strings inside interpolation
- unary negation and boolean negation

Examples:

```xaml
<TextBlock Text="{= Count * Count}" />
<TextBlock Text="{= Nickname ?? ('alias:' + FirstName)}" />
<TextBlock Text="{= Tags[0] + ', ' + Tags[1]}" />
<TextBlock Text="{= FormatSummary(FirstName, LastName, Count)}" />
<TextBlock IsVisible="{!IsLoading}" />
```

## Event expressions and lambdas

Event values can use inline lambda expressions when the target event has a delegate type
that can be matched by the provided lambda shape.

```xaml
<Button Content="{$'Clicked {ClickCount} times'}"
        Click="{(s, e) => ClickCount++}" />
```

Statement-style event logic that exceeds a single expression should use the inline C#
surface documented in [Inline C# Code](inline-csharp-code).

## Validity rules

- The XAML file remains valid XML.
- The expression site must be one that AXSG recognizes as expression-capable.
- Event lambda expressions are currently synchronous.
- Async inline event lambdas are rejected.
- Expression analysis uses the same project compilation and symbol model as the source
  generator and language service.

## Diagnostics

Common failures surface as AXSG semantic diagnostics, for example:

- missing or unresolved typed source context
- invalid event lambda shape for the target delegate
- unsupported async inline event lambdas
- unresolved members or methods

## Language-service behavior

Expressions participate in the AXSG language service.

Supported editor features include:

- completion
- hover
- go to definition
- go to declaration
- find references
- semantic highlighting
- type inlay hints
- rename propagation

This applies to:

- explicit expressions
- shorthand expressions
- interpolated expressions
- event lambdas

## Runtime and hot reload

Expressions are compiled into generated binding/runtime helpers rather than interpreted
through reflection.

Hot reload support remains available, but changes that alter generated code shape may be
handled through the generated reload path instead of a pure runtime fallback path.

## Relationship to inline C# blocks

Use expression syntax when the code is naturally a value expression or a compact event
lambda.

Use inline C# blocks when you need:

- multiple statements
- imperative event logic
- CDATA content blocks
- explicit control over the inline code payload

See [Inline C# Code](inline-csharp-code) for that surface.
