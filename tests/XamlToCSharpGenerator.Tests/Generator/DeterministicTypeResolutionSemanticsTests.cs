using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.ExpressionSemantics;

namespace XamlToCSharpGenerator.Tests.Generator;

public class DeterministicTypeResolutionSemanticsTests
{
    [Fact]
    public void CollectCandidatesFromNamespacePrefixes_Preserves_Prefix_Order_And_Deduplicates()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo.One
            {
                public class Widget { }
            }

            namespace Demo.Two
            {
                public class Widget { }
            }
            """);

        var candidates = DeterministicTypeResolutionSemantics.CollectCandidatesFromNamespacePrefixes(
            compilation,
            ["Demo.Two.", "Demo.One.", "Demo.Two."],
            "Widget");

        Assert.Equal(2, candidates.Length);
        Assert.Equal("global::Demo.Two.Widget", candidates[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.Equal("global::Demo.One.Widget", candidates[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    [Fact]
    public void CollectCandidatesFromNamespacePrefixes_Uses_Generic_Arity_Metadata_Name()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo
            {
                public class Box<T> { }
            }
            """);

        var candidates = DeterministicTypeResolutionSemantics.CollectCandidatesFromNamespacePrefixes(
            compilation,
            ["Demo."],
            "Box",
            genericArity: 1);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Box`1", candidate.MetadataName);
    }

    [Fact]
    public void SelectDeterministicCandidate_Returns_Ambiguity_Info_For_Multiple_Candidates()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo.One
            {
                public class Widget { }
            }

            namespace Demo.Two
            {
                public class Widget { }
            }
            """);

        var candidates = DeterministicTypeResolutionSemantics.CollectCandidatesFromNamespacePrefixes(
            compilation,
            ["Demo.One.", "Demo.Two."],
            "Widget");

        var selection = DeterministicTypeResolutionSemantics.SelectDeterministicCandidate(
            candidates,
            "Widget",
            "test strategy");

        Assert.Equal("global::Demo.One.Widget", selection.SelectedCandidate?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.NotNull(selection.Ambiguity);
        Assert.Equal("Widget|test strategy|global::Demo.One.Widget|global::Demo.Two.Widget", selection.Ambiguity!.DedupeKey);
        Assert.Contains("Using 'global::Demo.One.Widget' deterministically.", selection.Ambiguity.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectDeterministicCandidate_Keeps_Ambiguity_For_Same_Name_Candidates_From_Different_Assemblies()
    {
        var controlsReferenceA = CreateMetadataReferenceFromSource(
            "Demo.Controls.A",
            """
            namespace Demo.Controls
            {
                public class Widget { }
            }
            """);
        var controlsReferenceB = CreateMetadataReferenceFromSource(
            "Demo.Controls.B",
            """
            namespace Demo.Controls
            {
                public class Widget { }
            }
            """);
        var compilation = CreateCompilation(
            """
            namespace Demo
            {
                public class Host { }
            }
            """,
            controlsReferenceA,
            controlsReferenceB);

        var candidates = DeterministicTypeResolutionSemantics.CollectCandidatesFromNamespacePrefixes(
            compilation,
            ["Demo.Controls."],
            "Widget");

        Assert.Equal(2, candidates.Length);

        var selection = DeterministicTypeResolutionSemantics.SelectDeterministicCandidate(
            candidates,
            "Widget",
            "test strategy");

        Assert.NotNull(selection.Ambiguity);
        Assert.Contains("Demo.Controls.A", selection.Ambiguity!.Message, StringComparison.Ordinal);
        Assert.Contains("Demo.Controls.B", selection.Ambiguity.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectDeterministicCandidate_Returns_Default_For_Empty_Candidates()
    {
        var selection = DeterministicTypeResolutionSemantics.SelectDeterministicCandidate(
            default,
            "Widget",
            "test strategy");

        Assert.Null(selection.SelectedCandidate);
        Assert.Null(selection.Ambiguity);
    }

    [Theory]
    [InlineData("Widget", null, "Widget")]
    [InlineData("Widget", 0, "Widget")]
    [InlineData("Widget", 2, "Widget`2")]
    [InlineData("Widget`1", 2, "Widget`1")]
    public void AppendGenericArity_Uses_Deterministic_Metadata_Naming(
        string typeName,
        int? genericArity,
        string expected)
    {
        var actual = DeterministicTypeResolutionSemantics.AppendGenericArity(typeName, genericArity);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("List(String)", "List", new[] { "String" })]
    [InlineData("Dictionary(String, List(Int32))", "Dictionary", new[] { "String", "List(Int32)" })]
    [InlineData(" Foo ( Bar , Baz ) ", "Foo", new[] { "Bar", "Baz" })]
    public void TryParseGenericTypeToken_Parses_Type_And_Arguments(
        string token,
        string expectedTypeToken,
        string[] expectedArguments)
    {
        var success = DeterministicTypeResolutionSemantics.TryParseGenericTypeToken(
            token,
            out var typeToken,
            out var argumentTokens);

        Assert.True(success);
        Assert.Equal(expectedTypeToken, typeToken);
        Assert.Equal(expectedArguments, argumentTokens.ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("List")]
    [InlineData("List()")]
    [InlineData("List(String")]
    [InlineData("List(,)")]
    public void TryParseGenericTypeToken_Rejects_Invalid_Syntax(string token)
    {
        var success = DeterministicTypeResolutionSemantics.TryParseGenericTypeToken(
            token,
            out _,
            out _);
        Assert.False(success);
    }

    [Theory]
    [InlineData("clr-namespace:Demo.Controls", "Widget", null, "Demo.Controls.Widget")]
    [InlineData("using:Demo.Controls;assembly=Demo.Assembly", "Widget", null, "Demo.Controls.Widget")]
    [InlineData("clr-namespace:Demo.Controls", "Widget", 2, "Demo.Controls.Widget`2")]
    [InlineData("http://schemas.microsoft.com/winfx/2006/xaml", "Widget", null, null)]
    public void TryBuildClrNamespaceMetadataName_Parses_Known_Xml_Namespace_Forms(
        string xmlNamespace,
        string xmlTypeName,
        int? genericArity,
        string? expected)
    {
        var actual = DeterministicTypeResolutionSemantics.TryBuildClrNamespaceMetadataName(
            xmlNamespace,
            xmlTypeName,
            genericArity);
        Assert.Equal(expected, actual);
    }

    private static CSharpCompilation CreateCompilation(string code, params MetadataReference[] additionalReferences)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };
        AddLoadedRuntimeReference(references, "System.Runtime");
        AddLoadedRuntimeReference(references, "netstandard");
        references.AddRange(additionalReferences);

        return CSharpCompilation.Create(
            assemblyName: "Demo.Assembly",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static MetadataReference CreateMetadataReferenceFromSource(
        string assemblyName,
        string code,
        params MetadataReference[] additionalReferences)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText(code)],
            references: CreateCompilation(string.Empty, additionalReferences).References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
        stream.Position = 0;
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static void AddLoadedRuntimeReference(ICollection<MetadataReference> references, string assemblyName)
    {
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate =>
                    !candidate.IsDynamic &&
                    string.Equals(candidate.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                ?? Assembly.Load(assemblyName);
            if (!string.IsNullOrWhiteSpace(assembly.Location))
            {
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
        }
        catch
        {
            // Optional runtime facades can differ between test hosts.
        }
    }
}
