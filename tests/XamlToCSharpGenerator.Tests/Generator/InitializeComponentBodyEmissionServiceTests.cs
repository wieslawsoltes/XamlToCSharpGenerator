using System.Collections.Immutable;
using System.Text;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Emission;

namespace XamlToCSharpGenerator.Tests.Generator;

public class InitializeComponentBodyEmissionServiceTests
{
    [Fact]
    public void Emit_Resets_XBind_Resolves_Named_Elements_And_Emits_Hot_Reload_Registration()
    {
        var hotReloadCalled = false;
        var service = new InitializeComponentBodyEmissionService(
            (sourceBuilder, _, selfExpression) =>
            {
                hotReloadCalled = true;
                sourceBuilder.AppendLine($"            // hot reload for {selfExpression}");
            },
            static identifier => "Sanitized_" + identifier,
            static value => value);
        var sourceBuilder = new StringBuilder();

        service.Emit(
            sourceBuilder,
            CreateViewModel(hasXBind: true, enableHotReload: true, enableHotDesign: false),
            "__self",
            "__serviceProvider",
            CreateHotReloadScaffoldContext());

        var source = sourceBuilder.ToString();
        Assert.True(hotReloadCalled);
        Assert.Contains("ResetXBind(__self);", source);
        Assert.Contains("__PopulateGeneratedObjectGraph(__self, __serviceProvider);", source);
        Assert.Contains("__self.Sanitized_Root = (global::Demo.RootView)global::XamlToCSharpGenerator.Runtime.SourceGenNameReferenceHelper.ResolveByName(__self, \"Root\")!;", source);
        Assert.Contains("// hot reload for __self", source);
    }

    [Fact]
    public void Emit_Skips_Optional_Branches_When_Not_Enabled()
    {
        var hotReloadCalled = false;
        var service = new InitializeComponentBodyEmissionService(
            (_, _, _) => hotReloadCalled = true,
            static identifier => identifier,
            static value => value);
        var sourceBuilder = new StringBuilder();

        service.Emit(
            sourceBuilder,
            CreateViewModel(hasXBind: false, enableHotReload: false, enableHotDesign: false),
            "__self",
            "__serviceProvider",
            CreateHotReloadScaffoldContext());

        var source = sourceBuilder.ToString();
        Assert.False(hotReloadCalled);
        Assert.DoesNotContain("ResetXBind(", source);
        Assert.DoesNotContain("SourceGenNameReferenceHelper.ResolveByName(", source);
    }

    private static ResolvedViewModel CreateViewModel(bool hasXBind, bool enableHotReload, bool enableHotDesign)
    {
        return new ResolvedViewModel(
            Document: CreateDocument(),
            BuildUri: "avares://Demo/View.axaml",
            ClassModifier: "public",
            CreateSourceInfo: false,
            EnableHotReload: enableHotReload,
            EnableHotDesign: enableHotDesign,
            PassExecutionTrace: ImmutableArray<string>.Empty,
            EmitNameScopeRegistration: true,
            EmitStaticResourceResolver: false,
            HasXBind: hasXBind,
            RootObject: CreateResolvedObjectNode(),
            NamedElements: hasXBind
                ? ImmutableArray.Create(new ResolvedNamedElement("Root", "global::Demo.RootView", "private", 1, 1))
                : ImmutableArray<ResolvedNamedElement>.Empty,
            Resources: ImmutableArray<ResolvedResourceDefinition>.Empty,
            Templates: ImmutableArray<ResolvedTemplateDefinition>.Empty,
            CompiledBindings: ImmutableArray<ResolvedCompiledBindingDefinition>.Empty,
            UnsafeAccessors: ImmutableArray<ResolvedUnsafeAccessorDefinition>.Empty,
            Styles: ImmutableArray<ResolvedStyleDefinition>.Empty,
            ControlThemes: ImmutableArray<ResolvedControlThemeDefinition>.Empty,
            Includes: ImmutableArray<ResolvedIncludeDefinition>.Empty,
            HotDesignArtifactKind: ResolvedHotDesignArtifactKind.View,
            HotDesignScopeHints: ImmutableArray<string>.Empty);
    }

    private static FrameworkHotReloadScaffoldContext CreateHotReloadScaffoldContext()
    {
        return new FrameworkHotReloadScaffoldContext(
            RootTypeName: "global::Demo.RootView",
            ClassName: "RootView",
            EscapedUri: "avares://Demo/View.axaml",
            EscapedSourcePath: "/tests/View.axaml",
            CollectionCleanupDescriptorArrayExpression: "[]",
            ClrPropertyCleanupDescriptorArrayExpression: "[]",
            FrameworkPropertyCleanupDescriptorArrayExpression: "[]",
            EventCleanupDescriptorArrayExpression: "[]",
            ClearsRootCollection: false,
            HasXBind: true,
            EnableHotReload: true,
            EnableHotDesign: false,
            HotDesignDocumentRoleExpression: "global::XamlToCSharpGenerator.Runtime.HotDesignDocumentRole.View",
            HotDesignArtifactKindExpression: "global::XamlToCSharpGenerator.Runtime.HotDesignArtifactKind.View",
            HotDesignScopeHintsExpression: "global::System.Array.Empty<string>()");
    }

    private static XamlDocumentModel CreateDocument()
    {
        return new XamlDocumentModel(
            FilePath: "/tests/View.axaml",
            TargetPath: "View.axaml",
            ClassFullName: "Demo.RootView",
            ClassModifier: "public partial",
            Precompile: true,
            XmlNamespaces: ImmutableDictionary<string, string>.Empty,
            RootObject: CreateObjectNode(),
            NamedElements: ImmutableArray<XamlNamedElement>.Empty,
            Resources: ImmutableArray<XamlResourceDefinition>.Empty,
            Templates: ImmutableArray<XamlTemplateDefinition>.Empty,
            Styles: ImmutableArray<XamlStyleDefinition>.Empty,
            ControlThemes: ImmutableArray<XamlControlThemeDefinition>.Empty,
            Includes: ImmutableArray<XamlIncludeDefinition>.Empty,
            IsValid: true);
    }

    private static XamlObjectNode CreateObjectNode()
    {
        return new XamlObjectNode(
            "clr-namespace:Demo",
            "RootView",
            Key: null,
            Name: "Root",
            FieldModifier: null,
            DataType: null,
            CompileBindings: true,
            FactoryMethod: null,
            TypeArguments: ImmutableArray<string>.Empty,
            ArrayItemType: null,
            ConstructorArguments: ImmutableArray<XamlObjectNode>.Empty,
            TextContent: null,
            PropertyAssignments: ImmutableArray<XamlPropertyAssignment>.Empty,
            ChildObjects: ImmutableArray<XamlObjectNode>.Empty,
            PropertyElements: ImmutableArray<XamlPropertyElement>.Empty,
            Line: 1,
            Column: 1);
    }

    private static ResolvedObjectNode CreateResolvedObjectNode()
    {
        return new ResolvedObjectNode(
            KeyExpression: null,
            Name: "Root",
            TypeName: "global::Demo.RootView",
            IsBindingObjectNode: false,
            FactoryExpression: "new global::Demo.RootView()",
            FactoryValueRequirements: ResolvedValueRequirements.None,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: ImmutableArray<ResolvedObjectNode>.Empty,
            ChildAttachmentMode: ResolvedChildAttachmentMode.None,
            ContentPropertyName: null,
            Line: 1,
            Column: 1);
    }
}
