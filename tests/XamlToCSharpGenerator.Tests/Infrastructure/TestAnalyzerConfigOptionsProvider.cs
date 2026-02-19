using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace XamlToCSharpGenerator.Tests.Infrastructure;

internal sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigOptions _global;

    public TestAnalyzerConfigOptionsProvider(IEnumerable<KeyValuePair<string, string>> values)
    {
        _global = new TestAnalyzerConfigOptions(values);
    }

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        return _global;
    }

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        return _global;
    }

    public override AnalyzerConfigOptions GlobalOptions => _global;
}

internal sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
{
    private readonly Dictionary<string, string> _values;

    public TestAnalyzerConfigOptions(IEnumerable<KeyValuePair<string, string>> values)
    {
        _values = new Dictionary<string, string>(values);
    }

    public override bool TryGetValue(string key, out string value)
    {
        return _values.TryGetValue(key, out value!);
    }
}
