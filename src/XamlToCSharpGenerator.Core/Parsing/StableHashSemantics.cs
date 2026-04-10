using System.Globalization;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class StableHashSemantics
{
    public static uint ComputeFnv1a(string value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        for (var index = 0; index < value.Length; index++)
        {
            hash ^= value[index];
            hash *= prime;
        }

        return hash;
    }

    public static ulong ComputeFnv1a64(string value)
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;

        var hash = offsetBasis;
        for (var index = 0; index < value.Length; index++)
        {
            hash ^= value[index];
            hash *= prime;
        }

        return hash;
    }

    public static string ComputeFnv1aHex(string value)
    {
        return ComputeFnv1a(value).ToString("x8", CultureInfo.InvariantCulture);
    }

    public static string ComputeFnv1aHex64(string value)
    {
        return ComputeFnv1a64(value).ToString("x16", CultureInfo.InvariantCulture);
    }

    public static string ComputeFnv1aHexIgnoreCase(string value)
    {
        return ComputeFnv1aHex(value.ToLowerInvariant());
    }
}
