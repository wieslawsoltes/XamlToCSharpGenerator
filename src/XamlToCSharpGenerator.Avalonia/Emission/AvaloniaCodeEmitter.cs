using System;
using System.Collections.Immutable;
using System.Text;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;
using XamlToCSharpGenerator.Framework.Shared.Emission;

namespace XamlToCSharpGenerator.Avalonia.Emission;

public sealed partial class AvaloniaCodeEmitter : IXamlCodeEmitter
{
    private const string TopDownAttachValueToken = "__AXSG_VALUE__";
    private const string MarkupContextServiceProviderToken = "__AXSG_CTX_SERVICE_PROVIDER__";
    private const string MarkupContextRootObjectToken = "__AXSG_CTX_ROOT_OBJECT__";
    private const string MarkupContextIntermediateRootObjectToken = "__AXSG_CTX_INTERMEDIATE_ROOT_OBJECT__";
    private const string MarkupContextTargetObjectToken = "__AXSG_CTX_TARGET_OBJECT__";
    private const string MarkupContextTargetPropertyToken = "__AXSG_CTX_TARGET_PROPERTY__";
    private const string MarkupContextBaseUriToken = "__AXSG_CTX_BASE_URI__";
    private const string MarkupContextParentStackToken = "__AXSG_CTX_PARENT_STACK__";

    private static readonly CSharpLiteralEmissionService CSharpLiteralEmissionService = new();
    private static readonly IdentifierSanitizationService IdentifierSanitizationService = new();
    private static readonly CompiledBindingEmissionService CompiledBindingEmissionService = new();
    private static readonly ResolvedViewModelEmissionMetadataService EmissionMetadataService = new(
        ImmutableArray.Create("global::Avalonia.Data.Binding", "global::Avalonia.Markup.Xaml.MarkupExtensions.ReflectionBindingExtension"),
        ImmutableArray.Create("global::Avalonia.Data.RelativeSource"),
        CSharpLiteralEmissionService.EscapeStringLiteral);
    private static readonly EventBindingEmissionService EventBindingEmissionService = new();
    private static readonly FrameworkEventBindingMethodEmissionService EventBindingMethodEmissionService = new(
        AvaloniaFrameworkEventBindingEmitterAdapter.Instance,
        EventBindingEmissionService);
    private static readonly GeneratedSourceHintNameService GeneratedSourceHintNameService = new();
    private static readonly ViewModelScaffoldEmissionService ViewModelScaffoldEmissionService = new(
        GeneratedSourceHintNameService,
        CSharpLiteralEmissionService.EscapeStringLiteral);
    private static readonly FrameworkValueOperationEmissionService ValueOperationEmissionService = new(
        AvaloniaFrameworkValueOperationEmitterAdapter.Instance,
        CSharpLiteralEmissionService.EscapeStringLiteral,
        HotReloadRuntimeEmissionService.ExtractMemberName,
        HotReloadRuntimeEmissionService.IsValidIdentifierForGeneratedMemberAccess);
    private static readonly SourceMappedLineEmissionService SourceMappedLineEmissionService = new();
    private static readonly ParentStackEmissionService ParentStackEmissionService = new();
    private static readonly ObjectNodeLifecycleEmissionService ObjectNodeLifecycleEmissionService = new(
        AvaloniaFrameworkObjectNodeLifecycleEmitterAdapter.Instance,
        SourceMappedLineEmissionService);
    private static readonly DeferredDictionaryEmissionService DeferredDictionaryEmissionService = new(
        AvaloniaFrameworkDeferredDictionaryEmitterAdapter.Instance,
        SourceMappedLineEmissionService,
        ParentStackEmissionService);
    private static readonly CollectionAttachmentEmissionService CollectionAttachmentEmissionService = new(
        AvaloniaFrameworkCollectionAttachmentEmitterAdapter.Instance,
        AvaloniaFrameworkDeferredDictionaryEmitterAdapter.Instance,
        DeferredDictionaryEmissionService,
        SourceMappedLineEmissionService,
        ParentStackEmissionService);
    private static readonly DeferredTemplateScaffoldEmissionService DeferredTemplateScaffoldEmissionService = new(
        AvaloniaFrameworkDeferredTemplateEmitterAdapter.Instance);
    private static readonly HotReloadRuntimeEmissionService HotReloadRuntimeEmissionService = new(
        ValueOperationEmissionService,
        EventBindingEmissionService,
        AvaloniaFrameworkHotReloadEmitterAdapter.Instance,
        CSharpLiteralEmissionService.EscapeStringLiteral);
    private static readonly InitializeComponentBodyEmissionService InitializeComponentBodyEmissionService = new(
        HotReloadRuntimeEmissionService.EmitInitializeComponentHotReloadRegistrations,
        IdentifierSanitizationService.SanitizeIdentifier,
        CSharpLiteralEmissionService.EscapeStringLiteral);
    private static readonly SourceInfoRegistrationEmissionService SourceInfoRegistrationEmissionService = new(CSharpLiteralEmissionService.EscapeStringLiteral);
    private static readonly ClrObjectNodeEmissionService ClrObjectNodeEmissionService = new(
        CSharpLiteralEmissionService.EscapeStringLiteral,
        ValueOperationEmissionService.HasFrameworkPropertyOperation,
        new MarkupContextTokenSet(
            MarkupContextServiceProviderToken,
            MarkupContextRootObjectToken,
            MarkupContextIntermediateRootObjectToken,
            MarkupContextTargetObjectToken,
            MarkupContextTargetPropertyToken,
            MarkupContextBaseUriToken,
            MarkupContextParentStackToken));
    private static readonly AttachedNodeValueEmissionService AttachedNodeValueEmissionService = new();
    private static readonly ContentChildAttachmentEmissionService ContentChildAttachmentEmissionService = new(
        ClrObjectNodeEmissionService,
        SourceMappedLineEmissionService,
        ParentStackEmissionService);
    private static readonly ObjectNodeMemberEmissionService ObjectNodeMemberEmissionService = new(
        ValueOperationEmissionService,
        ClrObjectNodeEmissionService,
        CollectionAttachmentEmissionService,
        SourceMappedLineEmissionService,
        ParentStackEmissionService);
    private static readonly ObjectNodeEventSubscriptionEmissionService ObjectNodeEventSubscriptionEmissionService = new(
        AvaloniaFrameworkEventSubscriptionEmitterAdapter.Instance,
        EventBindingEmissionService,
        SourceMappedLineEmissionService);
    private static readonly ObjectNodeBodyEmissionService ObjectNodeBodyEmissionService = new(
        ObjectNodeLifecycleEmissionService,
        ObjectNodeMemberEmissionService,
        ObjectNodeEventSubscriptionEmissionService,
        CollectionAttachmentEmissionService,
        ContentChildAttachmentEmissionService,
        DeferredTemplateScaffoldEmissionService);
    private static readonly RecursiveObjectGraphEmissionService RecursiveObjectGraphEmissionService = new(
        ParentStackEmissionService,
        ObjectNodeBodyEmissionService.EmitNode,
        ClrObjectNodeEmissionService.BuildObjectCreationExpression,
        AttachedNodeValueEmissionService.BuildAttachedNodeValueExpression);

    public (string HintName, string Source) Emit(ResolvedViewModel viewModel)
    {
        var context = CreateEmitContext(viewModel);
        var sourceBuilder = new StringBuilder(ViewModelScaffoldEmissionService.EstimateSourceCapacity(viewModel));

        EmitPreamble(context, sourceBuilder);
        EmitTypeOpening(context, sourceBuilder);
        EmitArtifactRegistrationMembers(context, sourceBuilder);
        EmitObjectGraphMembers(context, sourceBuilder);
        EmitRuntimeMembers(context, sourceBuilder);

        sourceBuilder.AppendLine("    }");
        sourceBuilder.AppendLine("}");

        var source = sourceBuilder.ToString();
        source = CompiledBindingEmissionService.RewriteAccessorPlaceholders(source, context.CompiledBindingAccessorEmissionPlan);
        return (ViewModelScaffoldEmissionService.BuildHintName(viewModel), source);
    }
}
