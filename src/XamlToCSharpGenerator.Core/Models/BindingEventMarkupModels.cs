namespace XamlToCSharpGenerator.Core.Models;

public readonly struct RelativeSourceMarkup
{
    public RelativeSourceMarkup(string? mode, string? ancestorTypeToken, int? ancestorLevel, string? tree)
    {
        Mode = mode;
        AncestorTypeToken = ancestorTypeToken;
        AncestorLevel = ancestorLevel;
        Tree = tree;
    }

    public string? Mode { get; }

    public string? AncestorTypeToken { get; }

    public int? AncestorLevel { get; }

    public string? Tree { get; }
}

public readonly struct ResolveByNameReferenceToken
{
    public ResolveByNameReferenceToken(string name, bool fromMarkupExtension)
    {
        Name = name;
        FromMarkupExtension = fromMarkupExtension;
    }

    public string Name { get; }

    public bool FromMarkupExtension { get; }
}

public readonly struct EventBindingMarkup
{
    public EventBindingMarkup(
        ResolvedEventBindingTargetKind targetKind,
        ResolvedEventBindingSourceMode sourceMode,
        string targetPath,
        string? parameterPath,
        string? parameterValueExpression,
        bool hasParameterValueExpression,
        bool passEventArgs)
    {
        TargetKind = targetKind;
        SourceMode = sourceMode;
        TargetPath = targetPath;
        ParameterPath = parameterPath;
        ParameterValueExpression = parameterValueExpression;
        HasParameterValueExpression = hasParameterValueExpression;
        PassEventArgs = passEventArgs;
    }

    public ResolvedEventBindingTargetKind TargetKind { get; }

    public ResolvedEventBindingSourceMode SourceMode { get; }

    public string TargetPath { get; }

    public string? ParameterPath { get; }

    public string? ParameterValueExpression { get; }

    public bool HasParameterValueExpression { get; }

    public bool PassEventArgs { get; }
}

public readonly struct BindingMarkup
{
    public BindingMarkup(
        bool isCompiledBinding,
        string path,
        string? mode,
        string? elementName,
        RelativeSourceMarkup? relativeSource,
        string? source,
        string? dataType,
        string? converter,
        string? converterCulture,
        string? converterParameter,
        string? stringFormat,
        string? fallbackValue,
        string? targetNullValue,
        string? delay,
        string? priority,
        string? updateSourceTrigger,
        bool hasSourceConflict,
        string? sourceConflictMessage)
    {
        IsCompiledBinding = isCompiledBinding;
        Path = path;
        Mode = mode;
        ElementName = elementName;
        RelativeSource = relativeSource;
        Source = source;
        DataType = dataType;
        Converter = converter;
        ConverterCulture = converterCulture;
        ConverterParameter = converterParameter;
        StringFormat = stringFormat;
        FallbackValue = fallbackValue;
        TargetNullValue = targetNullValue;
        Delay = delay;
        Priority = priority;
        UpdateSourceTrigger = updateSourceTrigger;
        HasSourceConflict = hasSourceConflict;
        SourceConflictMessage = sourceConflictMessage;
    }

    public bool IsCompiledBinding { get; }

    public string Path { get; }

    public string? Mode { get; }

    public string? ElementName { get; }

    public RelativeSourceMarkup? RelativeSource { get; }

    public string? Source { get; }

    public string? DataType { get; }

    public string? Converter { get; }

    public string? ConverterCulture { get; }

    public string? ConverterParameter { get; }

    public string? StringFormat { get; }

    public string? FallbackValue { get; }

    public string? TargetNullValue { get; }

    public string? Delay { get; }

    public string? Priority { get; }

    public string? UpdateSourceTrigger { get; }

    public bool HasSourceConflict { get; }

    public string? SourceConflictMessage { get; }
}

public readonly struct XBindMarkup
{
    public XBindMarkup(
        string path,
        string? mode,
        string? bindBack,
        string? dataType,
        string? converter,
        string? converterCulture,
        string? converterParameter,
        string? stringFormat,
        string? fallbackValue,
        string? targetNullValue,
        string? delay,
        string? priority,
        string? updateSourceTrigger)
    {
        Path = path;
        Mode = mode;
        BindBack = bindBack;
        DataType = dataType;
        Converter = converter;
        ConverterCulture = converterCulture;
        ConverterParameter = converterParameter;
        StringFormat = stringFormat;
        FallbackValue = fallbackValue;
        TargetNullValue = targetNullValue;
        Delay = delay;
        Priority = priority;
        UpdateSourceTrigger = updateSourceTrigger;
    }

    public string Path { get; }

    public string? Mode { get; }

    public string? BindBack { get; }

    public string? DataType { get; }

    public string? Converter { get; }

    public string? ConverterCulture { get; }

    public string? ConverterParameter { get; }

    public string? StringFormat { get; }

    public string? FallbackValue { get; }

    public string? TargetNullValue { get; }

    public string? Delay { get; }

    public string? Priority { get; }

    public string? UpdateSourceTrigger { get; }
}
