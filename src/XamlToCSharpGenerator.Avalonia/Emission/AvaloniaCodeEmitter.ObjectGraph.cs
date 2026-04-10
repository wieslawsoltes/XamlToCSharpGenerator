using System.Collections.Immutable;
using System.Text;

namespace XamlToCSharpGenerator.Avalonia.Emission;

public sealed partial class AvaloniaCodeEmitter
{
    private static void EmitObjectGraphMembers(EmitContext context, StringBuilder sourceBuilder)
    {
        var viewModel = context.ViewModel;
        sourceBuilder.AppendLine(
            $"        internal static void __PopulateGeneratedObjectGraph({context.RootTypeName} __root, global::System.IServiceProvider? __serviceProvider = null, bool __replaceExistingCollections = false, bool __rootConstructedWithInitializer = false)");
        sourceBuilder.AppendLine("        {");
        if (viewModel.EmitNameScopeRegistration)
        {
            sourceBuilder.AppendLine("            var __nameScope = new global::Avalonia.Controls.NameScope();");
            sourceBuilder.AppendLine("            var __rootObject = (object)__root;");
            sourceBuilder.AppendLine("            if (__rootObject is global::Avalonia.StyledElement __rootStyledElement)");
            sourceBuilder.AppendLine("            {");
            sourceBuilder.AppendLine("                __AXSGObjectGraph.TrySetNameScope(__rootStyledElement, __nameScope);");
            sourceBuilder.AppendLine("            }");
        }

        var nodeCounter = 0;
        RecursiveObjectGraphEmissionService.EmitNode(
            viewModel.RootObject,
            sourceBuilder,
            ref nodeCounter,
            "            ",
            "__root",
            context.NamedFieldMap,
            context.EmittedEventBindingMethodNames,
            viewModel.EmitNameScopeRegistration,
            context.NameScopeReference,
            TopDownAttachValueToken,
            "__BindingXmlNamespaces",
            existingVariableName: "__root",
            topDownAttachmentTemplate: null,
            completeNameScopeOnNodeCompletion: viewModel.EmitNameScopeRegistration,
            serviceProviderReference: "__serviceProvider",
            baseUriExpression: context.BaseUriExpression,
            parentStackReferences: ImmutableArray<string>.Empty,
            intermediateRootReference: "__root",
            emitDebugLineDirectives: context.EmitDebugLineDirectives,
            lineDirectiveFilePath: context.LineDirectiveFilePath);

        sourceBuilder.AppendLine("        }");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine(
            $"        internal static {context.RootTypeName} __CreateRootInstance(global::System.IServiceProvider? __serviceProvider)");
        sourceBuilder.AppendLine("        {");
        sourceBuilder.AppendLine(
            $"            return {ClrObjectNodeEmissionService.BuildObjectCreationExpression(viewModel.RootObject, "__serviceProvider", context.BaseUriExpression)};");
        sourceBuilder.AppendLine("        }");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine($"        internal static {context.RootTypeName} __BuildGeneratedObjectGraph()");
        sourceBuilder.AppendLine("        {");
        sourceBuilder.AppendLine("            var __root = __CreateRootInstance(null);");
        sourceBuilder.AppendLine("            __PopulateGeneratedObjectGraph(__root, null, __rootConstructedWithInitializer: true);");
        sourceBuilder.AppendLine("            return __root;");
        sourceBuilder.AppendLine("        }");
        sourceBuilder.AppendLine();
    }
}
