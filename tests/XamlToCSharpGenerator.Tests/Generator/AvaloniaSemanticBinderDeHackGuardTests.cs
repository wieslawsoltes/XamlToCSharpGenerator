using System;
using System.IO;

namespace XamlToCSharpGenerator.Tests.Generator;

public class AvaloniaSemanticBinderDeHackGuardTests
{
    [Fact]
    public void Binder_Does_Not_Use_Legacy_Markup_Context_Token_Scanning()
    {
        var source = ReadBinderSource();

        Assert.DoesNotContain("ContainsMarkupContextTokens(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Does_Not_Use_Binding_Type_Suffix_Heuristics()
    {
        var source = ReadBinderSource();

        Assert.DoesNotContain("EndsWith(\".Binding\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EndsWith(\"Binding\"", source, StringComparison.Ordinal);
        Assert.Contains("IsBindingObjectType(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_Centralized_EventBinding_Source_Validation()
    {
        var source = ReadBinderSource();

        Assert.Contains("TryValidateEventBindingBindingSource(", source, StringComparison.Ordinal);
    }

    private static string ReadBinderSource()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var binderPath = Path.Combine(
            repositoryRoot,
            "src",
            "XamlToCSharpGenerator.Avalonia",
            "Binding",
            "AvaloniaSemanticBinder.cs");
        return File.ReadAllText(binderPath);
    }
}
