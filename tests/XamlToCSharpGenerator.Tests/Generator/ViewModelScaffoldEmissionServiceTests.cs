using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Emission;

namespace XamlToCSharpGenerator.Tests.Generator;

public class ViewModelScaffoldEmissionServiceTests
{
    private static readonly GeneratedSourceHintNameService GeneratedSourceHintNameService = new();

    [Fact]
    public void BuildHintName_And_HotDesign_Expressions_Are_Deterministic()
    {
        var service = new ViewModelScaffoldEmissionService(GeneratedSourceHintNameService, Escape);
        var viewModel = CreateViewModel(
            buildUri: "avares://Demo/App.xaml",
            hotDesignArtifactKind: ResolvedHotDesignArtifactKind.ResourceDictionary,
            hotDesignScopeHints: ImmutableArray.Create("RootScope", "NestedScope"));

        var hintName = service.BuildHintName(viewModel);
        var documentRoleExpression = service.BuildHotDesignDocumentRoleExpression(viewModel);
        var artifactKindExpression = service.BuildHotDesignArtifactKindExpression(viewModel);
        var scopeHintsExpression = service.BuildHotDesignScopeHintsExpression(viewModel);

        Assert.Equal("Demo.App.2750afec.XamlSourceGen.g.cs", hintName);
        Assert.Equal("global::XamlToCSharpGenerator.Runtime.SourceGenHotDesignDocumentRole.Resources", documentRoleExpression);
        Assert.Equal("global::XamlToCSharpGenerator.Runtime.SourceGenHotDesignArtifactKind.ResourceDictionary", artifactKindExpression);
        Assert.Equal("new string[] { \"RootScope\", \"NestedScope\" }", scopeHintsExpression);
    }

    [Fact]
    public void EmitCompiledBindingAccessorMethods_Emits_Source_And_Object_Overloads()
    {
        var service = new ViewModelScaffoldEmissionService(GeneratedSourceHintNameService, Escape);
        var builder = new StringBuilder();
        var emissionPlan = new CompiledBindingAccessorEmissionPlan(
            ImmutableArray.Create(
                new CompiledBindingAccessorEmissionMethod(
                    3,
                    "__Accessor_Source",
                    "__Binding_Source",
                    "__Accessor_Object",
                    "global::Demo.ViewModels.MainViewModel",
                    "global::Demo.ViewModels.MainViewModel|Title")),
            new Dictionary<int, string>(),
            new Dictionary<string, string>());

        service.EmitCompiledBindingAccessorMethods(builder, emissionPlan);

        var emitted = builder.ToString();
        Assert.Contains("private static object? __Accessor_Source(global::Demo.ViewModels.MainViewModel source)", emitted, StringComparison.Ordinal);
        Assert.Contains("return __CompiledBindingAccessor(3, source);", emitted, StringComparison.Ordinal);
        Assert.Contains("private static object? __Binding_Source(global::Demo.ViewModels.MainViewModel source)", emitted, StringComparison.Ordinal);
        Assert.Contains("return __Accessor_Source(source);", emitted, StringComparison.Ordinal);
        Assert.Contains("private static object? __Accessor_Object(object source)", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitUnsafeAccessorMethods_Deduplicates_By_Method_Name()
    {
        var service = new ViewModelScaffoldEmissionService(GeneratedSourceHintNameService, Escape);
        var builder = new StringBuilder();
        var unsafeAccessors = ImmutableArray.Create(
            new ResolvedUnsafeAccessorDefinition(
                "__AXSG_UnsafeAccessor_A",
                "Set_Value",
                "global::Demo.Owner",
                "void",
                ImmutableArray.Create("string")),
            new ResolvedUnsafeAccessorDefinition(
                "__AXSG_UnsafeAccessor_A",
                "Set_Value",
                "global::Demo.Owner",
                "void",
                ImmutableArray.Create("string")));

        service.EmitUnsafeAccessorMethods(builder, unsafeAccessors);

        var emitted = builder.ToString();
        Assert.Contains("[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = \"Set_Value\")]", emitted, StringComparison.Ordinal);
        Assert.Contains("private static extern void __AXSG_UnsafeAccessor_A(global::Demo.Owner __instance, string __arg0);", emitted, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(emitted, "__AXSG_UnsafeAccessor_A("));
    }

    [Fact]
    public void EstimateSourceCapacity_Honors_Minimum_And_Node_Contribution()
    {
        var service = new ViewModelScaffoldEmissionService(GeneratedSourceHintNameService, Escape);

        var minimal = service.EstimateSourceCapacity(CreateViewModel());
        var nested = service.EstimateSourceCapacity(CreateViewModel(rootObject: CreateNodeWithChild()));

        Assert.True(minimal >= 16_384);
        Assert.True(nested >= minimal);
    }

    private static ResolvedViewModel CreateViewModel(
        string buildUri = "avares://Demo/View.xaml",
        ResolvedObjectNode? rootObject = null,
        ResolvedHotDesignArtifactKind hotDesignArtifactKind = ResolvedHotDesignArtifactKind.View,
        ImmutableArray<string> hotDesignScopeHints = default)
    {
        var document = new XamlDocumentModel(
            FilePath: "/workspace/View.xaml",
            TargetPath: "View.xaml",
            ClassFullName: "Demo.App",
            ClassModifier: "public",
            Precompile: true,
            XmlNamespaces: ImmutableDictionary<string, string>.Empty,
            RootObject: new XamlObjectNode(
                XmlNamespace: "using:Demo",
                XmlTypeName: "App",
                Key: null,
                Name: null,
                FieldModifier: null,
                DataType: null,
                CompileBindings: null,
                FactoryMethod: null,
                TypeArguments: ImmutableArray<string>.Empty,
                ArrayItemType: null,
                ConstructorArguments: ImmutableArray<XamlObjectNode>.Empty,
                TextContent: null,
                PropertyAssignments: ImmutableArray<XamlPropertyAssignment>.Empty,
                ChildObjects: ImmutableArray<XamlObjectNode>.Empty,
                PropertyElements: ImmutableArray<XamlPropertyElement>.Empty,
                Line: 1,
                Column: 1),
            NamedElements: ImmutableArray<XamlNamedElement>.Empty,
            Resources: ImmutableArray<XamlResourceDefinition>.Empty,
            Templates: ImmutableArray<XamlTemplateDefinition>.Empty,
            Styles: ImmutableArray<XamlStyleDefinition>.Empty,
            ControlThemes: ImmutableArray<XamlControlThemeDefinition>.Empty,
            Includes: ImmutableArray<XamlIncludeDefinition>.Empty,
            IsValid: true);

        return new ResolvedViewModel(
            Document: document,
            BuildUri: buildUri,
            ClassModifier: "public",
            CreateSourceInfo: false,
            EnableHotReload: false,
            EnableHotDesign: false,
            PassExecutionTrace: ImmutableArray<string>.Empty,
            EmitNameScopeRegistration: false,
            EmitStaticResourceResolver: false,
            HasXBind: false,
            RootObject: rootObject ?? CreateNode(),
            NamedElements: ImmutableArray<ResolvedNamedElement>.Empty,
            Resources: ImmutableArray<ResolvedResourceDefinition>.Empty,
            Templates: ImmutableArray<ResolvedTemplateDefinition>.Empty,
            CompiledBindings: ImmutableArray<ResolvedCompiledBindingDefinition>.Empty,
            UnsafeAccessors: ImmutableArray<ResolvedUnsafeAccessorDefinition>.Empty,
            Styles: ImmutableArray<ResolvedStyleDefinition>.Empty,
            ControlThemes: ImmutableArray<ResolvedControlThemeDefinition>.Empty,
            Includes: ImmutableArray<ResolvedIncludeDefinition>.Empty,
            HotDesignArtifactKind: hotDesignArtifactKind,
            HotDesignScopeHints: hotDesignScopeHints.IsDefault ? ImmutableArray<string>.Empty : hotDesignScopeHints);
    }

    private static ResolvedObjectNode CreateNodeWithChild()
    {
        return CreateNode(
            children: ImmutableArray.Create(CreateNode()));
    }

    private static ResolvedObjectNode CreateNode(
        ImmutableArray<ResolvedObjectNode> children = default)
    {
        return new ResolvedObjectNode(
            KeyExpression: null,
            Name: null,
            TypeName: "global::Demo.Root",
            IsBindingObjectNode: false,
            FactoryExpression: null,
            FactoryValueRequirements: ResolvedValueRequirements.None,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: children.IsDefault ? ImmutableArray<ResolvedObjectNode>.Empty : children,
            ChildAttachmentMode: ResolvedChildAttachmentMode.None,
            ContentPropertyName: null,
            Line: 1,
            Column: 1,
            ChildAddInstructions: ImmutableArray<ResolvedCollectionAddInstruction>.Empty);
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
