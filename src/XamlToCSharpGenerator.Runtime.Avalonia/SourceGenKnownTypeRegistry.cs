using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Styling;

namespace XamlToCSharpGenerator.Runtime;

public static class SourceGenKnownTypeRegistry
{
    private const string AvaloniaDefaultXmlNamespace = "https://github.com/avaloniaui";
    private static readonly object Sync = new();
    private static readonly Dictionary<string, List<Type>> TypesByFullName = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<Type>> TypesBySimpleName = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, HashSet<string>> XmlNamespaceToClrNamespaces = new(StringComparer.Ordinal);
    private static readonly string[] AvaloniaDefaultNamespaceCandidates =
    [
        "Avalonia.Controls",
        "Avalonia.Controls.Primitives",
        "Avalonia.Controls.Presenters",
        "Avalonia.Controls.Shapes",
        "Avalonia.Controls.Documents",
        "Avalonia.Controls.Chrome",
        "Avalonia.Controls.Embedding",
        "Avalonia.Controls.Notifications",
        "Avalonia.Controls.Converters",
        "Avalonia.Markup.Xaml.Templates",
        "Avalonia.Markup.Xaml.Styling",
        "Avalonia.Markup.Xaml.MarkupExtensions",
        "Avalonia.Styling",
        "Avalonia.Controls.Templates",
        "Avalonia.Input",
        "Avalonia.Automation",
        "Avalonia.Dialogs",
        "Avalonia.Dialogs.Internal",
        "Avalonia.Layout",
        "Avalonia.Media",
        "Avalonia.Media.Transformation",
        "Avalonia.Media.Imaging",
        "Avalonia.Animation",
        "Avalonia.Animation.Easings",
        "Avalonia"
    ];

    static SourceGenKnownTypeRegistry()
    {
        RegisterXmlnsDefinition(AvaloniaDefaultXmlNamespace, "Avalonia");
        RegisterXmlnsDefinition(AvaloniaDefaultXmlNamespace, "XamlToCSharpGenerator.Runtime.Markup");
        for (var index = 0; index < AvaloniaDefaultNamespaceCandidates.Length; index++)
        {
            RegisterXmlnsDefinition(AvaloniaDefaultXmlNamespace, AvaloniaDefaultNamespaceCandidates[index]);
        }

        SeedBuiltInAvaloniaTypes();
        RegisterTypes(typeof(Markup.CSharp), typeof(Markup.CSharpExtension), typeof(CSharp), typeof(CSharpExtension));
    }

    public static void RegisterType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        lock (Sync)
        {
            AddType(type);
        }
    }

    public static void RegisterTypes(params Type[]? types)
    {
        if (types is null || types.Length == 0)
        {
            return;
        }

        lock (Sync)
        {
            for (var index = 0; index < types.Length; index++)
            {
                var type = types[index];
                if (type is null)
                {
                    continue;
                }

                AddType(type);
            }
        }
    }

    public static void RegisterXmlnsDefinition(string xmlNamespace, string clrNamespace)
    {
        if (string.IsNullOrWhiteSpace(xmlNamespace) ||
            string.IsNullOrWhiteSpace(clrNamespace))
        {
            return;
        }

        var normalizedXmlNamespace = xmlNamespace.Trim();
        var normalizedClrNamespace = clrNamespace.Trim();
        lock (Sync)
        {
            if (!XmlNamespaceToClrNamespaces.TryGetValue(normalizedXmlNamespace, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                XmlNamespaceToClrNamespaces[normalizedXmlNamespace] = set;
            }

            set.Add(normalizedClrNamespace);
        }
    }

    public static bool TryResolve(string? xmlNamespace, string typeName, out Type? resolvedType)
    {
        resolvedType = null;
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        var normalizedTypeName = NormalizeTypeName(typeName);
        if (normalizedTypeName.Length == 0)
        {
            return false;
        }

        var normalizedXmlNamespace = xmlNamespace?.Trim();
        lock (Sync)
        {
            if (normalizedTypeName.Contains('.', StringComparison.Ordinal) &&
                TryResolveByFullName(normalizedTypeName, assemblyName: null, out resolvedType))
            {
                return true;
            }

            if (TryTryResolveClrNamespaceMatch(normalizedXmlNamespace, normalizedTypeName, out resolvedType))
            {
                return true;
            }

            if (normalizedXmlNamespace is not null &&
                XmlNamespaceToClrNamespaces.TryGetValue(normalizedXmlNamespace, out var clrNamespaces))
            {
                foreach (var clrNamespace in clrNamespaces)
                {
                    if (TryResolveByFullName(clrNamespace + "." + normalizedTypeName, assemblyName: null, out resolvedType))
                    {
                        return true;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(normalizedXmlNamespace) ||
                string.Equals(normalizedXmlNamespace, AvaloniaDefaultXmlNamespace, StringComparison.OrdinalIgnoreCase))
            {
                for (var index = 0; index < AvaloniaDefaultNamespaceCandidates.Length; index++)
                {
                    if (TryResolveByFullName(
                            AvaloniaDefaultNamespaceCandidates[index] + "." + normalizedTypeName,
                            assemblyName: null,
                            out resolvedType))
                    {
                        return true;
                    }
                }
            }

            if (TypesBySimpleName.TryGetValue(normalizedTypeName, out var candidates))
            {
                resolvedType = PickBestCandidate(candidates, assemblyName: null);
                return resolvedType is not null;
            }
        }

        return false;
    }

    public static IReadOnlyList<Type> GetRegisteredTypes()
    {
        lock (Sync)
        {
            return TypesByFullName
                .SelectMany(static pair => pair.Value)
                .Distinct()
                .OrderBy(static type => type.FullName ?? type.Name, StringComparer.Ordinal)
                .ToArray();
        }
    }

    private static bool TryTryResolveClrNamespaceMatch(string? xmlNamespace, string typeName, out Type? resolvedType)
    {
        resolvedType = null;
        if (string.IsNullOrWhiteSpace(xmlNamespace))
        {
            return false;
        }

        string? clrNamespace = null;
        string? assemblyName = null;
        if (xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            ParseClrNamespace(xmlNamespace, out clrNamespace, out assemblyName);
        }
        else if (xmlNamespace.StartsWith("using:", StringComparison.Ordinal))
        {
            clrNamespace = xmlNamespace["using:".Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(clrNamespace))
        {
            return false;
        }

        return TryResolveByFullName(clrNamespace + "." + typeName, assemblyName, out resolvedType);
    }

    private static void ParseClrNamespace(string namespaceUri, out string? clrNamespace, out string? assemblyName)
    {
        clrNamespace = null;
        assemblyName = null;

        var payload = namespaceUri["clr-namespace:".Length..];
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        foreach (var segment in payload.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = segment.Trim();
            if (trimmed.StartsWith("assembly=", StringComparison.OrdinalIgnoreCase))
            {
                assemblyName = trimmed["assembly=".Length..].Trim();
                continue;
            }

            if (clrNamespace is null)
            {
                clrNamespace = trimmed;
            }
        }
    }

    private static bool TryResolveByFullName(string fullName, string? assemblyName, out Type? resolvedType)
    {
        resolvedType = null;
        if (!TypesByFullName.TryGetValue(fullName, out var candidates))
        {
            return false;
        }

        resolvedType = PickBestCandidate(candidates, assemblyName);
        return resolvedType is not null;
    }

    private static Type? PickBestCandidate(IReadOnlyList<Type> candidates, string? assemblyName)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                if (string.Equals(candidate.Assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        return candidates
            .OrderBy(static candidate => candidate.FullName ?? candidate.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static void AddType(Type type)
    {
        var fullName = type.FullName;
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            AddTypeCandidate(TypesByFullName, fullName, type);
        }

        var simpleName = type.Name;
        if (!string.IsNullOrWhiteSpace(simpleName))
        {
            AddTypeCandidate(TypesBySimpleName, StripGenericArity(simpleName), type);
        }

        if (!string.IsNullOrWhiteSpace(type.Namespace))
        {
            var namespaceQualifiedName = type.Namespace + "." + StripGenericArity(type.Name);
            AddTypeCandidate(TypesByFullName, namespaceQualifiedName, type);
        }
    }

    private static void AddTypeCandidate(Dictionary<string, List<Type>> index, string key, Type type)
    {
        if (!index.TryGetValue(key, out var list))
        {
            list = [];
            index[key] = list;
        }

        if (!list.Contains(type))
        {
            list.Add(type);
        }
    }

    private static string NormalizeTypeName(string typeName)
    {
        var normalized = typeName.Trim();
        if (normalized.StartsWith("global::", StringComparison.Ordinal))
        {
            normalized = normalized["global::".Length..];
        }

        return StripGenericArity(normalized);
    }

    private static string StripGenericArity(string value)
    {
        var tickIndex = value.IndexOf('`');
        return tickIndex > 0
            ? value[..tickIndex]
            : value;
    }

    private static void SeedBuiltInAvaloniaTypes()
    {
        var builtInTypes = new[]
        {
            typeof(AvaloniaObject),
            typeof(Visual),
            typeof(Layoutable),
            typeof(StyledElement),
            typeof(Control),
            typeof(TemplatedControl),
            typeof(ContentControl),
            typeof(ContentPresenter),
            typeof(Window),
            typeof(TextBlock),
            typeof(TextBox),
            typeof(Button),
            typeof(ToggleButton),
            typeof(Border),
            typeof(Grid),
            typeof(StackPanel),
            typeof(DockPanel),
            typeof(Canvas),
            typeof(ScrollViewer),
            typeof(ItemsControl),
            typeof(ListBox),
            typeof(ComboBox),
            typeof(TabControl),
            typeof(TreeView),
            typeof(ProgressBar),
            typeof(Slider),
            typeof(Rectangle),
            typeof(Ellipse),
            typeof(Avalonia.Controls.Shapes.Path),
            typeof(Style),
            typeof(Setter),
            typeof(ControlTheme)
        };

        for (var index = 0; index < builtInTypes.Length; index++)
        {
            RegisterType(builtInTypes[index]);
        }
    }

    private static void SeedAvaloniaPropertyOwnerTypes()
    {
        var rootTypes = new[]
        {
            typeof(AvaloniaObject),
            typeof(StyledElement),
            typeof(Control),
            typeof(TemplatedControl),
            typeof(ItemsControl),
            typeof(ScrollViewer),
            typeof(Window)
        };

        for (var rootIndex = 0; rootIndex < rootTypes.Length; rootIndex++)
        {
            var rootType = rootTypes[rootIndex];
            foreach (var property in AvaloniaPropertyRegistry.Instance.GetRegistered(rootType))
            {
                RegisterType(property.OwnerType);
            }

            foreach (var property in AvaloniaPropertyRegistry.Instance.GetRegisteredAttached(rootType))
            {
                RegisterType(property.OwnerType);
            }
        }
    }
}
