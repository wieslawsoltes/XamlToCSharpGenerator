using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XamlToCSharpGenerator.Runtime;

public static class XamlSourceGenHotDesignTool
{
    public static void Enable(SourceGenHotDesignOptions? options = null)
    {
        XamlSourceGenHotDesignManager.Enable(options);
    }

    public static void Disable()
    {
        XamlSourceGenHotDesignManager.Disable();
    }

    public static bool Toggle()
    {
        return XamlSourceGenHotDesignManager.Toggle();
    }

    public static SourceGenHotDesignStatus GetStatus()
    {
        return XamlSourceGenHotDesignManager.GetStatus();
    }

    public static IReadOnlyList<SourceGenHotDesignDocumentDescriptor> ListDocuments()
    {
        return XamlSourceGenHotDesignManager.GetRegisteredDocuments();
    }

    public static SourceGenHotDesignWorkspaceSnapshot GetWorkspaceSnapshot(string? buildUri = null, string? search = null)
    {
        return XamlSourceGenHotDesignCoreTools.GetWorkspaceSnapshot(buildUri, search);
    }

    public static void SetWorkspaceMode(SourceGenHotDesignWorkspaceMode mode)
    {
        XamlSourceGenHotDesignCoreTools.SetWorkspaceMode(mode);
    }

    public static void SetPropertyFilterMode(SourceGenHotDesignPropertyFilterMode mode)
    {
        XamlSourceGenHotDesignCoreTools.SetPropertyFilterMode(mode);
    }

    public static bool TogglePanel(SourceGenHotDesignPanelKind panel)
    {
        return XamlSourceGenHotDesignCoreTools.TogglePanel(panel);
    }

    public static void SetPanelVisibility(SourceGenHotDesignPanelKind panel, bool visible)
    {
        XamlSourceGenHotDesignCoreTools.SetPanelVisibility(panel, visible);
    }

    public static void SetCanvasZoom(double zoom)
    {
        XamlSourceGenHotDesignCoreTools.SetCanvasZoom(zoom);
    }

    public static void SetCanvasFormFactor(string formFactor, double? width = null, double? height = null)
    {
        XamlSourceGenHotDesignCoreTools.SetCanvasFormFactor(formFactor, width, height);
    }

    public static void SetCanvasTheme(bool darkTheme)
    {
        XamlSourceGenHotDesignCoreTools.SetCanvasTheme(darkTheme);
    }

    public static void SelectDocument(string? buildUri)
    {
        XamlSourceGenHotDesignCoreTools.SelectDocument(buildUri);
    }

    public static void SelectElement(string? buildUri, string? elementId)
    {
        XamlSourceGenHotDesignCoreTools.SelectElement(buildUri, elementId);
    }

    public static bool TryResolveElementForLiveSelection(
        IReadOnlyList<string>? controlNames,
        IReadOnlyList<string>? controlTypeNames,
        out string? buildUri,
        out string? elementId)
    {
        return XamlSourceGenHotDesignCoreTools.TryResolveElementForLiveSelection(
            controlNames,
            controlTypeNames,
            out buildUri,
            out elementId);
    }

    public static ValueTask<SourceGenHotDesignApplyResult> ApplyDocumentTextAsync(
        string buildUri,
        string xamlText,
        CancellationToken cancellationToken = default)
    {
        return XamlSourceGenHotDesignCoreTools.ApplyDocumentTextAsync(buildUri, xamlText, cancellationToken);
    }

    public static ValueTask<SourceGenHotDesignApplyResult> ApplyPropertyUpdateAsync(
        SourceGenHotDesignPropertyUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        return XamlSourceGenHotDesignCoreTools.ApplyPropertyUpdateAsync(request, cancellationToken);
    }

    public static ValueTask<SourceGenHotDesignApplyResult> InsertElementAsync(
        SourceGenHotDesignElementInsertRequest request,
        CancellationToken cancellationToken = default)
    {
        return XamlSourceGenHotDesignCoreTools.InsertElementAsync(request, cancellationToken);
    }

    public static ValueTask<SourceGenHotDesignApplyResult> RemoveElementAsync(
        SourceGenHotDesignElementRemoveRequest request,
        CancellationToken cancellationToken = default)
    {
        return XamlSourceGenHotDesignCoreTools.RemoveElementAsync(request, cancellationToken);
    }

    public static ValueTask<SourceGenHotDesignApplyResult> UndoAsync(string? buildUri = null, CancellationToken cancellationToken = default)
    {
        return XamlSourceGenHotDesignCoreTools.UndoAsync(buildUri, cancellationToken);
    }

    public static ValueTask<SourceGenHotDesignApplyResult> RedoAsync(string? buildUri = null, CancellationToken cancellationToken = default)
    {
        return XamlSourceGenHotDesignCoreTools.RedoAsync(buildUri, cancellationToken);
    }

    public static ValueTask<SourceGenHotDesignApplyResult> ApplyUpdateAsync(
        SourceGenHotDesignUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        return XamlSourceGenHotDesignManager.ApplyUpdateAsync(request, cancellationToken);
    }

    public static SourceGenHotDesignApplyResult ApplyUpdate(
        SourceGenHotDesignUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        return XamlSourceGenHotDesignManager.ApplyUpdate(request, cancellationToken);
    }

    public static ValueTask<SourceGenHotDesignApplyResult> ApplyUpdateByUriAsync(
        string buildUri,
        string xamlText,
        CancellationToken cancellationToken = default)
    {
        return ApplyUpdateAsync(new SourceGenHotDesignUpdateRequest
        {
            BuildUri = buildUri,
            XamlText = xamlText
        }, cancellationToken);
    }

    public static ValueTask<SourceGenHotDesignApplyResult> ApplyUpdateByTypeAsync(
        string targetTypeName,
        string xamlText,
        CancellationToken cancellationToken = default)
    {
        return ApplyUpdateAsync(new SourceGenHotDesignUpdateRequest
        {
            TargetTypeName = targetTypeName,
            XamlText = xamlText
        }, cancellationToken);
    }

    public static string ExecuteCommand(string command, string? argument1 = null, string? argument2 = null)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return "Command is required.";
        }

        var normalized = command.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "enable":
                Enable();
                return "Hot design mode enabled.";
            case "disable":
                Disable();
                return "Hot design mode disabled.";
            case "toggle":
                return Toggle()
                    ? "Hot design mode enabled."
                    : "Hot design mode disabled.";
            case "status":
            {
                var status = GetStatus();
                return "Enabled=" + status.IsEnabled +
                       ", Documents=" + status.RegisteredDocumentCount +
                       ", Appliers=" + status.RegisteredApplierCount + ".";
            }
            case "list":
            {
                var documents = ListDocuments();
                if (documents.Count == 0)
                {
                    return "No hot design documents are registered.";
                }

                var builder = new StringBuilder();
                for (var index = 0; index < documents.Count; index++)
                {
                    var doc = documents[index];
                    builder.Append(index + 1)
                        .Append(". ")
                        .Append(doc.BuildUri)
                        .Append(" [")
                        .Append(doc.RootType.FullName)
                        .Append("] instances=")
                        .Append(doc.LiveInstanceCount);
                    if (!string.IsNullOrWhiteSpace(doc.SourcePath))
                    {
                        builder.Append(" source=").Append(doc.SourcePath);
                    }

                    if (index < documents.Count - 1)
                    {
                        builder.AppendLine();
                    }
                }

                return builder.ToString();
            }
            case "snapshot":
            {
                var snapshot = GetWorkspaceSnapshot(argument1, argument2);
                return "Mode=" + snapshot.Mode +
                       ", Active=" + (snapshot.ActiveBuildUri ?? "<none>") +
                       ", Element=" + (snapshot.SelectedElementId ?? "<none>") +
                       ", Elements=" + snapshot.Elements.Count +
                       ", Properties=" + snapshot.Properties.Count +
                       ", Undo=" + snapshot.CanUndo +
                       ", Redo=" + snapshot.CanRedo + ".";
            }
            case "set-mode":
            {
                if (!TryParseWorkspaceMode(argument1, out var mode))
                {
                    return "Usage: set-mode <agent|design|interactive>";
                }

                SetWorkspaceMode(mode);
                return "Workspace mode set to " + mode + ".";
            }
            case "set-property-mode":
            {
                if (!TryParsePropertyFilterMode(argument1, out var mode))
                {
                    return "Usage: set-property-mode <smart|all>";
                }

                SetPropertyFilterMode(mode);
                return "Property mode set to " + mode + ".";
            }
            case "panel-toggle":
            {
                if (!TryParsePanelKind(argument1, out var panel))
                {
                    return "Usage: panel-toggle <toolbar|elements|toolbox|canvas|properties>";
                }

                var visible = TogglePanel(panel);
                return panel + " visible=" + visible + ".";
            }
            case "set-zoom":
            {
                if (!double.TryParse(argument1, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom))
                {
                    return "Usage: set-zoom <number>";
                }

                SetCanvasZoom(zoom);
                return "Canvas zoom set to " + zoom.ToString("0.##", CultureInfo.InvariantCulture) + ".";
            }
            case "select-doc":
            {
                if (string.IsNullOrWhiteSpace(argument1))
                {
                    return "Usage: select-doc <buildUri>";
                }

                SelectDocument(argument1);
                return "Selected document " + argument1 + ".";
            }
            case "select-element":
            {
                SelectElement(argument1, argument2);
                return "Selected element " + (argument2 ?? "<root>") + " for " + (argument1 ?? "<active>") + ".";
            }
            case "apply-doc":
            {
                if (string.IsNullOrWhiteSpace(argument1) || argument2 is null)
                {
                    return "Usage: apply-doc <buildUri> <xamlText>";
                }

                var result = ApplyDocumentTextAsync(argument1, argument2).GetAwaiter().GetResult();
                return result.Succeeded ? "Applied: " + result.Message : "Failed: " + result.Message;
            }
            case "set-property":
            {
                if (string.IsNullOrWhiteSpace(argument1) || string.IsNullOrWhiteSpace(argument2))
                {
                    return "Usage: set-property <propertyName> <value>";
                }

                var snapshot = GetWorkspaceSnapshot();
                if (string.IsNullOrWhiteSpace(snapshot.ActiveBuildUri))
                {
                    return "No active document is selected.";
                }

                var result = ApplyPropertyUpdateAsync(new SourceGenHotDesignPropertyUpdateRequest
                {
                    BuildUri = snapshot.ActiveBuildUri,
                    ElementId = snapshot.SelectedElementId,
                    PropertyName = argument1,
                    PropertyValue = argument2
                }).GetAwaiter().GetResult();

                return result.Succeeded ? "Applied: " + result.Message : "Failed: " + result.Message;
            }
            case "undo":
            {
                var result = UndoAsync(argument1).GetAwaiter().GetResult();
                return result.Succeeded ? "Undo: " + result.Message : "Failed: " + result.Message;
            }
            case "redo":
            {
                var result = RedoAsync(argument1).GetAwaiter().GetResult();
                return result.Succeeded ? "Redo: " + result.Message : "Failed: " + result.Message;
            }
            case "apply-uri":
            {
                if (string.IsNullOrWhiteSpace(argument1) || argument2 is null)
                {
                    return "Usage: apply-uri <buildUri> <xamlText>";
                }

                var result = ApplyUpdate(new SourceGenHotDesignUpdateRequest
                {
                    BuildUri = argument1,
                    XamlText = argument2
                });
                return result.Succeeded ? "Applied: " + result.Message : "Failed: " + result.Message;
            }
            case "apply-type":
            {
                if (string.IsNullOrWhiteSpace(argument1) || argument2 is null)
                {
                    return "Usage: apply-type <fullTypeName> <xamlText>";
                }

                var result = ApplyUpdate(new SourceGenHotDesignUpdateRequest
                {
                    TargetTypeName = argument1,
                    XamlText = argument2
                });
                return result.Succeeded ? "Applied: " + result.Message : "Failed: " + result.Message;
            }
            default:
                return "Unknown command '" + command + "'. Supported commands: enable, disable, toggle, status, list, snapshot, set-mode, set-property-mode, panel-toggle, set-zoom, select-doc, select-element, apply-doc, set-property, undo, redo, apply-uri, apply-type.";
        }
    }

    private static bool TryParseWorkspaceMode(string? value, out SourceGenHotDesignWorkspaceMode mode)
    {
        mode = SourceGenHotDesignWorkspaceMode.Design;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Enum.TryParse(value.Trim(), true, out mode);
    }

    private static bool TryParsePropertyFilterMode(string? value, out SourceGenHotDesignPropertyFilterMode mode)
    {
        mode = SourceGenHotDesignPropertyFilterMode.Smart;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Enum.TryParse(value.Trim(), true, out mode);
    }

    private static bool TryParsePanelKind(string? value, out SourceGenHotDesignPanelKind panel)
    {
        panel = SourceGenHotDesignPanelKind.Toolbar;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Enum.TryParse(value.Trim(), true, out panel);
    }
}
