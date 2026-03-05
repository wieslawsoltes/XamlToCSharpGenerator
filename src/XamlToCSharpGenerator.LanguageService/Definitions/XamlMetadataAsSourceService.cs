using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Symbols;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal static class XamlMetadataAsSourceService
{
    private static readonly ConcurrentDictionary<string, string> Documents = new(StringComparer.Ordinal);

    public static bool TryCreateTypeLocation(
        XamlAnalysisResult analysis,
        AvaloniaTypeInfo typeInfo,
        out AvaloniaSymbolSourceLocation location)
    {
        return TryCreateTypeLocation(
            analysis.Compilation,
            typeInfo.FullTypeName,
            typeInfo.AssemblyName,
            out location);
    }

    public static bool TryCreateTypeLocation(
        XamlAnalysisResult analysis,
        XamlResolvedTypeReference typeReference,
        out AvaloniaSymbolSourceLocation location)
    {
        return TryCreateTypeLocation(
            analysis.Compilation,
            typeReference.FullTypeName,
            typeReference.AssemblyName,
            out location);
    }

    public static bool TryCreateTypeLocation(
        Compilation? compilation,
        string fullTypeName,
        string? assemblyName,
        out AvaloniaSymbolSourceLocation location)
    {
        location = default;
        if (compilation is null ||
            !TryResolveTypeSymbol(compilation, fullTypeName, assemblyName, out var typeSymbol) ||
            !TryBuildDocument(typeSymbol, targetMember: null, out var documentText, out var targetRange))
        {
            return false;
        }

        var documentId = RegisterDocument(
            "type:" + (assemblyName ?? string.Empty) + ":" + fullTypeName,
            documentText);
        location = new AvaloniaSymbolSourceLocation(
            XamlMetadataSymbolUri.CreateMetadataDocumentUri(fullTypeName, documentId),
            targetRange);
        return true;
    }

    public static bool TryCreatePropertyLocation(
        XamlAnalysisResult analysis,
        string ownerTypeName,
        string propertyName,
        string? assemblyName,
        out AvaloniaSymbolSourceLocation location)
    {
        location = default;
        if (analysis.Compilation is null ||
            !TryResolveTypeSymbol(analysis.Compilation, ownerTypeName, assemblyName, out var typeSymbol) ||
            !TryResolvePropertySymbol(typeSymbol, propertyName, out var targetMember) ||
            !TryBuildDocument(typeSymbol, targetMember, out var documentText, out var targetRange))
        {
            return false;
        }

        var documentId = RegisterDocument(
            "property:" + (assemblyName ?? string.Empty) + ":" + ownerTypeName + ":" + propertyName,
            documentText);
        location = new AvaloniaSymbolSourceLocation(
            XamlMetadataSymbolUri.CreateMetadataDocumentUri(ownerTypeName, documentId, propertyName),
            targetRange);
        return true;
    }

    public static bool TryGetDocumentText(string documentId, out string text)
    {
        return Documents.TryGetValue(documentId, out text!);
    }

    private static bool TryResolveTypeSymbol(
        Compilation compilation,
        string fullTypeName,
        string? assemblyName,
        out INamedTypeSymbol typeSymbol)
    {
        typeSymbol = null!;
        foreach (var assemblySymbol in EnumerateAssemblies(compilation, assemblyName))
        {
            var candidate = assemblySymbol.GetTypeByMetadataName(fullTypeName);
            if (candidate is not null)
            {
                typeSymbol = candidate;
                return true;
            }

            foreach (var type in EnumerateTypes(assemblySymbol.GlobalNamespace))
            {
                if (!string.Equals(
                        type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        fullTypeName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                typeSymbol = type;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<IAssemblySymbol> EnumerateAssemblies(Compilation compilation, string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName) ||
            string.Equals(compilation.Assembly.Identity.Name, assemblyName, StringComparison.Ordinal))
        {
            yield return compilation.Assembly;
        }

        foreach (var assemblySymbol in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (!string.IsNullOrWhiteSpace(assemblyName) &&
                !string.Equals(assemblySymbol.Identity.Name, assemblyName, StringComparison.Ordinal))
            {
                continue;
            }

            yield return assemblySymbol;
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            foreach (var nested in EnumerateTypeAndNestedTypes(type))
            {
                yield return nested;
            }
        }

        foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (var type in EnumerateTypes(nestedNamespace))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypeAndNestedTypes(INamedTypeSymbol typeSymbol)
    {
        yield return typeSymbol;
        foreach (var nestedType in typeSymbol.GetTypeMembers())
        {
            foreach (var nested in EnumerateTypeAndNestedTypes(nestedType))
            {
                yield return nested;
            }
        }
    }

    private static bool TryResolvePropertySymbol(
        INamedTypeSymbol typeSymbol,
        string propertyName,
        out ISymbol targetMember)
    {
        targetMember = typeSymbol.GetMembers(propertyName)
            .FirstOrDefault(static member => member is IPropertySymbol or IFieldSymbol)!;
        if (targetMember is not null)
        {
            return true;
        }

        var backingFieldName = propertyName + "Property";
        targetMember = typeSymbol.GetMembers(backingFieldName)
            .FirstOrDefault(static member => member is IFieldSymbol)!;
        return targetMember is not null;
    }

    private static bool TryBuildDocument(
        INamedTypeSymbol typeSymbol,
        ISymbol? targetMember,
        out string documentText,
        out SourceRange targetRange)
    {
        var writer = new MetadataDocumentWriter(typeSymbol, targetMember);
        writer.Write();
        documentText = writer.ToString();
        targetRange = writer.TargetRange;
        return !string.IsNullOrWhiteSpace(documentText);
    }

    private static string RegisterDocument(string key, string text)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key + "\n" + text));
        var documentId = Convert.ToHexString(hashBytes);
        Documents.TryAdd(documentId, text);
        return documentId;
    }

    private static string GetAccessibilityText(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Private => "private",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => string.Empty
        };
    }

    private static string GetTypeKeyword(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeKind == TypeKind.Interface)
        {
            return "interface";
        }

        if (typeSymbol.TypeKind == TypeKind.Struct)
        {
            return typeSymbol.IsRecord ? "record struct" : "struct";
        }

        if (typeSymbol.TypeKind == TypeKind.Enum)
        {
            return "enum";
        }

        if (typeSymbol.TypeKind == TypeKind.Delegate)
        {
            return "delegate";
        }

        return typeSymbol.IsRecord ? "record" : "class";
    }

    private static string FormatTypeName(ITypeSymbol typeSymbol)
    {
        return typeSymbol
            .ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
            .Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    private static string FormatTypeHeader(INamedTypeSymbol typeSymbol)
    {
        var builder = new StringBuilder();
        var accessibility = GetAccessibilityText(typeSymbol.DeclaredAccessibility);
        if (!string.IsNullOrWhiteSpace(accessibility))
        {
            builder.Append(accessibility);
            builder.Append(' ');
        }

        if (typeSymbol.IsStatic)
        {
            builder.Append("static ");
        }
        else
        {
            if (typeSymbol.IsAbstract && typeSymbol.TypeKind == TypeKind.Class && !typeSymbol.IsRecord)
            {
                builder.Append("abstract ");
            }

            if (typeSymbol.IsSealed && typeSymbol.TypeKind == TypeKind.Class && !typeSymbol.IsStatic)
            {
                builder.Append("sealed ");
            }
        }

        builder.Append(GetTypeKeyword(typeSymbol));
        builder.Append(' ');
        builder.Append(typeSymbol.Name);

        if (typeSymbol.TypeParameters.Length > 0)
        {
            builder.Append('<');
            for (var index = 0; index < typeSymbol.TypeParameters.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(typeSymbol.TypeParameters[index].Name);
            }

            builder.Append('>');
        }

        var baseTypes = new List<string>();
        if (typeSymbol.TypeKind == TypeKind.Class &&
            typeSymbol.BaseType is not null &&
            typeSymbol.BaseType.SpecialType != SpecialType.System_Object)
        {
            baseTypes.Add(FormatTypeName(typeSymbol.BaseType));
        }

        foreach (var interfaceType in typeSymbol.Interfaces)
        {
            baseTypes.Add(FormatTypeName(interfaceType));
        }

        if (baseTypes.Count > 0)
        {
            builder.Append(" : ");
            builder.Append(string.Join(", ", baseTypes));
        }

        return builder.ToString();
    }

    private static string FormatMemberType(ITypeSymbol? typeSymbol)
    {
        return typeSymbol is null ? "void" : FormatTypeName(typeSymbol);
    }

    private static string FormatParameters(ImmutableArray<IParameterSymbol> parameters)
    {
        if (parameters.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var index = 0; index < parameters.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            var parameter = parameters[index];
            if (parameter.RefKind == RefKind.Ref)
            {
                builder.Append("ref ");
            }
            else if (parameter.RefKind == RefKind.Out)
            {
                builder.Append("out ");
            }
            else if (parameter.RefKind == RefKind.In)
            {
                builder.Append("in ");
            }

            if (parameter.IsParams)
            {
                builder.Append("params ");
            }

            builder.Append(FormatTypeName(parameter.Type));
            builder.Append(' ');
            builder.Append(parameter.Name);
        }

        return builder.ToString();
    }

    private sealed class MetadataDocumentWriter
    {
        private readonly StringBuilder _builder = new();
        private readonly INamedTypeSymbol _typeSymbol;
        private readonly ISymbol? _targetMember;
        private int _line;

        public MetadataDocumentWriter(INamedTypeSymbol typeSymbol, ISymbol? targetMember)
        {
            _typeSymbol = typeSymbol;
            _targetMember = targetMember;
        }

        public SourceRange TargetRange { get; private set; } = new(
            new SourcePosition(0, 0),
            new SourcePosition(0, 1));

        public void Write()
        {
            var namespaceName = _typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(namespaceName))
            {
                AppendLine("// AXSG metadata as source");
                AppendLine();
                AppendLine("namespace " + namespaceName);
                AppendLine("{");
                WriteType(_typeSymbol, indentLevel: 1);
                AppendLine("}");
                return;
            }

            AppendLine("// AXSG metadata as source");
            AppendLine();
            WriteType(_typeSymbol, indentLevel: 0);
        }

        public override string ToString()
        {
            return _builder.ToString();
        }

        private void WriteType(INamedTypeSymbol typeSymbol, int indentLevel)
        {
            if (typeSymbol.TypeKind == TypeKind.Delegate &&
                typeSymbol.DelegateInvokeMethod is { } invokeMethod)
            {
                AppendTrackedLine(
                    typeSymbol,
                    indentLevel,
                    $"{GetAccessibilityText(typeSymbol.DeclaredAccessibility)} delegate {FormatMemberType(invokeMethod.ReturnType)} {typeSymbol.Name}({FormatParameters(invokeMethod.Parameters)});");
                return;
            }

            AppendTrackedLine(typeSymbol, indentLevel, FormatTypeHeader(typeSymbol));
            AppendLine(indentLevel, "{");

            if (typeSymbol.TypeKind == TypeKind.Enum)
            {
                var enumFields = typeSymbol.GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(static field => field.HasConstantValue)
                    .ToArray();

                for (var index = 0; index < enumFields.Length; index++)
                {
                    var suffix = index + 1 < enumFields.Length ? "," : string.Empty;
                    AppendTrackedLine(enumFields[index], indentLevel + 1, enumFields[index].Name + suffix);
                }
            }
            else
            {
                foreach (var member in typeSymbol.GetMembers().Where(ShouldEmitMember))
                {
                    WriteMember(member, indentLevel + 1);
                }
            }

            AppendLine(indentLevel, "}");
        }

        private void WriteMember(ISymbol member, int indentLevel)
        {
            switch (member)
            {
                case IPropertySymbol property:
                    {
                        var accessorSuffix = new StringBuilder();
                        accessorSuffix.Append("{ ");
                        if (property.GetMethod is not null)
                        {
                            accessorSuffix.Append("get; ");
                        }

                        if (property.SetMethod is not null)
                        {
                            accessorSuffix.Append("set; ");
                        }

                        accessorSuffix.Append('}');
                        AppendTrackedLine(
                            property,
                            indentLevel,
                            $"{GetAccessibilityText(property.DeclaredAccessibility)} {FormatMemberType(property.Type)} {property.Name} {accessorSuffix}");
                        break;
                    }

                case IFieldSymbol field:
                    {
                        var modifierBuilder = new StringBuilder(GetAccessibilityText(field.DeclaredAccessibility));
                        if (field.IsConst)
                        {
                            modifierBuilder.Append(" const");
                        }
                        else
                        {
                            if (field.IsStatic)
                            {
                                modifierBuilder.Append(" static");
                            }

                            if (field.IsReadOnly)
                            {
                                modifierBuilder.Append(" readonly");
                            }
                        }

                        AppendTrackedLine(
                            field,
                            indentLevel,
                            $"{modifierBuilder} {FormatMemberType(field.Type)} {field.Name};");
                        break;
                    }

                case IMethodSymbol method when method.MethodKind == MethodKind.Constructor:
                    AppendTrackedLine(
                        method,
                        indentLevel,
                        $"{GetAccessibilityText(method.DeclaredAccessibility)} {_typeSymbol.Name}({FormatParameters(method.Parameters)}) {{ }}");
                    break;

                case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary:
                    {
                        var methodBuilder = new StringBuilder();
                        methodBuilder.Append(GetAccessibilityText(method.DeclaredAccessibility));
                        if (method.IsStatic)
                        {
                            methodBuilder.Append(" static");
                        }

                        if (method.IsAbstract)
                        {
                            methodBuilder.Append(" abstract");
                        }

                        methodBuilder.Append(' ');
                        methodBuilder.Append(FormatMemberType(method.ReturnType));
                        methodBuilder.Append(' ');
                        methodBuilder.Append(method.Name);
                        if (method.TypeParameters.Length > 0)
                        {
                            methodBuilder.Append('<');
                            for (var index = 0; index < method.TypeParameters.Length; index++)
                            {
                                if (index > 0)
                                {
                                    methodBuilder.Append(", ");
                                }

                                methodBuilder.Append(method.TypeParameters[index].Name);
                            }

                            methodBuilder.Append('>');
                        }

                        methodBuilder.Append('(');
                        methodBuilder.Append(FormatParameters(method.Parameters));
                        methodBuilder.Append(')');
                        methodBuilder.Append(method.IsAbstract || _typeSymbol.TypeKind == TypeKind.Interface ? ";" : " { }");
                        AppendTrackedLine(method, indentLevel, methodBuilder.ToString());
                        break;
                    }

                case IEventSymbol eventSymbol:
                    AppendTrackedLine(
                        eventSymbol,
                        indentLevel,
                        $"{GetAccessibilityText(eventSymbol.DeclaredAccessibility)} event {FormatMemberType(eventSymbol.Type)} {eventSymbol.Name};");
                    break;

                case INamedTypeSymbol nestedType:
                    WriteType(nestedType, indentLevel);
                    break;
            }
        }

        private static bool ShouldEmitMember(ISymbol member)
        {
            if (member.IsImplicitlyDeclared)
            {
                return false;
            }

            if (member.DeclaredAccessibility != Accessibility.Public &&
                member.DeclaredAccessibility != Accessibility.Protected &&
                member.DeclaredAccessibility != Accessibility.ProtectedOrInternal)
            {
                return false;
            }

            return member switch
            {
                IMethodSymbol method => method.MethodKind is MethodKind.Constructor or MethodKind.Ordinary,
                IPropertySymbol => true,
                IFieldSymbol => true,
                IEventSymbol => true,
                INamedTypeSymbol => true,
                _ => false
            };
        }

        private void AppendTrackedLine(ISymbol symbol, int indentLevel, string text)
        {
            var start = new SourcePosition(_line, indentLevel * 4);
            AppendLine(indentLevel, text);

            if (_targetMember is not null)
            {
                if (SymbolEqualityComparer.Default.Equals(symbol, _targetMember))
                {
                    TargetRange = new SourceRange(start, new SourcePosition(start.Line, start.Character + Math.Max(1, text.Length)));
                }
            }
            else if (SymbolEqualityComparer.Default.Equals(symbol, _typeSymbol))
            {
                TargetRange = new SourceRange(start, new SourcePosition(start.Line, start.Character + Math.Max(1, text.Length)));
            }
        }

        private void AppendLine(int indentLevel, string text)
        {
            if (indentLevel > 0)
            {
                _builder.Append(' ', indentLevel * 4);
            }

            _builder.AppendLine(text);
            _line++;
        }

        private void AppendLine(string text)
        {
            _builder.AppendLine(text);
            _line++;
        }

        private void AppendLine()
        {
            _builder.AppendLine();
            _line++;
        }
    }
}
