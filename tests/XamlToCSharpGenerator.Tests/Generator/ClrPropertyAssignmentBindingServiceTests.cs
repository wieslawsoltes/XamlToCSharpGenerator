using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Tests.Generator;

public class ClrPropertyAssignmentBindingServiceTests
{
    [Fact]
    public void TryBind_Uses_Framework_Assignment_For_Inline_Code_Before_Clr_Fallback()
    {
        var compilation = CreateCompilation();
        var request = CreateRequest(compilation, "{x:Code Foo()}");
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var inlineBuilderCalled = false;
        var service = CreateService(
            tryParseInlineCode: static (string value, out string code) =>
            {
                code = "Foo()";
                return value == "{x:Code Foo()}";
            },
            tryBindFrameworkPropertyAssignment: static (
                ClrPropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                bool _,
                string? _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                assignment = new ResolvedPropertyAssignment(
                    PropertyName: "Title",
                    ValueExpression: "FrameworkBind()",
                    ClrPropertyOwnerTypeName: null,
                    ClrPropertyTypeName: null,
                    Line: 1,
                    Column: 1);
                return true;
            },
            tryBuildInlineCodeBindingExpression: (
                Compilation _,
                INamedTypeSymbol? _,
                INamedTypeSymbol? _,
                INamedTypeSymbol? _,
                string _,
                out string expression,
                out string normalized,
                out string? resultTypeName,
                out string errorMessage) =>
            {
                inlineBuilderCalled = true;
                expression = string.Empty;
                normalized = string.Empty;
                resultTypeName = null;
                errorMessage = string.Empty;
                return false;
            });

        var handled = service.TryBind(request, diagnostics, out var assignment);

        Assert.True(handled);
        Assert.NotNull(assignment);
        Assert.Equal("FrameworkBind()", assignment!.ValueExpression);
        Assert.False(inlineBuilderCalled);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void TryBind_Reports_Invalid_Expression_Markup_Diagnostic()
    {
        var compilation = CreateCompilation();
        var request = CreateRequest(compilation, "{x:Expr Foo}");
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var service = CreateService(
            tryConvertExpressionMarkup: static (
                string _,
                Compilation _,
                XamlDocumentModel _,
                GeneratorOptions _,
                INamedTypeSymbol? _,
                string? _,
                out bool isExpressionMarkup,
                out string expressionBindingValueExpression,
                out string accessorExpression,
                out string normalizedExpression,
                out string? resultTypeName,
                out string diagnosticId,
                out string diagnosticMessage) =>
            {
                isExpressionMarkup = true;
                expressionBindingValueExpression = string.Empty;
                accessorExpression = string.Empty;
                normalizedExpression = string.Empty;
                resultTypeName = null;
                diagnosticId = "AXSG0110";
                diagnosticMessage = "missing x:DataType";
                return false;
            });

        var handled = service.TryBind(request, diagnostics, out var assignment);

        Assert.True(handled);
        Assert.Null(assignment);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("AXSG0110", diagnostic.Id);
        Assert.Contains("requires x:DataType", diagnostic.Message);
    }

    [Fact]
    public void TryBind_Falls_Back_To_Literal_Conversion_Assignment()
    {
        var compilation = CreateCompilation();
        var request = CreateRequest(compilation, "42");
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var service = CreateService(
            tryConvertLiteralValue: static (
                string _,
                ITypeSymbol _,
                Compilation _,
                XamlDocumentModel _,
                INamedTypeSymbol? _,
                int _,
                out ResolvedValueConversionResult conversion,
                bool _,
                INamedTypeSymbol? _,
                ImmutableArray<AttributeData> _) =>
            {
                conversion = new ResolvedValueConversionResult(
                    Expression: "42",
                    ValueKind: ResolvedValueKind.Literal,
                    RequiresRuntimeServiceProvider: false,
                    RequiresParentStack: false,
                    RequiresProvideValueTarget: false,
                    RequiresRootObject: false,
                    RequiresBaseUri: false,
                    RequiresStaticResourceResolver: false);
                return true;
            });

        var handled = service.TryBind(request, diagnostics, out var assignment);

        Assert.True(handled);
        Assert.NotNull(assignment);
        Assert.Equal("42", assignment!.ValueExpression);
        Assert.Equal(ResolvedValueKind.Literal, assignment.ValueKind);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void TryBind_Prefers_Typed_Setter_Literal_Before_Implicit_Shorthand()
    {
        var compilation = CreateCompilation();
        var request = CreateSetterValueRequest(compilation, "Left", "Demo.HorizontalAlignment");
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var shorthandBranchVisited = false;
        var service = CreateService(
            isSetterType: static type => type.Name == "Setter",
            isPotentialCSharpExpressionMarkup: static (_, _, _, _, _) => true,
            tryResolveImplicitShorthand: static (
                string _,
                Compilation _,
                XamlDocumentModel _,
                GeneratorOptions _,
                INamedTypeSymbol? _,
                INamedTypeSymbol? _,
                INamedTypeSymbol? _,
                ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? _,
                out bool isShorthandExpression,
                out CSharpShorthandResolutionResult result) =>
            {
                isShorthandExpression = true;
                result = new CSharpShorthandResolutionResult(
                    Kind: CSharpShorthandResolutionKind.BindingPath,
                    Path: "Left",
                    ValueExpression: "ShouldNotBeUsed()",
                    AccessorExpression: "Left",
                    SourceTypeName: "global::Demo.Root",
                    ResultTypeName: "global::System.String",
                    DiagnosticId: null,
                    DiagnosticMessage: null);
                return true;
            },
            tryResolveSetterValueWithPolicy: (
                ClrPropertyAssignmentBindingRequest _,
                ITypeSymbol _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedValueConversionResult resolution) =>
            {
                shorthandBranchVisited = true;
                resolution = new ResolvedValueConversionResult(
                    Expression: "global::Demo.HorizontalAlignment.Left",
                    ValueKind: ResolvedValueKind.Literal);
                return true;
            });

        var handled = service.TryBind(request, diagnostics, out var assignment);

        Assert.True(handled);
        Assert.NotNull(assignment);
        Assert.Equal("global::Demo.HorizontalAlignment.Left", assignment!.ValueExpression);
        Assert.Equal(ResolvedValueKind.Literal, assignment.ValueKind);
        Assert.True(shorthandBranchVisited);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void TryBind_Creates_Assignment_For_Resolved_Implicit_Shorthand()
    {
        var compilation = CreateCompilation();
        var request = CreateRequest(compilation, "Title");
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var service = CreateService(
            canAssignBindingValue: static (_, _) => true,
            isPotentialCSharpExpressionMarkup: static (_, _, _, _, _) => true,
            tryResolveImplicitShorthand: static (
                string _,
                Compilation _,
                XamlDocumentModel _,
                GeneratorOptions _,
                INamedTypeSymbol? _,
                INamedTypeSymbol? _,
                INamedTypeSymbol? _,
                ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? _,
                out bool isShorthandExpression,
                out CSharpShorthandResolutionResult result) =>
            {
                isShorthandExpression = true;
                result = new CSharpShorthandResolutionResult(
                    Kind: CSharpShorthandResolutionKind.BindingPath,
                    Path: "Title",
                    ValueExpression: "BindExpr()",
                    AccessorExpression: "Title",
                    SourceTypeName: "global::Demo.Control",
                    ResultTypeName: "global::System.String",
                    DiagnosticId: null,
                    DiagnosticMessage: null);
                return true;
            });

        var handled = service.TryBind(request, diagnostics, out var assignment);

        Assert.True(handled);
        Assert.NotNull(assignment);
        Assert.Equal("BindExpr()", assignment!.ValueExpression);
        Assert.Equal(ResolvedValueKind.Binding, assignment.ValueKind);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void TryBind_Falls_Back_To_Literal_When_Implicit_Shorthand_Target_Cannot_Assign_Binding()
    {
        var compilation = CreateCompilation();
        var request = CreateRequest(compilation, "Title");
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var service = CreateService(
            isPotentialCSharpExpressionMarkup: static (_, _, _, _, _) => true,
            tryResolveImplicitShorthand: static (
                string _,
                Compilation _,
                XamlDocumentModel _,
                GeneratorOptions _,
                INamedTypeSymbol? _,
                INamedTypeSymbol? _,
                INamedTypeSymbol? _,
                ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? _,
                out bool isShorthandExpression,
                out CSharpShorthandResolutionResult result) =>
            {
                isShorthandExpression = true;
                result = new CSharpShorthandResolutionResult(
                    Kind: CSharpShorthandResolutionKind.BindingPath,
                    Path: "Title",
                    ValueExpression: "BindExpr()",
                    AccessorExpression: "Title",
                    SourceTypeName: "global::Demo.Control",
                    ResultTypeName: "global::System.String",
                    DiagnosticId: null,
                    DiagnosticMessage: null);
                return true;
            },
            tryConvertLiteralValue: static (
                string _,
                ITypeSymbol _,
                Compilation _,
                XamlDocumentModel _,
                INamedTypeSymbol? _,
                int _,
                out ResolvedValueConversionResult conversion,
                bool _,
                INamedTypeSymbol? _,
                ImmutableArray<AttributeData> _) =>
            {
                conversion = new ResolvedValueConversionResult(
                    Expression: "\"Title\"",
                    ValueKind: ResolvedValueKind.Literal);
                return true;
            });

        var handled = service.TryBind(request, diagnostics, out var assignment);

        Assert.True(handled);
        Assert.NotNull(assignment);
        Assert.Equal("\"Title\"", assignment!.ValueExpression);
        Assert.Equal(ResolvedValueKind.Literal, assignment.ValueKind);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void TryBind_Reports_Diagnostic_For_Unresolved_Setter_Value_Instead_Of_Silent_Drop()
    {
        var compilation = CreateCompilation();
        var request = CreateSetterValueRequest(compilation, "Left", "Demo.HorizontalAlignment");
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var service = CreateService(
            isSetterType: static type => type.Name == "Setter",
            tryResolveSetterValueWithPolicy: static (
                ClrPropertyAssignmentBindingRequest _,
                ITypeSymbol _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedValueConversionResult resolution) =>
            {
                resolution = default;
                return false;
            });

        var handled = service.TryBind(request, diagnostics, out var assignment);

        Assert.True(handled);
        Assert.Null(assignment);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("AXSG0102", diagnostic.Id);
        Assert.Contains("Could not convert setter value 'Left'", diagnostic.Message, StringComparison.Ordinal);
    }

    private static ClrPropertyAssignmentBindingService CreateService(
        ClrPropertyAssignmentBindingService.TryBindFrameworkPropertyAssignmentDelegate? tryBindFrameworkPropertyAssignment = null,
        ClrPropertyAssignmentBindingService.TryParseInlineCSharpMarkupExtensionCodeDelegate? tryParseInlineCode = null,
        ClrPropertyAssignmentBindingService.TryBuildInlineCodeBindingExpressionDelegate? tryBuildInlineCodeBindingExpression = null,
        ClrPropertyAssignmentBindingService.TryConvertCSharpExpressionMarkupToBindingExpressionDelegate? tryConvertExpressionMarkup = null,
        ClrPropertyAssignmentBindingService.TryConvertLiteralValueDelegate? tryConvertLiteralValue = null,
        ClrPropertyAssignmentBindingService.IsPotentialCSharpExpressionMarkupDelegate? isPotentialCSharpExpressionMarkup = null,
        ClrPropertyAssignmentBindingService.TryResolveImplicitCSharpShorthandExpressionDelegate? tryResolveImplicitShorthand = null,
        ClrPropertyAssignmentBindingService.TryResolveSetterValueWithPolicyDelegate? tryResolveSetterValueWithPolicy = null,
        ClrPropertyAssignmentBindingService.IsSetterTypeDelegate? isSetterType = null,
        ClrPropertyAssignmentBindingService.CanAssignBindingValueDelegate? canAssignBindingValue = null)
    {
        tryBindFrameworkPropertyAssignment ??= static (
            ClrPropertyAssignmentBindingRequest _,
            ImmutableArray<DiagnosticInfo>.Builder _,
            bool _,
            string? _,
            out ResolvedPropertyAssignment? assignment) =>
        {
            assignment = null;
            return false;
        };

        tryParseInlineCode ??= static (string _, out string code) =>
        {
            code = string.Empty;
            return false;
        };

        tryBuildInlineCodeBindingExpression ??= static (
            Compilation _,
            INamedTypeSymbol? _,
            INamedTypeSymbol? _,
            INamedTypeSymbol? _,
            string _,
            out string expression,
            out string normalized,
            out string? resultTypeName,
            out string errorMessage) =>
        {
            expression = string.Empty;
            normalized = string.Empty;
            resultTypeName = null;
            errorMessage = string.Empty;
            return false;
        };

        tryConvertExpressionMarkup ??= static (
            string _,
            Compilation _,
            XamlDocumentModel _,
            GeneratorOptions _,
            INamedTypeSymbol? _,
            string? _,
            out bool isExpressionMarkup,
            out string expressionBindingValueExpression,
            out string accessorExpression,
            out string normalizedExpression,
            out string? resultTypeName,
            out string diagnosticId,
            out string diagnosticMessage) =>
        {
            isExpressionMarkup = false;
            expressionBindingValueExpression = string.Empty;
            accessorExpression = string.Empty;
            normalizedExpression = string.Empty;
            resultTypeName = null;
            diagnosticId = string.Empty;
            diagnosticMessage = string.Empty;
            return false;
        };

        tryConvertLiteralValue ??= static (
            string _,
            ITypeSymbol _,
            Compilation _,
            XamlDocumentModel _,
            INamedTypeSymbol? _,
            int _,
            out ResolvedValueConversionResult conversion,
            bool _,
            INamedTypeSymbol? _,
            ImmutableArray<AttributeData> _) =>
        {
            conversion = default;
            return false;
        };

        isPotentialCSharpExpressionMarkup ??= static (_, _, _, _, _) => false;

        tryResolveImplicitShorthand ??= static (
            string _,
            Compilation _,
            XamlDocumentModel _,
            GeneratorOptions _,
            INamedTypeSymbol? _,
            INamedTypeSymbol? _,
            INamedTypeSymbol? _,
            ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? _,
            out bool isShorthandExpression,
            out CSharpShorthandResolutionResult result) =>
        {
            isShorthandExpression = false;
            result = default;
            return false;
        };

        tryResolveSetterValueWithPolicy ??= static (
            ClrPropertyAssignmentBindingRequest _,
            ITypeSymbol _,
            ImmutableArray<DiagnosticInfo>.Builder _,
            out ResolvedValueConversionResult resolution) =>
        {
            resolution = default;
            return false;
        };

        isSetterType ??= static _ => false;
        canAssignBindingValue ??= static (_, _) => false;

        return new ClrPropertyAssignmentBindingService(
            static _ => false,
            static (string _, Compilation _, XamlDocumentModel _, INamedTypeSymbol? _, out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            tryBindFrameworkPropertyAssignment,
            tryParseInlineCode,
            tryBuildInlineCodeBindingExpression,
            isPotentialCSharpExpressionMarkup,
            tryResolveImplicitShorthand,
            tryConvertExpressionMarkup,
            static (string _, out XBindMarkup markup) =>
            {
                markup = default;
                return false;
            },
            static (
                Compilation _,
                XamlDocumentModel _,
                XamlObjectNode _,
                XBindMarkup _,
                INamedTypeSymbol? _,
                INamedTypeSymbol? _,
                INamedTypeSymbol? _,
                ITypeSymbol _,
                int _,
                bool _,
                string _,
                out string bindingExpression,
                out string? resultTypeName,
                out string errorCode,
                    out string errorMessage) =>
            {
                bindingExpression = string.Empty;
                resultTypeName = null;
                errorCode = string.Empty;
                errorMessage = string.Empty;
                return false;
            },
            canAssignBindingValue,
            static (string _, out BindingMarkup markup) =>
            {
                markup = default;
                return false;
            },
            static (_, _, _, _, _, _) => false,
            static (
                Compilation _,
                XamlDocumentModel _,
                BindingMarkup _,
                INamedTypeSymbol? _,
                INamedTypeSymbol? _,
                out INamedTypeSymbol? sourceType,
                out bool requiresAmbientDataType,
                out bool hasInvalidLocalDataType) =>
            {
                sourceType = null;
                requiresAmbientDataType = false;
                hasInvalidLocalDataType = false;
                return false;
            },
            static (
                Compilation _,
                XamlDocumentModel _,
                INamedTypeSymbol _,
                string _,
                ITypeSymbol? _,
                ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? _,
                out CompiledBindingAccessorResolutionResult resolution,
                out string errorMessage) =>
            {
                resolution = default;
                errorMessage = string.Empty;
                return false;
            },
            static (line, column) => $"__CB_{line}_{column}",
            static (
                Compilation _,
                XamlDocumentModel _,
                BindingMarkup _,
                ITypeSymbol _,
                INamedTypeSymbol? _,
                int _,
                out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            static (string _, out MarkupExtensionInfo markup) =>
            {
                markup = default;
                return false;
            },
            isSetterType,
            static (string _, ITypeSymbol _, XamlDocumentModel _, out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            static (_, _) => false,
            static (string _, ITypeSymbol _, out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            static (string _, INamedTypeSymbol _, INamedTypeSymbol? _, out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            tryResolveSetterValueWithPolicy,
            tryConvertLiteralValue,
            static (request, valueExpression, valueKind, requiresStaticResourceResolver, valueRequirements, preserveBindingValue) =>
                new ResolvedPropertyAssignment(
                    PropertyName: request.Property.Name,
                    ValueExpression: valueExpression,
                    ClrPropertyOwnerTypeName: request.OwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClrPropertyTypeName: request.Property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Line: request.Assignment.Line,
                    Column: request.Assignment.Column,
                    Condition: request.Assignment.Condition,
                    ValueKind: valueKind,
                    RequiresStaticResourceResolver: requiresStaticResourceResolver,
                    ValueRequirements: valueRequirements,
                    PreserveBindingValue: preserveBindingValue),
            static _ => false);
    }

    private static ClrPropertyAssignmentBindingRequest CreateRequest(CSharpCompilation compilation, string value)
    {
        var ownerType = compilation.GetTypeByMetadataName("Demo.Control")!;
        var property = ownerType.GetMembers("Title").OfType<IPropertySymbol>().Single();
        return new ClrPropertyAssignmentBindingRequest(
            OwnerType: ownerType,
            OwnerTypeName: "global::Demo.Control",
            Property: property,
            Assignment: new XamlPropertyAssignment("Title", string.Empty, value, IsAttached: false, Line: 3, Column: 4),
            Compilation: compilation,
            Document: CreateDocument(),
            Options: CreateOptions(),
            CompiledBindings: ImmutableArray.CreateBuilder<ResolvedCompiledBindingDefinition>(),
            UnsafeAccessors: ImmutableArray.CreateBuilder<ResolvedUnsafeAccessorDefinition>(),
            CompileBindingsEnabled: true,
            AssignmentDataType: ownerType,
            CurrentSetterTargetType: ownerType,
            BindingPriorityScope: 0,
            IsTemplateBindingPriorityScope: false,
            RootTypeSymbol: ownerType,
            IsInsideDataTemplate: false,
            XBindDefaultMode: "OneTime",
            CurrentNode: CreateObjectNode(),
            InferredSetterValueType: null,
            SelectorNestingTypeHint: null,
            FrameworkPropertyMetadataTypeName: "global::Avalonia.AvaloniaProperty");
    }

    private static ClrPropertyAssignmentBindingRequest CreateSetterValueRequest(
        CSharpCompilation compilation,
        string value,
        string inferredSetterValueTypeMetadataName)
    {
        var ownerType = compilation.GetTypeByMetadataName("Demo.Setter")!;
        var property = ownerType.GetMembers("Value").OfType<IPropertySymbol>().Single();
        return new ClrPropertyAssignmentBindingRequest(
            OwnerType: ownerType,
            OwnerTypeName: "global::Demo.Setter",
            Property: property,
            Assignment: new XamlPropertyAssignment("Value", string.Empty, value, IsAttached: false, Line: 3, Column: 4),
            Compilation: compilation,
            Document: CreateDocument(),
            Options: CreateOptions(),
            CompiledBindings: ImmutableArray.CreateBuilder<ResolvedCompiledBindingDefinition>(),
            UnsafeAccessors: ImmutableArray.CreateBuilder<ResolvedUnsafeAccessorDefinition>(),
            CompileBindingsEnabled: true,
            AssignmentDataType: null,
            CurrentSetterTargetType: compilation.GetTypeByMetadataName("Demo.TargetControl"),
            BindingPriorityScope: 0,
            IsTemplateBindingPriorityScope: false,
            RootTypeSymbol: compilation.GetTypeByMetadataName("Demo.Root"),
            IsInsideDataTemplate: false,
            XBindDefaultMode: "OneTime",
            CurrentNode: new XamlObjectNode(
                XmlNamespace: "https://github.com/avaloniaui",
                XmlTypeName: "Setter",
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
                PropertyAssignments: ImmutableArray.Create(
                    new XamlPropertyAssignment("Property", string.Empty, "HorizontalAlignment", IsAttached: false, Line: 2, Column: 2),
                    new XamlPropertyAssignment("Value", string.Empty, value, IsAttached: false, Line: 3, Column: 4)),
                ChildObjects: ImmutableArray<XamlObjectNode>.Empty,
                PropertyElements: ImmutableArray<XamlPropertyElement>.Empty,
                Line: 1,
                Column: 1),
            InferredSetterValueType: compilation.GetTypeByMetadataName(inferredSetterValueTypeMetadataName),
            SelectorNestingTypeHint: null,
            FrameworkPropertyMetadataTypeName: "global::Avalonia.AvaloniaProperty");
    }

    private static GeneratorOptions CreateOptions()
    {
        return new GeneratorOptions(
            IsEnabled: true,
            UseCompiledBindingsByDefault: true,
            CSharpExpressionsEnabled: true,
            ImplicitCSharpExpressionsEnabled: true,
            CreateSourceInfo: false,
            StrictMode: true,
            HotReloadEnabled: false,
            HotReloadErrorResilienceEnabled: false,
            IdeHotReloadEnabled: false,
            HotDesignEnabled: false,
            IosHotReloadEnabled: false,
            IosHotReloadUseInterpreter: false,
            DotNetWatchBuild: false,
            BuildingInsideVisualStudio: false,
            BuildingByReSharper: false,
            TracePasses: false,
            MetricsEnabled: false,
            MetricsDetailed: false,
            MarkupParserLegacyInvalidNamedArgumentFallbackEnabled: false,
            TypeResolutionCompatibilityFallbackEnabled: false,
            AllowImplicitXmlnsDeclaration: false,
            ImplicitStandardXmlnsPrefixesEnabled: false,
            ImplicitDefaultXmlns: "https://github.com/avaloniaui",
            InferClassFromPath: false,
            ImplicitProjectNamespacesEnabled: false,
            GlobalXmlnsPrefixes: null,
            RootNamespace: "Demo",
            IntermediateOutputPath: null,
            BaseIntermediateOutputPath: null,
            ProjectDirectory: null,
            Backend: "SourceGen",
            AssemblyName: "Demo");
    }

    private static XamlDocumentModel CreateDocument()
    {
        return new XamlDocumentModel(
            FilePath: "/tests/Sample.axaml",
            TargetPath: "Sample.axaml",
            ClassFullName: "Demo.SampleView",
            ClassModifier: "public",
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
            XmlNamespace: "https://github.com/avaloniaui",
            XmlTypeName: "Control",
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
            Column: 1);
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
                              namespace Demo
                              {
                                  public enum HorizontalAlignment
                                  {
                                      Left,
                                      Center,
                                      Right,
                                      Stretch
                                  }

                                  public class Control
                                  {
                                      public string? Title { get; set; }
                                  }

                                  public class TargetControl : Control { }

                                  public class Root
                                  {
                                      public string? Title { get; set; }
                                  }

                                  public class Setter
                                  {
                                      public object? Value { get; set; }
                                  }
                              }
                              """;

        return CSharpCompilation.Create(
            "ClrPropertyAssignmentBindingServiceTests",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
