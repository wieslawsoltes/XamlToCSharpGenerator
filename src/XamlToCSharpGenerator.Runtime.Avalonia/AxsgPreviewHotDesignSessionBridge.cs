using System;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Templates;
using global::Avalonia.Styling;

namespace XamlToCSharpGenerator.Runtime;

/// <summary>
/// Bridges the active preview designer session into the reusable AXSG hot-design runtime workspace.
/// </summary>
public static class AxsgPreviewHotDesignSessionBridge
{
    private static readonly object Sync = new();

    private static Control? _currentRootControl;
    private static string? _currentBuildUri;
    private static string? _currentSourcePath;
    private static string? _currentXamlText;
    private static string? _hoverBuildUri;
    private static string? _hoverElementId;

    public static void UpdateCurrentDocument(
        object? root,
        string? xamlText,
        string? buildUri,
        string? sourcePath)
    {
        if (root is null || string.IsNullOrWhiteSpace(buildUri))
        {
            ClearCurrentDocument();
            return;
        }

        string normalizedBuildUri = buildUri.Trim();
        string? normalizedSourcePath = string.IsNullOrWhiteSpace(sourcePath) ? null : sourcePath.Trim();
        string normalizedXamlText = xamlText ?? string.Empty;

        lock (Sync)
        {
            _currentRootControl = root as Control;
            _currentBuildUri = normalizedBuildUri;
            _currentSourcePath = normalizedSourcePath;
            _currentXamlText = normalizedXamlText;

            if (!string.Equals(_hoverBuildUri, normalizedBuildUri, StringComparison.OrdinalIgnoreCase))
            {
                _hoverBuildUri = null;
                _hoverElementId = null;
            }
        }

        XamlSourceGenHotDesignManager.Register(
            root,
            static _ => { },
            new SourceGenHotDesignRegistrationOptions
            {
                TrackingType = root.GetType(),
                BuildUri = normalizedBuildUri,
                SourcePath = normalizedSourcePath,
                DocumentRole = InferDocumentRole(root),
                ArtifactKind = InferArtifactKind(root)
            });
        XamlSourceGenHotDesignTool.SetCurrentDocumentText(normalizedBuildUri, normalizedXamlText);
        XamlSourceGenHotDesignTool.SelectDocument(normalizedBuildUri);
    }

    public static void ClearCurrentDocument()
    {
        lock (Sync)
        {
            _currentRootControl = null;
            _currentBuildUri = null;
            _currentSourcePath = null;
            _currentXamlText = null;
            _hoverBuildUri = null;
            _hoverElementId = null;
        }
    }

    public static void UpdateHoverElement(string? buildUri, string? elementId)
    {
        lock (Sync)
        {
            _hoverBuildUri = string.IsNullOrWhiteSpace(buildUri) ? null : buildUri.Trim();
            _hoverElementId = string.IsNullOrWhiteSpace(elementId) ? null : elementId.Trim();
        }
    }

    public static void ClearHoverElement()
    {
        lock (Sync)
        {
            _hoverBuildUri = null;
            _hoverElementId = null;
        }
    }

    internal static bool TryGetCurrentDocument(out Control? rootControl, out string? buildUri, out string? sourcePath, out string? xamlText)
    {
        lock (Sync)
        {
            rootControl = _currentRootControl;
            buildUri = _currentBuildUri;
            sourcePath = _currentSourcePath;
            xamlText = _currentXamlText;
            return rootControl is not null && !string.IsNullOrWhiteSpace(buildUri);
        }
    }

    internal static bool TryGetHoverElement(out string? buildUri, out string? elementId)
    {
        lock (Sync)
        {
            buildUri = _hoverBuildUri;
            elementId = _hoverElementId;
            return !string.IsNullOrWhiteSpace(buildUri) && !string.IsNullOrWhiteSpace(elementId);
        }
    }

    private static SourceGenHotDesignArtifactKind InferArtifactKind(object root)
    {
        return root switch
        {
            global::Avalonia.Application => SourceGenHotDesignArtifactKind.Application,
            ResourceDictionary => SourceGenHotDesignArtifactKind.ResourceDictionary,
            ControlTheme => SourceGenHotDesignArtifactKind.ControlTheme,
            IDataTemplate => SourceGenHotDesignArtifactKind.Template,
            IStyle => SourceGenHotDesignArtifactKind.Style,
            _ => SourceGenHotDesignArtifactKind.View
        };
    }

    private static SourceGenHotDesignDocumentRole InferDocumentRole(object root)
    {
        return root switch
        {
            global::Avalonia.Application => SourceGenHotDesignDocumentRole.Root,
            ResourceDictionary => SourceGenHotDesignDocumentRole.Resources,
            ControlTheme => SourceGenHotDesignDocumentRole.Theme,
            IDataTemplate => SourceGenHotDesignDocumentRole.Template,
            IStyle => SourceGenHotDesignDocumentRole.Theme,
            _ => SourceGenHotDesignDocumentRole.Root
        };
    }
}
