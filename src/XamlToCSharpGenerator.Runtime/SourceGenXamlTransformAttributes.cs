using System;

namespace XamlToCSharpGenerator.Runtime;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SourceGenXamlTypeAliasAttribute : Attribute
{
    public SourceGenXamlTypeAliasAttribute(string xmlNamespace, string xamlTypeName, string clrTypeName)
    {
        XmlNamespace = xmlNamespace;
        XamlTypeName = xamlTypeName;
        ClrTypeName = clrTypeName;
    }

    public string XmlNamespace { get; }

    public string XamlTypeName { get; }

    public string ClrTypeName { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SourceGenXmlnsDefinitionAttribute : Attribute
{
    public SourceGenXmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace)
    {
        XmlNamespace = xmlNamespace;
        ClrNamespace = clrNamespace;
    }

    public string XmlNamespace { get; }

    public string ClrNamespace { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SourceGenXamlPropertyAliasAttribute : Attribute
{
    public SourceGenXamlPropertyAliasAttribute(string targetTypeName, string xamlPropertyName, string clrPropertyName)
    {
        TargetTypeName = targetTypeName;
        XamlPropertyName = xamlPropertyName;
        ClrPropertyName = clrPropertyName;
    }

    public string TargetTypeName { get; }

    public string XamlPropertyName { get; }

    public string ClrPropertyName { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SourceGenXamlAvaloniaPropertyAliasAttribute : Attribute
{
    public SourceGenXamlAvaloniaPropertyAliasAttribute(
        string targetTypeName,
        string xamlPropertyName,
        string avaloniaPropertyOwnerTypeName,
        string avaloniaPropertyFieldName)
    {
        TargetTypeName = targetTypeName;
        XamlPropertyName = xamlPropertyName;
        AvaloniaPropertyOwnerTypeName = avaloniaPropertyOwnerTypeName;
        AvaloniaPropertyFieldName = avaloniaPropertyFieldName;
    }

    public string TargetTypeName { get; }

    public string XamlPropertyName { get; }

    public string AvaloniaPropertyOwnerTypeName { get; }

    public string AvaloniaPropertyFieldName { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SourceGenGlobalXmlnsPrefixAttribute : Attribute
{
    public SourceGenGlobalXmlnsPrefixAttribute(string prefix, string xmlNamespace)
    {
        Prefix = prefix;
        XmlNamespace = xmlNamespace;
    }

    public string Prefix { get; }

    public string XmlNamespace { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class SourceGenAllowImplicitXmlnsDeclarationAttribute : Attribute
{
    public SourceGenAllowImplicitXmlnsDeclarationAttribute(bool allow = true)
    {
        Allow = allow;
    }

    public bool Allow { get; }
}
