using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class ViewModelScaffoldEmissionService
{
    private readonly GeneratedSourceHintNameService _generatedSourceHintNameService;
    private readonly Func<string, string> _escape;

    public ViewModelScaffoldEmissionService(
        GeneratedSourceHintNameService generatedSourceHintNameService,
        Func<string, string> escape)
    {
        _generatedSourceHintNameService = generatedSourceHintNameService ?? throw new ArgumentNullException(nameof(generatedSourceHintNameService));
        _escape = escape ?? throw new ArgumentNullException(nameof(escape));
    }

    public int EstimateSourceCapacity(ResolvedViewModel viewModel)
    {
        var estimate = 16_384;
        estimate += viewModel.NamedElements.Length * 72;
        estimate += viewModel.Resources.Length * 220;
        estimate += viewModel.Templates.Length * 220;
        estimate += viewModel.CompiledBindings.Length * 180;
        estimate += viewModel.UnsafeAccessors.Length * 160;
        estimate += viewModel.Styles.Length * 220;
        estimate += viewModel.ControlThemes.Length * 260;
        estimate += viewModel.Includes.Length * 180;
        estimate += viewModel.PassExecutionTrace.Length * 96;
        estimate += viewModel.HotDesignScopeHints.Length * 64;
        estimate += EstimateObjectNodeContribution(viewModel.RootObject);

        if (viewModel.Document.IsClassBacked)
        {
            estimate += 8_192;
        }

        if (estimate < 16_384)
        {
            return 16_384;
        }

        if (estimate > 4_000_000)
        {
            return 4_000_000;
        }

        return estimate;
    }

    public string BuildHintName(ResolvedViewModel viewModel)
    {
        var sourceIdentity = viewModel.BuildUri;
        if (string.IsNullOrWhiteSpace(sourceIdentity))
        {
            sourceIdentity = (viewModel.Document.ClassFullName ?? viewModel.Document.ClassName) + "|" + viewModel.Document.TargetPath;
        }

        var baseName = viewModel.Document.ClassFullName ?? (viewModel.Document.ClassNamespace + "." + viewModel.Document.ClassName);
        return _generatedSourceHintNameService.BuildHintName(baseName, sourceIdentity);
    }

    public string BuildHotDesignDocumentRoleExpression(ResolvedViewModel viewModel)
    {
        var roleToken = viewModel.HotDesignArtifactKind switch
        {
            ResolvedHotDesignArtifactKind.Template => "Template",
            ResolvedHotDesignArtifactKind.ResourceDictionary => "Resources",
            ResolvedHotDesignArtifactKind.ControlTheme => "Theme",
            _ when !viewModel.Document.IsClassBacked => "Include",
            _ => "Root"
        };

        return "global::XamlToCSharpGenerator.Runtime.SourceGenHotDesignDocumentRole." + roleToken;
    }

    public string BuildHotDesignArtifactKindExpression(ResolvedViewModel viewModel)
    {
        return "global::XamlToCSharpGenerator.Runtime.SourceGenHotDesignArtifactKind." + viewModel.HotDesignArtifactKind;
    }

    public string BuildHotDesignScopeHintsExpression(ResolvedViewModel viewModel)
    {
        var hints = viewModel.HotDesignScopeHints;
        if (hints.IsDefaultOrEmpty)
        {
            return "null";
        }

        if (hints.Length == 1)
        {
            return $"new string[] {{ \"{_escape(hints[0])}\" }}";
        }

        return $"new string[] {{ \"{_escape(hints[0])}\", \"{_escape(hints[1])}\" }}";
    }

    public void EmitCompiledBindingAccessorMethods(
        StringBuilder sourceBuilder,
        CompiledBindingAccessorEmissionPlan compiledBindingAccessorEmissionPlan)
    {
        if (compiledBindingAccessorEmissionPlan.Methods.IsDefaultOrEmpty)
        {
            return;
        }

        for (var index = 0; index < compiledBindingAccessorEmissionPlan.Methods.Length; index++)
        {
            var method = compiledBindingAccessorEmissionPlan.Methods[index];
            sourceBuilder.AppendLine(
                $"        private static object? {method.MethodName}({method.SourceTypeName} source)");
            sourceBuilder.AppendLine("        {");
            sourceBuilder.AppendLine(
                $"            return __CompiledBindingAccessor({method.CompiledBindingIndex}, source);");
            sourceBuilder.AppendLine("        }");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine(
                $"        private static object? {method.BindingMethodName}({method.SourceTypeName} source)");
            sourceBuilder.AppendLine("        {");
            sourceBuilder.AppendLine(
                $"            return {method.MethodName}(source);");
            sourceBuilder.AppendLine("        }");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine(
                $"        private static object? {method.ObjectMethodName}(object source)");
            sourceBuilder.AppendLine("        {");
            sourceBuilder.AppendLine(
                $"            return __CompiledBindingAccessor({method.CompiledBindingIndex}, source);");
            sourceBuilder.AppendLine("        }");
            sourceBuilder.AppendLine();
        }
    }

    public void EmitUnsafeAccessorMethods(
        StringBuilder sourceBuilder,
        ImmutableArray<ResolvedUnsafeAccessorDefinition> unsafeAccessors)
    {
        if (unsafeAccessors.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var unsafeAccessor in unsafeAccessors
                     .GroupBy(static accessor => accessor.MethodName, StringComparer.Ordinal)
                     .Select(static group => group.First())
                     .OrderBy(static accessor => accessor.MethodName, StringComparer.Ordinal))
        {
            sourceBuilder.AppendLine(
                $"        [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = \"{_escape(unsafeAccessor.UnsafeAccessorTargetName)}\")]");
            sourceBuilder.Append("        private static extern ");
            sourceBuilder.Append(unsafeAccessor.ReturnTypeName);
            sourceBuilder.Append(' ');
            sourceBuilder.Append(unsafeAccessor.MethodName);
            sourceBuilder.Append('(');
            sourceBuilder.Append(unsafeAccessor.DeclaringTypeName);
            sourceBuilder.Append(" __instance");
            for (var index = 0; index < unsafeAccessor.ParameterTypeNames.Length; index++)
            {
                sourceBuilder.Append(", ");
                sourceBuilder.Append(unsafeAccessor.ParameterTypeNames[index]);
                sourceBuilder.Append(" __arg");
                sourceBuilder.Append(index.ToString(CultureInfo.InvariantCulture));
            }

            sourceBuilder.AppendLine(");");
            sourceBuilder.AppendLine();
        }
    }

    private static int EstimateObjectNodeContribution(ResolvedObjectNode node)
    {
        var estimate = 320;
        estimate += node.PropertyAssignments.Length * 96;
        estimate += node.PropertyElementAssignments.Length * 96;
        estimate += node.EventSubscriptions.Length * 80;
        estimate += node.ChildAddInstructions.Length * 48;

        for (var childIndex = 0; childIndex < node.Children.Length; childIndex++)
        {
            estimate += EstimateObjectNodeContribution(node.Children[childIndex]);
        }

        return estimate;
    }
}
