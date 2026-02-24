using System;
using System.Collections.Immutable;
using System.Globalization;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class EventBindingPathSemantics
{
    public static bool TrySplitMethodPath(string methodPath, out string targetPath, out string methodName)
    {
        targetPath = ".";
        methodName = string.Empty;

        if (string.IsNullOrWhiteSpace(methodPath))
        {
            return false;
        }

        var normalized = methodPath.Trim();
        if (!IsSimplePath(normalized))
        {
            return false;
        }

        var lastDot = normalized.LastIndexOf('.');
        if (lastDot <= 0 || lastDot >= normalized.Length - 1)
        {
            methodName = normalized;
            return IsSimpleIdentifier(methodName);
        }

        targetPath = normalized[..lastDot];
        methodName = normalized[(lastDot + 1)..];
        return targetPath.Length > 0 &&
               methodName.Length > 0 &&
               IsSimplePath(targetPath) &&
               IsSimpleIdentifier(methodName);
    }

    public static ImmutableArray<ImmutableArray<ResolvedEventBindingMethodArgumentKind>> BuildMethodArgumentSets(
        bool hasParameterToken,
        bool passEventArgs)
    {
        if (hasParameterToken)
        {
            return
            [
                [ResolvedEventBindingMethodArgumentKind.Parameter],
                [ResolvedEventBindingMethodArgumentKind.Sender, ResolvedEventBindingMethodArgumentKind.Parameter],
                [ResolvedEventBindingMethodArgumentKind.Sender, ResolvedEventBindingMethodArgumentKind.EventArgs, ResolvedEventBindingMethodArgumentKind.Parameter]
            ];
        }

        if (passEventArgs)
        {
            return
            [
                [ResolvedEventBindingMethodArgumentKind.Sender, ResolvedEventBindingMethodArgumentKind.EventArgs],
                [ResolvedEventBindingMethodArgumentKind.EventArgs],
                [ResolvedEventBindingMethodArgumentKind.Sender],
                ImmutableArray<ResolvedEventBindingMethodArgumentKind>.Empty
            ];
        }

        return
        [
            ImmutableArray<ResolvedEventBindingMethodArgumentKind>.Empty,
            [ResolvedEventBindingMethodArgumentKind.Sender],
            [ResolvedEventBindingMethodArgumentKind.EventArgs],
            [ResolvedEventBindingMethodArgumentKind.Sender, ResolvedEventBindingMethodArgumentKind.EventArgs]
        ];
    }

    public static bool IsSimplePath(string path)
    {
        var normalizedPath = path.Trim();
        if (normalizedPath.Length == 0)
        {
            return false;
        }

        if (normalizedPath == ".")
        {
            return true;
        }

        var segments = normalizedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < segments.Length; index++)
        {
            if (!IsSimpleIdentifier(segments[index]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsSimpleIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var start = value[0];
        if (!(start == '_' || char.IsLetter(start)))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var current = value[index];
            if (!(current == '_' || char.IsLetterOrDigit(current)))
            {
                return false;
            }
        }

        return true;
    }

    public static string ExtractMethodName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Trim();
        var dot = normalized.LastIndexOf('.');
        if (dot > 0 && dot < normalized.Length - 1)
        {
            return normalized[(dot + 1)..];
        }

        return normalized;
    }

    public static string BuildGeneratedMethodName(string eventName, int line, int column)
    {
        var chars = eventName.ToCharArray();
        if (chars.Length == 0)
        {
            return "__AXSG_EventBinding_" + line.ToString(CultureInfo.InvariantCulture) + "_" + column.ToString(CultureInfo.InvariantCulture);
        }

        for (var index = 0; index < chars.Length; index++)
        {
            if (!char.IsLetterOrDigit(chars[index]) && chars[index] != '_')
            {
                chars[index] = '_';
            }
        }

        if (!char.IsLetter(chars[0]) && chars[0] != '_')
        {
            return "__AXSG_EventBinding_E" + new string(chars) + "_" +
                   line.ToString(CultureInfo.InvariantCulture) + "_" +
                   column.ToString(CultureInfo.InvariantCulture);
        }

        return "__AXSG_EventBinding_" + new string(chars) + "_" +
               line.ToString(CultureInfo.InvariantCulture) + "_" +
               column.ToString(CultureInfo.InvariantCulture);
    }
}
