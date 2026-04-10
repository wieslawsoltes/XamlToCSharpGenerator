using System.Collections.Generic;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Emission;

namespace XamlToCSharpGenerator.Avalonia.Emission;

public sealed partial class AvaloniaCodeEmitter
{
    private sealed record EmitContext(
        ResolvedViewModel ViewModel,
        string NamespaceName,
        string ClassName,
        string EscapedUri,
        bool EmitDebugLineDirectives,
        string LineDirectiveFilePath,
        IReadOnlyDictionary<string, string> NamedFieldMap,
        ImmutableArray<string> KnownTypeNames,
        string BindingXmlNamespaceMapExpression,
        ImmutableArray<ResolvedEventBindingDefinition> EventBindingDefinitions,
        IReadOnlyDictionary<string, string> EmittedEventBindingMethodNames,
        CompiledBindingAccessorEmissionPlan CompiledBindingAccessorEmissionPlan,
        FrameworkHotReloadScaffoldContext HotReloadScaffoldContext)
    {
        public string RootTypeName => ViewModel.RootObject.TypeName;

        public string BaseUriExpression => "__SourceGenDocumentUri";

        public string? NameScopeReference => ViewModel.EmitNameScopeRegistration ? "__nameScope" : null;
    }

    private static EmitContext CreateEmitContext(ResolvedViewModel viewModel)
    {
        var document = viewModel.Document;
        var escapedUri = CSharpLiteralEmissionService.EscapeStringLiteral(viewModel.BuildUri);
        var lineDirectiveFilePath = SourceMappedLineEmissionService.NormalizeLineDirectivePath(document.FilePath);
        var namedFieldMap = EmissionMetadataService.BuildNamedFieldMap(viewModel, IdentifierSanitizationService.SanitizeIdentifier);
        var knownTypeNames = EmissionMetadataService.CollectKnownTypeNames(viewModel);
        var bindingXmlNamespaceMapExpression = EmissionMetadataService.BuildBindingXmlNamespaceMapExpression(document.XmlNamespaces);
        var emittedEventBindingMethodNames = document.IsClassBacked
            ? EventBindingEmissionService.BuildStableMethodNameMap(EventBindingEmissionService.CollectDefinitions(viewModel.RootObject))
            : new Dictionary<string, string>();
        var eventBindingDefinitions = document.IsClassBacked
            ? EventBindingEmissionService.CollectDefinitions(viewModel.RootObject)
            : ImmutableArray<ResolvedEventBindingDefinition>.Empty;
        var hotReloadScaffoldContext = CreateHotReloadScaffoldContext(
            viewModel,
            document.ClassName,
            escapedUri,
            CSharpLiteralEmissionService.EscapeStringLiteral(document.FilePath),
            namedFieldMap,
            emittedEventBindingMethodNames);

        return new EmitContext(
            viewModel,
            document.ClassNamespace,
            document.ClassName,
            escapedUri,
            viewModel.CreateSourceInfo,
            lineDirectiveFilePath,
            namedFieldMap,
            knownTypeNames,
            bindingXmlNamespaceMapExpression,
            eventBindingDefinitions,
            emittedEventBindingMethodNames,
            CompiledBindingEmissionService.BuildEmissionPlan(viewModel.CompiledBindings),
            hotReloadScaffoldContext);
    }

    private static FrameworkHotReloadScaffoldContext CreateHotReloadScaffoldContext(
        ResolvedViewModel viewModel,
        string className,
        string escapedUri,
        string escapedSourcePath,
        IReadOnlyDictionary<string, string> namedFieldMap,
        IReadOnlyDictionary<string, string> emittedEventBindingMethodNames)
    {
        var hotReloadCollectionMembers = ImmutableArray<string>.Empty;
        var hotReloadClrPropertyMembers = ImmutableArray<string>.Empty;
        var hotReloadFrameworkPropertyCleanupPlans = ImmutableArray<FrameworkHotReloadPropertyCleanupPlan>.Empty;
        var hotReloadRootEventSubscriptions = ImmutableArray<ResolvedEventSubscription>.Empty;
        var hotReloadClearsRootCollection = false;

        if (viewModel.Document.IsClassBacked && (viewModel.EnableHotReload || viewModel.EnableHotDesign))
        {
            hotReloadCollectionMembers = HotReloadRuntimeEmissionService.BuildRootHotReloadCollectionMembers(viewModel.RootObject);
            hotReloadClrPropertyMembers = HotReloadRuntimeEmissionService.BuildRootHotReloadClrPropertyMembers(viewModel.RootObject, namedFieldMap);
            hotReloadFrameworkPropertyCleanupPlans = HotReloadRuntimeEmissionService.BuildRootHotReloadFrameworkPropertyCleanupPlans(viewModel.RootObject);
            hotReloadRootEventSubscriptions = HotReloadRuntimeEmissionService.BuildRootHotReloadEventSubscriptions(viewModel.RootObject);
            hotReloadClearsRootCollection = HotReloadRuntimeEmissionService.ShouldClearRootSelfCollection(viewModel.RootObject);
        }

        return new FrameworkHotReloadScaffoldContext(
            RootTypeName: viewModel.RootObject.TypeName,
            ClassName: className,
            EscapedUri: escapedUri,
            EscapedSourcePath: escapedSourcePath,
            CollectionCleanupDescriptorArrayExpression: HotReloadRuntimeEmissionService.BuildCollectionCleanupDescriptorArrayExpression(
                hotReloadCollectionMembers,
                viewModel.RootObject.TypeName),
            ClrPropertyCleanupDescriptorArrayExpression: HotReloadRuntimeEmissionService.BuildClrPropertyCleanupDescriptorArrayExpression(
                hotReloadClrPropertyMembers,
                viewModel.RootObject.TypeName),
            FrameworkPropertyCleanupDescriptorArrayExpression: HotReloadRuntimeEmissionService.BuildFrameworkPropertyCleanupDescriptorArrayExpression(
                hotReloadFrameworkPropertyCleanupPlans),
            EventCleanupDescriptorArrayExpression: HotReloadRuntimeEmissionService.BuildEventCleanupDescriptorArrayExpression(
                hotReloadRootEventSubscriptions,
                viewModel.RootObject.TypeName,
                emittedEventBindingMethodNames),
            ClearsRootCollection: hotReloadClearsRootCollection,
            HasXBind: viewModel.HasXBind,
            EnableHotReload: viewModel.EnableHotReload,
            EnableHotDesign: viewModel.EnableHotDesign,
            HotDesignDocumentRoleExpression: ViewModelScaffoldEmissionService.BuildHotDesignDocumentRoleExpression(viewModel),
            HotDesignArtifactKindExpression: ViewModelScaffoldEmissionService.BuildHotDesignArtifactKindExpression(viewModel),
            HotDesignScopeHintsExpression: ViewModelScaffoldEmissionService.BuildHotDesignScopeHintsExpression(viewModel));
    }
}
