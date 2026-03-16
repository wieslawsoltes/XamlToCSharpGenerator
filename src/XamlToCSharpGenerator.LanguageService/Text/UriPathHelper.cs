using System;
using System.IO;

namespace XamlToCSharpGenerator.LanguageService.Text;

internal static class UriPathHelper
{
    public static string ToFilePath(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return string.Empty;
        }

        if (TryNormalizeWindowsDrivePath(uri, out var normalizedWindowsPath))
        {
            return NormalizePlatformPath(normalizedWindowsPath);
        }

        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            if (parsed.IsFile)
            {
                return NormalizePlatformPath(parsed.LocalPath);
            }

            return uri;
        }

        return NormalizePlatformPath(uri);
    }

    public static string ToDocumentUri(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        if (TryNormalizeWindowsDrivePath(filePath, out var normalizedWindowsPath))
        {
            return CreateWindowsFileUri(normalizedWindowsPath);
        }

        if (Uri.TryCreate(filePath, UriKind.Absolute, out var parsed))
        {
            if (parsed.IsFile)
            {
                return parsed.AbsoluteUri;
            }

            return filePath;
        }

        var normalizedPath = NormalizeFilePath(filePath);
        if (TryNormalizeWindowsDrivePath(normalizedPath, out normalizedWindowsPath))
        {
            return CreateWindowsFileUri(normalizedWindowsPath);
        }

        return new Uri(normalizedPath).AbsoluteUri;
    }

    public static string NormalizeFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (TryNormalizeWindowsDrivePath(path, out var normalizedWindowsPath))
        {
            return NormalizePlatformPath(normalizedWindowsPath);
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out var parsed))
        {
            if (parsed.IsFile)
            {
                return NormalizePlatformPath(parsed.LocalPath);
            }

            return path;
        }

        return NormalizePlatformPath(path);
    }

    private static string NormalizePlatformPath(string path)
    {
        if (TryNormalizeWindowsDrivePath(path, out var normalizedWindowsPath))
        {
            return OperatingSystem.IsWindows()
                ? Path.GetFullPath(normalizedWindowsPath)
                : normalizedWindowsPath;
        }

        return Path.GetFullPath(path);
    }

    private static string CreateWindowsFileUri(string windowsPath)
    {
        var builder = new UriBuilder(Uri.UriSchemeFile, string.Empty)
        {
            Path = windowsPath.Replace('\\', '/')
        };

        return builder.Uri.AbsoluteUri;
    }

    private static bool TryNormalizeWindowsDrivePath(string value, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (candidate.Length >= 4 &&
            IsDirectorySeparator(candidate[0]) &&
            IsDriveSpecifier(candidate, 1) &&
            IsDirectorySeparator(candidate[3]))
        {
            candidate = candidate.Substring(1);
        }

        if (candidate.Length < 3 || !IsDriveSpecifier(candidate, 0) || !IsDirectorySeparator(candidate[2]))
        {
            return false;
        }

        normalizedPath =
            char.ToUpperInvariant(candidate[0]) +
            ":" +
            candidate.Substring(2).Replace('/', '\\');
        return true;
    }

    private static bool IsDriveSpecifier(string value, int index)
    {
        return index + 1 < value.Length &&
               char.IsLetter(value[index]) &&
               value[index + 1] == ':';
    }

    private static bool IsDirectorySeparator(char value)
    {
        return value == '/' || value == '\\';
    }
}
