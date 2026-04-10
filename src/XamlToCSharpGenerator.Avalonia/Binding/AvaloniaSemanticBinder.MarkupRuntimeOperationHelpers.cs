using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static string BuildTypedStaticResourceCoercionExpression(ITypeSymbol targetType, string expression)
    {
        var coercedTargetType = targetType.WithNullableAnnotation(NullableAnnotation.None);
        if (coercedTargetType.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_Object)
        {
            return expression;
        }

        return "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.CoerceStaticResourceValue<" +
               coercedTargetType.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat) +
               ">(" +
               expression +
               ")";
    }

    private static string BuildStaticResourceOperationExpression(ResolvedResourceKeyExpression resourceKeyExpression)
    {
        return "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideStaticResource(" +
               resourceKeyExpression.Expression + ", " +
               MarkupContextServiceProviderToken + ", " +
               MarkupContextRootObjectToken + ", " +
               MarkupContextIntermediateRootObjectToken + ", " +
               MarkupContextTargetObjectToken + ", " +
               MarkupContextTargetPropertyToken + ", " +
               MarkupContextBaseUriToken + ", " +
               MarkupContextParentStackToken + ")";
    }

    private static string BuildDynamicResourceOperationExpression(ResolvedResourceKeyExpression resourceKeyExpression)
    {
        return "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideDynamicResource(" +
               resourceKeyExpression.Expression + ", " +
               MarkupContextServiceProviderToken + ", " +
               MarkupContextRootObjectToken + ", " +
               MarkupContextIntermediateRootObjectToken + ", " +
               MarkupContextTargetObjectToken + ", " +
               MarkupContextTargetPropertyToken + ", " +
               MarkupContextBaseUriToken + ", " +
               MarkupContextParentStackToken + ")";
    }

    private static string BuildReferenceOperationExpression(string referenceName)
    {
        return "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideReference(\"" +
               Escape(referenceName) +
               "\", " +
               MarkupContextServiceProviderToken + ", " +
               MarkupContextRootObjectToken + ", " +
               MarkupContextIntermediateRootObjectToken + ", " +
               MarkupContextTargetObjectToken + ", " +
               MarkupContextTargetPropertyToken + ", " +
               MarkupContextBaseUriToken + ", " +
               MarkupContextParentStackToken + ")";
    }
}
