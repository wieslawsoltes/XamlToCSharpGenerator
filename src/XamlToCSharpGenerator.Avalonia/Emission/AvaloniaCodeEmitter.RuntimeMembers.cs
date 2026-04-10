using System;
using System.Text;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Avalonia.Emission;

public sealed partial class AvaloniaCodeEmitter
{
    private static void EmitRuntimeMembers(EmitContext context, StringBuilder sourceBuilder)
    {
        EmitHotReloadMembers(context, sourceBuilder);
        ViewModelScaffoldEmissionService.EmitUnsafeAccessorMethods(sourceBuilder, context.ViewModel.UnsafeAccessors);
        ViewModelScaffoldEmissionService.EmitCompiledBindingAccessorMethods(sourceBuilder, context.CompiledBindingAccessorEmissionPlan);
        EmitCompiledBindingDispatcher(context, sourceBuilder);

        if (context.EventBindingDefinitions.Length > 0)
        {
            EventBindingMethodEmissionService.EmitMethods(
                sourceBuilder,
                context.EventBindingDefinitions,
                context.EmittedEventBindingMethodNames);
        }

        EmitGeneratedControlThemeFactory(context, sourceBuilder);

        if (context.ViewModel.EmitStaticResourceResolver)
        {
            sourceBuilder.AppendLine("        private static object? __ResolveStaticResource(object? anchor, object key)");
            sourceBuilder.AppendLine("        {");
            sourceBuilder.AppendLine(
                $"            return global::XamlToCSharpGenerator.Runtime.SourceGenStaticResourceResolver.Resolve(anchor, key, \"{context.EscapedUri}\");");
            sourceBuilder.AppendLine("        }");
            sourceBuilder.AppendLine();
        }

        if (context.ViewModel.Document.IsClassBacked)
        {
            EmitInitializeComponentMembers(context, sourceBuilder);
        }

        if (context.ViewModel.Document.IsClassBacked && context.ViewModel.HasXBind)
        {
            EmitSourceGenXBindBindingsClass(context, sourceBuilder);
        }
    }

    private static void EmitHotReloadMembers(EmitContext context, StringBuilder sourceBuilder)
    {
        var viewModel = context.ViewModel;
        var hotReloadContext = context.HotReloadScaffoldContext;
        if (viewModel.Document.IsClassBacked && (viewModel.EnableHotReload || viewModel.EnableHotDesign))
        {
            sourceBuilder.AppendLine(
                "        private static global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor[] __GetSourceGenHotReloadCollectionCleanupDescriptors()");
            sourceBuilder.AppendLine("        {");
            sourceBuilder.AppendLine(
                $"            return {hotReloadContext.CollectionCleanupDescriptorArrayExpression};");
            sourceBuilder.AppendLine("        }");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine(
                "        private static global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor[] __GetSourceGenHotReloadClrPropertyCleanupDescriptors()");
            sourceBuilder.AppendLine("        {");
            sourceBuilder.AppendLine(
                $"            return {hotReloadContext.ClrPropertyCleanupDescriptorArrayExpression};");
            sourceBuilder.AppendLine("        }");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine(
                "        private static global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor[] __GetSourceGenHotReloadAvaloniaPropertyCleanupDescriptors()");
            sourceBuilder.AppendLine("        {");
            sourceBuilder.AppendLine(
                $"            return {hotReloadContext.FrameworkPropertyCleanupDescriptorArrayExpression};");
            sourceBuilder.AppendLine("        }");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine(
                "        private static global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor[] __GetSourceGenHotReloadRootEventCleanupDescriptors()");
            sourceBuilder.AppendLine("        {");
            sourceBuilder.AppendLine(
                $"            return {hotReloadContext.EventCleanupDescriptorArrayExpression};");
            sourceBuilder.AppendLine("        }");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine("        private static void __TrackAndReconcileSourceGenHotReloadState(object __instance)");
            sourceBuilder.AppendLine("        {");
            sourceBuilder.AppendLine("            global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadStateTracker.Reconcile(");
            sourceBuilder.AppendLine("                __instance,");
            sourceBuilder.AppendLine("                __GetSourceGenHotReloadCollectionCleanupDescriptors(),");
            sourceBuilder.AppendLine("                __GetSourceGenHotReloadClrPropertyCleanupDescriptors(),");
            sourceBuilder.AppendLine("                __GetSourceGenHotReloadAvaloniaPropertyCleanupDescriptors(),");
            sourceBuilder.AppendLine($"                {CSharpLiteralEmissionService.BoolLiteral(hotReloadContext.ClearsRootCollection)},");
            sourceBuilder.AppendLine("                __GetSourceGenHotReloadRootEventCleanupDescriptors());");
            sourceBuilder.AppendLine("        }");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine("        internal void __ApplySourceGenHotReload()");
            sourceBuilder.AppendLine("        {");
            sourceBuilder.AppendLine("            __RegisterXamlSourceGenArtifacts();");
            sourceBuilder.AppendLine("            __TrackAndReconcileSourceGenHotReloadState(this);");
            if (hotReloadContext.HasXBind)
            {
                sourceBuilder.AppendLine("            global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ResetXBind(this);");
            }

            sourceBuilder.AppendLine("            __PopulateGeneratedObjectGraph(this, null, true);");
            sourceBuilder.AppendLine("            var __self = (object)this;");
            sourceBuilder.AppendLine("            if (__self is global::Avalonia.Layout.Layoutable __layoutable)");
            sourceBuilder.AppendLine("            {");
            sourceBuilder.AppendLine("                __layoutable.InvalidateMeasure();");
            sourceBuilder.AppendLine("                __layoutable.InvalidateArrange();");
            sourceBuilder.AppendLine("            }");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine("            if (__self is global::Avalonia.Visual __visual)");
            sourceBuilder.AppendLine("            {");
            sourceBuilder.AppendLine("                __visual.InvalidateVisual();");
            sourceBuilder.AppendLine("            }");
            sourceBuilder.AppendLine("        }");
            sourceBuilder.AppendLine();
        }
        else if (viewModel.EnableHotReload)
        {
            sourceBuilder.AppendLine("        private static void __RegisterSourceGenHotReload(object __instance)");
            sourceBuilder.AppendLine("        {");
            sourceBuilder.AppendLine(
                "            global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager.Register(__instance, __ApplySourceGenHotReload, new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadRegistrationOptions");
            sourceBuilder.AppendLine("            {");
            sourceBuilder.AppendLine($"                TrackingType = typeof({context.ClassName}),");
            sourceBuilder.AppendLine($"                BuildUri = \"{hotReloadContext.EscapedUri}\",");
            sourceBuilder.AppendLine($"                SourcePath = \"{hotReloadContext.EscapedSourcePath}\"");
            sourceBuilder.AppendLine("            });");
            sourceBuilder.AppendLine("        }");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine("        private static void __ApplySourceGenHotReload(object __instance)");
            sourceBuilder.AppendLine("        {");
            sourceBuilder.AppendLine("            __RegisterXamlSourceGenArtifacts();");
            sourceBuilder.AppendLine($"            __PopulateGeneratedObjectGraph(({context.RootTypeName})__instance, null, true);");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine("            if (__instance is global::Avalonia.Layout.Layoutable __layoutable)");
            sourceBuilder.AppendLine("            {");
            sourceBuilder.AppendLine("                __layoutable.InvalidateMeasure();");
            sourceBuilder.AppendLine("                __layoutable.InvalidateArrange();");
            sourceBuilder.AppendLine("            }");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine("            if (__instance is global::Avalonia.Visual __visual)");
            sourceBuilder.AppendLine("            {");
            sourceBuilder.AppendLine("                __visual.InvalidateVisual();");
            sourceBuilder.AppendLine("            }");
            sourceBuilder.AppendLine("        }");
            sourceBuilder.AppendLine();
        }
    }

    private static void EmitCompiledBindingDispatcher(EmitContext context, StringBuilder sourceBuilder)
    {
        sourceBuilder.AppendLine("        private static object? __CompiledBindingAccessor(int __index, object __source)");
        sourceBuilder.AppendLine("        {");
        sourceBuilder.AppendLine("            switch (__index)");
        sourceBuilder.AppendLine("            {");
        for (var index = 0; index < context.ViewModel.CompiledBindings.Length; index++)
        {
            var compiledBinding = context.ViewModel.CompiledBindings[index];
            var accessorExpression = CompiledBindingEmissionService.RewriteSourceReceiver(
                compiledBinding.AccessorExpression,
                "__source",
                "source");
            sourceBuilder.AppendLine($"                case {index}:");
            sourceBuilder.AppendLine("                {");
            sourceBuilder.AppendLine($"                    var source = ({compiledBinding.SourceTypeName})__source;");
            sourceBuilder.AppendLine($"                    return {accessorExpression};");
            sourceBuilder.AppendLine("                }");
        }

        sourceBuilder.AppendLine("                default:");
        sourceBuilder.AppendLine("                    return null;");
        sourceBuilder.AppendLine("            }");
        sourceBuilder.AppendLine("        }");
        sourceBuilder.AppendLine();
    }

    private static void EmitGeneratedControlThemeFactory(EmitContext context, StringBuilder sourceBuilder)
    {
        sourceBuilder.AppendLine("        private static global::Avalonia.Styling.ControlTheme __BuildGeneratedControlTheme(int __index)");
        sourceBuilder.AppendLine("        {");
        sourceBuilder.AppendLine("            switch (__index)");
        sourceBuilder.AppendLine("            {");
        for (var themeIndex = 0; themeIndex < context.ViewModel.ControlThemes.Length; themeIndex++)
        {
            var controlTheme = context.ViewModel.ControlThemes[themeIndex];
            sourceBuilder.AppendLine($"                case {themeIndex}:");
            sourceBuilder.AppendLine("                {");
            sourceBuilder.AppendLine("                    var __theme = new global::Avalonia.Styling.ControlTheme();");

            if (!string.IsNullOrWhiteSpace(controlTheme.TargetTypeName))
            {
                sourceBuilder.AppendLine(
                    $"                    __theme.TargetType = typeof({controlTheme.TargetTypeName});");
            }

            for (var setterIndex = 0; setterIndex < controlTheme.Setters.Length; setterIndex++)
            {
                var setter = controlTheme.Setters[setterIndex];
                var propertyExpression = ValueOperationEmissionService.BuildFrameworkPropertyExpression(setter);
                if (string.IsNullOrWhiteSpace(propertyExpression))
                {
                    continue;
                }

                var expandedValueExpression = ClrObjectNodeEmissionService.ExpandMarkupContextExpression(
                    setter.ValueExpression,
                    "null",
                    "__theme",
                    "__theme",
                    "__theme",
                    propertyExpression,
                    context.BaseUriExpression,
                    "new object[] { __theme }");
                var emittedValueExpression = ShouldAttachBindingMetadata(setter)
                    ? ValueOperationEmissionService.BuildBindingMetadataAttachmentExpression(
                        expandedValueExpression,
                        nameScopeReference: null,
                        xmlNamespacesReference: "__BindingXmlNamespaces")
                    : expandedValueExpression;

                sourceBuilder.AppendLine(
                    $"                    __theme.Setters.Add(new global::Avalonia.Styling.Setter({propertyExpression}, {emittedValueExpression}));");
            }

            sourceBuilder.AppendLine("                    return __theme;");
            sourceBuilder.AppendLine("                }");
        }

        sourceBuilder.AppendLine("                default:");
        sourceBuilder.AppendLine("                    return new global::Avalonia.Styling.ControlTheme();");
        sourceBuilder.AppendLine("            }");
        sourceBuilder.AppendLine("        }");
        sourceBuilder.AppendLine();
    }

    private static bool ShouldAttachBindingMetadata(ResolvedSetterDefinition setter)
    {
        return setter.ValueKind is ResolvedValueKind.Binding or
               ResolvedValueKind.TemplateBinding or
               ResolvedValueKind.DynamicResourceBinding;
    }

    private static void EmitInitializeComponentMembers(EmitContext context, StringBuilder sourceBuilder)
    {
        sourceBuilder.AppendLine("        public void InitializeComponent(bool loadXaml = true)");
        sourceBuilder.AppendLine("        {");
        sourceBuilder.AppendLine("            __InitializeXamlSourceGenComponent(this, null, loadXaml);");
        sourceBuilder.AppendLine("        }");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine(
            $"        internal static void __InitializeXamlSourceGenComponent({context.RootTypeName} __self)");
        sourceBuilder.AppendLine("        {");
        sourceBuilder.AppendLine("            __InitializeXamlSourceGenComponent(__self, null, true);");
        sourceBuilder.AppendLine("        }");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine(
            $"        internal static void __InitializeXamlSourceGenComponent(global::System.IServiceProvider? __serviceProvider, {context.RootTypeName} __self)");
        sourceBuilder.AppendLine("        {");
        sourceBuilder.AppendLine("            __InitializeXamlSourceGenComponent(__self, __serviceProvider, true);");
        sourceBuilder.AppendLine("        }");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine(
            $"        private static void __InitializeXamlSourceGenComponent({context.RootTypeName} __self, global::System.IServiceProvider? __serviceProvider, bool loadXaml)");
        sourceBuilder.AppendLine("        {");
        EmitInitializeComponentBody(context, sourceBuilder, "__self", "__serviceProvider");
        sourceBuilder.AppendLine("        }");
    }

    private static void EmitInitializeComponentBody(
        EmitContext context,
        StringBuilder sourceBuilder,
        string selfExpression,
        string serviceProviderExpression)
    {
        var viewModel = context.ViewModel;
        sourceBuilder.AppendLine("            var __loadedWithSourceGen = false;");
        sourceBuilder.AppendLine("            if (loadXaml)");
        sourceBuilder.AppendLine("            {");
        if (viewModel.HasXBind)
        {
            sourceBuilder.AppendLine(
                $"                global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ResetXBind({selfExpression});");
        }

        sourceBuilder.AppendLine(
            $"                __PopulateGeneratedObjectGraph({selfExpression}, {serviceProviderExpression});");
        sourceBuilder.AppendLine("                __loadedWithSourceGen = true;");
        sourceBuilder.AppendLine("            }");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("            if (!__loadedWithSourceGen)");
        sourceBuilder.AppendLine("            {");
        foreach (var namedElement in viewModel.NamedElements)
        {
            sourceBuilder.AppendLine(
                $"                {selfExpression}.{IdentifierSanitizationService.SanitizeIdentifier(namedElement.Name)} = ({namedElement.TypeName})global::XamlToCSharpGenerator.Runtime.SourceGenNameReferenceHelper.ResolveByName({selfExpression}, \"{CSharpLiteralEmissionService.EscapeStringLiteral(namedElement.Name)}\")!;");
        }

        sourceBuilder.AppendLine("            }");
        sourceBuilder.AppendLine();

        if (viewModel.EnableHotReload || viewModel.EnableHotDesign)
        {
            sourceBuilder.AppendLine("            if (__loadedWithSourceGen)");
            sourceBuilder.AppendLine("            {");
            sourceBuilder.AppendLine(
                $"                __TrackAndReconcileSourceGenHotReloadState({selfExpression});");

            if (viewModel.EnableHotReload)
            {
                sourceBuilder.AppendLine(
                    $"                global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager.Register({selfExpression}, static __instance => (({context.RootTypeName})__instance).__ApplySourceGenHotReload(), new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadRegistrationOptions");
                sourceBuilder.AppendLine("                {");
                sourceBuilder.AppendLine($"                    BuildUri = \"{context.HotReloadScaffoldContext.EscapedUri}\",");
                sourceBuilder.AppendLine($"                    SourcePath = \"{context.HotReloadScaffoldContext.EscapedSourcePath}\",");
                sourceBuilder.AppendLine("                    CaptureState = static __instance =>");
                sourceBuilder.AppendLine("                        __instance is global::Avalonia.StyledElement __styledElement");
                sourceBuilder.AppendLine("                            ? __styledElement.DataContext");
                sourceBuilder.AppendLine("                            : null,");
                sourceBuilder.AppendLine("                    RestoreState = static (__instance, __state) =>");
                sourceBuilder.AppendLine("                    {");
                sourceBuilder.AppendLine("                        if (__instance is global::Avalonia.StyledElement __styledElement &&");
                sourceBuilder.AppendLine("                            __styledElement.DataContext is null)");
                sourceBuilder.AppendLine("                        {");
                sourceBuilder.AppendLine("                            __styledElement.DataContext = __state;");
                sourceBuilder.AppendLine("                        }");
                sourceBuilder.AppendLine("                    }");
                sourceBuilder.AppendLine("                });");
            }

            if (viewModel.EnableHotDesign)
            {
                sourceBuilder.AppendLine(
                    $"                global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotDesignManager.Register({selfExpression}, static __instance => (({context.RootTypeName})__instance).__ApplySourceGenHotReload(), new global::XamlToCSharpGenerator.Runtime.SourceGenHotDesignRegistrationOptions");
                sourceBuilder.AppendLine("                {");
                sourceBuilder.AppendLine($"                    BuildUri = \"{context.HotReloadScaffoldContext.EscapedUri}\",");
                sourceBuilder.AppendLine($"                    SourcePath = \"{context.HotReloadScaffoldContext.EscapedSourcePath}\",");
                sourceBuilder.AppendLine($"                    DocumentRole = {context.HotReloadScaffoldContext.HotDesignDocumentRoleExpression},");
                sourceBuilder.AppendLine($"                    ArtifactKind = {context.HotReloadScaffoldContext.HotDesignArtifactKindExpression},");
                sourceBuilder.AppendLine($"                    ScopeHints = {context.HotReloadScaffoldContext.HotDesignScopeHintsExpression}");
                sourceBuilder.AppendLine("                });");
            }

            sourceBuilder.AppendLine("            }");
        }
    }

    private static void EmitSourceGenXBindBindingsClass(EmitContext context, StringBuilder sourceBuilder)
    {
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("        public sealed class __SourceGenXBindBindings");
        sourceBuilder.AppendLine("        {");
        sourceBuilder.AppendLine($"            private readonly {context.RootTypeName} __owner;");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine($"            internal __SourceGenXBindBindings({context.RootTypeName} owner)");
        sourceBuilder.AppendLine("            {");
        sourceBuilder.AppendLine("                __owner = owner;");
        sourceBuilder.AppendLine("            }");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("            public void Initialize()");
        sourceBuilder.AppendLine("            {");
        sourceBuilder.AppendLine("                global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.InitializeXBind(__owner);");
        sourceBuilder.AppendLine("            }");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("            public void Update()");
        sourceBuilder.AppendLine("            {");
        sourceBuilder.AppendLine("                global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.UpdateXBind(__owner);");
        sourceBuilder.AppendLine("            }");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("            public void StopTracking()");
        sourceBuilder.AppendLine("            {");
        sourceBuilder.AppendLine("                global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.StopTrackingXBind(__owner);");
        sourceBuilder.AppendLine("            }");
        sourceBuilder.AppendLine("        }");
    }
}
