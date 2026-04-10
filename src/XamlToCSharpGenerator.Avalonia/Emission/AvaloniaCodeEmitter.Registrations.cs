using System.Text;

namespace XamlToCSharpGenerator.Avalonia.Emission;

public sealed partial class AvaloniaCodeEmitter
{
    private static void EmitArtifactRegistrationMembers(EmitContext context, StringBuilder sourceBuilder)
    {
        var viewModel = context.ViewModel;
        sourceBuilder.AppendLine("        [ModuleInitializer]");
        sourceBuilder.AppendLine("        internal static void __InitializeXamlSourceGenArtifacts()");
        sourceBuilder.AppendLine("        {");
        sourceBuilder.AppendLine("            __RegisterXamlSourceGenArtifacts();");
        sourceBuilder.AppendLine("        }");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("        internal static void __RegisterXamlSourceGenArtifacts()");
        sourceBuilder.AppendLine("        {");
        sourceBuilder.AppendLine(
            $"            global::XamlToCSharpGenerator.Runtime.SourceGenArtifactRegistryRuntime.ResetDocumentRegistries(\"{context.EscapedUri}\");");
        if (context.KnownTypeNames.Length > 0)
        {
            sourceBuilder.AppendLine(
                $"            global::XamlToCSharpGenerator.Runtime.SourceGenKnownTypeRegistry.RegisterTypes({ParentStackEmissionService.BuildTypeofArgumentListExpression(context.KnownTypeNames)});");
        }

        sourceBuilder.AppendLine(
            $"            global::XamlToCSharpGenerator.Runtime.XamlSourceGenTypeUriRegistry.Register(typeof({context.ClassName}), \"{context.EscapedUri}\");");
        sourceBuilder.AppendLine(
            $"            global::XamlToCSharpGenerator.Runtime.XamlSourceGenArtifactRefreshRegistry.Register(typeof({context.ClassName}), __RegisterXamlSourceGenArtifacts);");
        sourceBuilder.AppendLine($"            global::XamlToCSharpGenerator.Runtime.XamlSourceGenRegistry.Register(\"{context.EscapedUri}\", static __serviceProvider =>");
        sourceBuilder.AppendLine("            {");
        sourceBuilder.AppendLine("                var __instance = __CreateRootInstance(__serviceProvider);");
        sourceBuilder.AppendLine("                __PopulateGeneratedObjectGraph(__instance, __serviceProvider, __rootConstructedWithInitializer: true);");
        if (!viewModel.Document.IsClassBacked && viewModel.EnableHotReload)
        {
            sourceBuilder.AppendLine("                __RegisterSourceGenHotReload(__instance);");
        }

        sourceBuilder.AppendLine("                return __instance;");
        sourceBuilder.AppendLine("            });");

        if (viewModel.CreateSourceInfo)
        {
            SourceInfoRegistrationEmissionService.EmitRegistrations(viewModel, sourceBuilder, context.EscapedUri);
        }

        foreach (var resource in viewModel.Resources)
        {
            sourceBuilder.AppendLine(
                $"            global::XamlToCSharpGenerator.Runtime.XamlResourceRegistry.Register(new global::XamlToCSharpGenerator.Runtime.SourceGenResourceDescriptor(\"{context.EscapedUri}\", \"{CSharpLiteralEmissionService.EscapeStringLiteral(resource.Key)}\", \"{CSharpLiteralEmissionService.EscapeStringLiteral(resource.TypeName)}\", \"{CSharpLiteralEmissionService.EscapeStringLiteral(resource.RawXaml)}\"));");
        }

        foreach (var template in viewModel.Templates)
        {
            sourceBuilder.AppendLine(
                $"            global::XamlToCSharpGenerator.Runtime.XamlTemplateRegistry.Register(new global::XamlToCSharpGenerator.Runtime.SourceGenTemplateDescriptor(\"{context.EscapedUri}\", \"{CSharpLiteralEmissionService.EscapeStringLiteral(template.Kind)}\", {CSharpLiteralEmissionService.QuoteOrNull(template.Key)}, {CSharpLiteralEmissionService.QuoteOrNull(template.TargetTypeName)}, {CSharpLiteralEmissionService.QuoteOrNull(template.DataType)}, \"{CSharpLiteralEmissionService.EscapeStringLiteral(template.RawXaml)}\"));");
        }

        foreach (var style in viewModel.Styles)
        {
            sourceBuilder.AppendLine(
                $"            global::XamlToCSharpGenerator.Runtime.XamlStyleRegistry.Register(new global::XamlToCSharpGenerator.Runtime.SourceGenStyleDescriptor(\"{context.EscapedUri}\", {CSharpLiteralEmissionService.QuoteOrNull(style.Key)}, \"{CSharpLiteralEmissionService.EscapeStringLiteral(style.Selector)}\", {CSharpLiteralEmissionService.QuoteOrNull(style.TargetTypeName)}, \"{CSharpLiteralEmissionService.EscapeStringLiteral(style.RawXaml)}\"));");
        }

        for (var controlThemeIndex = 0; controlThemeIndex < viewModel.ControlThemes.Length; controlThemeIndex++)
        {
            var controlTheme = viewModel.ControlThemes[controlThemeIndex];
            sourceBuilder.AppendLine(
                $"            global::XamlToCSharpGenerator.Runtime.XamlControlThemeRegistry.Register(new global::XamlToCSharpGenerator.Runtime.SourceGenControlThemeDescriptor(\"{context.EscapedUri}\", {CSharpLiteralEmissionService.QuoteOrNull(controlTheme.Key)}, {CSharpLiteralEmissionService.QuoteOrNull(controlTheme.TargetTypeName)}, {CSharpLiteralEmissionService.QuoteOrNull(controlTheme.BasedOn)}, {CSharpLiteralEmissionService.QuoteOrNull(controlTheme.ThemeVariant)}, \"{CSharpLiteralEmissionService.EscapeStringLiteral(controlTheme.RawXaml)}\", Factory: static () => __BuildGeneratedControlTheme({controlThemeIndex})));");
        }

        foreach (var include in viewModel.Includes)
        {
            sourceBuilder.AppendLine(
                $"            global::XamlToCSharpGenerator.Runtime.XamlIncludeRegistry.Register(new global::XamlToCSharpGenerator.Runtime.SourceGenIncludeDescriptor(\"{context.EscapedUri}\", \"{CSharpLiteralEmissionService.EscapeStringLiteral(include.Kind)}\", \"{CSharpLiteralEmissionService.EscapeStringLiteral(include.Source)}\", \"{CSharpLiteralEmissionService.EscapeStringLiteral(include.MergeTarget)}\", {CSharpLiteralEmissionService.BoolLiteral(include.IsAbsoluteUri)}, \"{CSharpLiteralEmissionService.EscapeStringLiteral(include.RawXaml)}\"));");

            if (!string.IsNullOrWhiteSpace(include.ResolvedSourceUri) && include.IsProjectLocal)
            {
                sourceBuilder.AppendLine(
                    $"            global::XamlToCSharpGenerator.Runtime.XamlIncludeGraphRegistry.Register(\"{context.EscapedUri}\", \"{CSharpLiteralEmissionService.EscapeStringLiteral(include.ResolvedSourceUri!)}\", \"{CSharpLiteralEmissionService.EscapeStringLiteral(include.MergeTarget)}\");");
            }
        }

        for (var index = 0; index < viewModel.CompiledBindings.Length; index++)
        {
            var compiledBinding = viewModel.CompiledBindings[index];
            var compiledBindingMethodName = CompiledBindingEmissionService.ResolveObjectAccessorMethodName(
                index,
                context.CompiledBindingAccessorEmissionPlan);
            sourceBuilder.AppendLine(
                $"            global::XamlToCSharpGenerator.Runtime.XamlCompiledBindingRegistry.Register(new global::XamlToCSharpGenerator.Runtime.SourceGenCompiledBindingDescriptor(\"{context.EscapedUri}\", \"{CSharpLiteralEmissionService.EscapeStringLiteral(compiledBinding.TargetTypeName)}\", \"{CSharpLiteralEmissionService.EscapeStringLiteral(compiledBinding.TargetPropertyName)}\", \"{CSharpLiteralEmissionService.EscapeStringLiteral(compiledBinding.Path)}\", \"{CSharpLiteralEmissionService.EscapeStringLiteral(compiledBinding.SourceTypeName)}\", {compiledBindingMethodName}));");
        }

        sourceBuilder.AppendLine("        }");
        sourceBuilder.AppendLine();
    }
}
