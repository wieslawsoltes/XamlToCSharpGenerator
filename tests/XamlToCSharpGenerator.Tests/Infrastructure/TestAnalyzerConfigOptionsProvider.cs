using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace XamlToCSharpGenerator.Tests.Infrastructure;

internal sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigOptions _global;
    private readonly ImmutableDictionary<string, AnalyzerConfigOptions> _additionalOptionsByPath;

    public TestAnalyzerConfigOptionsProvider(IEnumerable<KeyValuePair<string, string>> values)
    {
        _global = new TestAnalyzerConfigOptions(values);
        _additionalOptionsByPath = ImmutableDictionary<string, AnalyzerConfigOptions>.Empty;
    }

    public TestAnalyzerConfigOptionsProvider(
        IEnumerable<KeyValuePair<string, string>> values,
        IEnumerable<(string Path, IEnumerable<KeyValuePair<string, string>> Values)> additionalFileOptions)
    {
        _global = new TestAnalyzerConfigOptions(values);
        var builder = ImmutableDictionary.CreateBuilder<string, AnalyzerConfigOptions>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in additionalFileOptions)
        {
            builder[NormalizePath(entry.Path)] = new TestAnalyzerConfigOptions(entry.Values);
        }

        _additionalOptionsByPath = builder.ToImmutable();
    }

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        return _global;
    }

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        if (_additionalOptionsByPath.TryGetValue(NormalizePath(textFile.Path), out var options))
        {
            return options;
        }

        return _global;
    }

    public override AnalyzerConfigOptions GlobalOptions => _global;

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
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
