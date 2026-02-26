using System;
using System.Collections.Generic;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class XamlIncludePathSemantics
{
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalizedSeparators = path.Replace('\\', '/');
        var parts = normalizedSeparators.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            stack.Add(part);
        }

        return string.Join("/", stack);
    }

    public static string GetDirectory(string targetPath)
    {
        if (!XamlTokenSplitSemantics.TrySplitAtLastSeparator(targetPath, '/', out var directory, out _))
        {
            return string.Empty;
        }

        return directory;
    }

    public static string CombinePath(string baseDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return relativePath;
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return baseDirectory;
        }

        return baseDirectory.TrimEnd('/') + "/" + relativePath.TrimStart('/');
    }
}
