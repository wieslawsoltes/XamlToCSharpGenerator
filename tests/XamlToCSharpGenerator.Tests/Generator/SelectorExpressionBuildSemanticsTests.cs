using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.ExpressionSemantics;

namespace XamlToCSharpGenerator.Tests.Generator;

public class SelectorExpressionBuildSemanticsTests
{
    [Fact]
    public void TryBuildSelectorExpression_Builds_Combinators_And_Pseudo_Functions()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo;

            public class StyledElement { }
            public class Button : StyledElement { }
            public class TextBlock : StyledElement { }
            """);

        var emitter = new TestSelectorExpressionEmitter();
        var success = SelectorExpressionBuildSemantics.TryBuildSelectorExpression(
            "Button > TextBlock:nth-child(2n+1)",
            selectorTypeFallback: null,
            selectorNestingTypeHint: null,
            resolveTypeToken: token => compilation.GetTypeByMetadataName("Demo." + token),
            emitter: emitter,
            tryResolvePropertyPredicate: static (
                string _,
                INamedTypeSymbol? __,
                out string ___,
                out string ____) =>
            {
                ___ = string.Empty;
                ____ = string.Empty;
                return false;
            },
            out var expression);

        Assert.True(success);
        Assert.Equal(
            "nth-child(of-type(child(of-type(null,Button)),TextBlock),2,1)",
            expression);
    }

    [Fact]
    public void TryBuildSelectorExpression_Invokes_Property_Predicate_Callback_With_Type_Hint()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo;

            public class StyledElement { }
            public class TextBlock : StyledElement { }
            """);

        var emitter = new TestSelectorExpressionEmitter();
        INamedTypeSymbol? callbackOwnerType = null;
        string? callbackPredicate = null;
        var success = SelectorExpressionBuildSemantics.TryBuildSelectorExpression(
            "TextBlock[Tag='Probe']",
            selectorTypeFallback: null,
            selectorNestingTypeHint: null,
            resolveTypeToken: token => compilation.GetTypeByMetadataName("Demo." + token),
            emitter: emitter,
            tryResolvePropertyPredicate: (
                string predicateText,
                INamedTypeSymbol? defaultOwnerType,
                out string propertyExpression,
                out string valueExpression) =>
            {
                callbackOwnerType = defaultOwnerType;
                callbackPredicate = predicateText;
                propertyExpression = "property";
                valueExpression = "value";
                return true;
            },
            out var expression);

        Assert.True(success);
        Assert.Equal("prop-equals(of-type(null,TextBlock),property,value)", expression);
        Assert.Equal("Tag='Probe'", callbackPredicate);
        Assert.Equal(
            "global::Demo.TextBlock",
            callbackOwnerType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    [Fact]
    public void TryBuildSelectorExpression_Builds_Not_Pseudo_With_Recursive_Selector_Expression()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo;

            public class StyledElement { }
            public class Button : StyledElement { }
            public class TextBlock : StyledElement { }
            """);

        var emitter = new TestSelectorExpressionEmitter();
        var success = SelectorExpressionBuildSemantics.TryBuildSelectorExpression(
            ":not(Button, TextBlock)",
            selectorTypeFallback: null,
            selectorNestingTypeHint: null,
            resolveTypeToken: token => compilation.GetTypeByMetadataName("Demo." + token),
            emitter: emitter,
            tryResolvePropertyPredicate: static (
                string _,
                INamedTypeSymbol? __,
                out string ___,
                out string ____) =>
            {
                ___ = string.Empty;
                ____ = string.Empty;
                return false;
            },
            out var expression);

        Assert.True(success);
        Assert.Equal("not(null,or(of-type(null,Button),of-type(null,TextBlock)))", expression);
    }

    [Fact]
    public void TryBuildSelectorExpression_Rejects_Invalid_Not_Argument_That_Fails_Syntax_Validation()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo;

            public class StyledElement { }
            public class Button : StyledElement { }
            """);

        var emitter = new TestSelectorExpressionEmitter();
        var predicateResolverInvoked = false;
        var success = SelectorExpressionBuildSemantics.TryBuildSelectorExpression(
            "Button:not([Tag='Probe'])",
            selectorTypeFallback: null,
            selectorNestingTypeHint: compilation.GetTypeByMetadataName("Demo.Button"),
            resolveTypeToken: token => compilation.GetTypeByMetadataName("Demo." + token),
            emitter: emitter,
            tryResolvePropertyPredicate: (
                string _,
                INamedTypeSymbol? __,
                out string propertyExpression,
                out string valueExpression) =>
            {
                predicateResolverInvoked = true;
                propertyExpression = "property";
                valueExpression = "value";
                return true;
            },
            out _);

        Assert.False(success);
        Assert.False(predicateResolverInvoked);
    }

    [Fact]
    public void TryBuildSelectorExpression_Builds_Middle_Nesting_When_Nesting_Context_Is_Available()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo;

            public class StyledElement { }
            public class Button : StyledElement { }
            """);

        var emitter = new TestSelectorExpressionEmitter();
        var success = SelectorExpressionBuildSemantics.TryBuildSelectorExpression(
            "Button^:pointerover",
            selectorTypeFallback: null,
            selectorNestingTypeHint: compilation.GetTypeByMetadataName("Demo.Button"),
            resolveTypeToken: token => compilation.GetTypeByMetadataName("Demo." + token),
            emitter: emitter,
            tryResolvePropertyPredicate: static (
                string _,
                INamedTypeSymbol? __,
                out string propertyExpression,
                out string valueExpression) =>
            {
                propertyExpression = string.Empty;
                valueExpression = string.Empty;
                return false;
            },
            out var expression);

        Assert.True(success);
        Assert.Equal("pseudo(nest(of-type(null,Button)),pointerover)", expression);
    }

    [Fact]
    public void TryBuildSelectorExpression_Fails_For_Nesting_Selector_Without_Nesting_Context()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo;

            public class StyledElement { }
            public class Button : StyledElement { }
            """);

        var emitter = new TestSelectorExpressionEmitter();
        var success = SelectorExpressionBuildSemantics.TryBuildSelectorExpression(
            "^:pointerover",
            selectorTypeFallback: null,
            selectorNestingTypeHint: null,
            resolveTypeToken: token => compilation.GetTypeByMetadataName("Demo." + token),
            emitter: emitter,
            tryResolvePropertyPredicate: static (
                string _,
                INamedTypeSymbol? __,
                out string propertyExpression,
                out string valueExpression) =>
            {
                propertyExpression = string.Empty;
                valueExpression = string.Empty;
                return false;
            },
            out _);

        Assert.False(success);
    }

    [Fact]
    public void TryBuildSelectorExpression_Fails_For_Unresolved_Type_Token()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo;

            public class StyledElement { }
            """);

        var emitter = new TestSelectorExpressionEmitter();
        var success = SelectorExpressionBuildSemantics.TryBuildSelectorExpression(
            "MissingControl",
            selectorTypeFallback: null,
            selectorNestingTypeHint: null,
            resolveTypeToken: token => compilation.GetTypeByMetadataName("Demo." + token),
            emitter: emitter,
            tryResolvePropertyPredicate: static (
                string _,
                INamedTypeSymbol? __,
                out string ___,
                out string ____) =>
            {
                ___ = string.Empty;
                ____ = string.Empty;
                return false;
            },
            out _);

        Assert.False(success);
    }

    private static CSharpCompilation CreateCompilation(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };

        return CSharpCompilation.Create(
            assemblyName: "Demo.Assembly",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private sealed class TestSelectorExpressionEmitter : ISelectorExpressionEmitter
    {
        public string EmitOr(ImmutableArray<string> branchExpressions)
        {
            return "or(" + string.Join(",", branchExpressions) + ")";
        }

        public string EmitDescendant(string previousExpression)
        {
            return "desc(" + previousExpression + ")";
        }

        public string EmitChild(string previousExpression)
        {
            return "child(" + previousExpression + ")";
        }

        public string EmitTemplate(string previousExpression)
        {
            return "template(" + previousExpression + ")";
        }

        public string EmitNesting(string previousExpressionOrNull)
        {
            return "nest(" + previousExpressionOrNull + ")";
        }

        public string EmitOfType(string previousExpressionOrNull, INamedTypeSymbol type)
        {
            return "of-type(" + previousExpressionOrNull + "," + type.Name + ")";
        }

        public string EmitClass(string previousExpressionOrNull, string className)
        {
            return "class(" + previousExpressionOrNull + "," + className + ")";
        }

        public string EmitName(string previousExpressionOrNull, string name)
        {
            return "name(" + previousExpressionOrNull + "," + name + ")";
        }

        public string EmitPseudoClass(string previousExpressionOrNull, string pseudoClassName)
        {
            return "pseudo(" + previousExpressionOrNull + "," + pseudoClassName + ")";
        }

        public string EmitIs(string previousExpressionOrNull, INamedTypeSymbol type)
        {
            return "is(" + previousExpressionOrNull + "," + type.Name + ")";
        }

        public string EmitNot(string previousExpressionOrNull, string argumentExpression)
        {
            return "not(" + previousExpressionOrNull + "," + argumentExpression + ")";
        }

        public string EmitNthChild(string previousExpressionOrNull, int step, int offset)
        {
            return "nth-child(" + previousExpressionOrNull + "," + step + "," + offset + ")";
        }

        public string EmitNthLastChild(string previousExpressionOrNull, int step, int offset)
        {
            return "nth-last-child(" + previousExpressionOrNull + "," + step + "," + offset + ")";
        }

        public string EmitPropertyEquals(string previousExpressionOrNull, string propertyExpression, string valueExpression)
        {
            return "prop-equals(" + previousExpressionOrNull + "," + propertyExpression + "," + valueExpression + ")";
        }
    }
}
