using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using global::Avalonia;
using global::Avalonia.Data;
using global::Avalonia.Data.Converters;
using global::Avalonia.Markup.Xaml;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal sealed class SourceGeneratedPreviewMarkupRuntime
{
    internal const int MaxCachedEvaluatorCount = 128;

    private static readonly object EvaluatorCacheGate = new();
    private static readonly Dictionary<string, EvaluatorCacheEntry> EvaluatorCache = new(StringComparer.Ordinal);
    private static readonly LinkedList<string> EvaluatorCacheLru = new();
    private static readonly ConcurrentDictionary<Type, Delegate> NoOpDelegateCache = new();
    private static readonly Lazy<PortableExecutableReference[]> MetadataReferences = new(CreateMetadataReferences);

    public object? ProvideValue(
        string? code,
        string? codeBase64Url,
        string? dependencyNamesBase64Url,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var resolvedCode = ResolveCode(code, codeBase64Url);
        if (string.IsNullOrWhiteSpace(resolvedCode))
        {
            return null;
        }

        var provideValueTarget = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        var rootObject = (serviceProvider.GetService(typeof(IRootObjectProvider)) as IRootObjectProvider)?.RootObject;
        var targetObject = provideValueTarget?.TargetObject;
        var targetProperty = provideValueTarget?.TargetProperty;

        if (TryResolveDelegateType(targetProperty, out var delegateType))
        {
            return CreateNoOpDelegate(delegateType);
        }

        var evaluator = GetOrCreateEvaluator(resolvedCode);
        var dependencyNames = ResolveDependencyNames(dependencyNamesBase64Url);
        if (ShouldReturnBinding(targetObject, targetProperty))
        {
            return CreateBinding(evaluator, dependencyNames, rootObject, targetObject);
        }

        var targetType = ResolveTargetType(targetProperty);
        return CoerceEvaluatedValue(
            EvaluateOnce(evaluator, rootObject, targetObject),
            targetType,
            CultureInfo.CurrentCulture);
    }

    private static string ResolveCode(string? code, string? codeBase64Url)
    {
        if (!string.IsNullOrWhiteSpace(codeBase64Url))
        {
            return PreviewMarkupValueCodec.DecodeBase64Url(codeBase64Url.Trim());
        }

        return code?.Trim() ?? string.Empty;
    }

    private static IReadOnlyList<string> ResolveDependencyNames(string? dependencyNamesBase64Url)
    {
        if (string.IsNullOrWhiteSpace(dependencyNamesBase64Url))
        {
            return Array.Empty<string>();
        }

        var decoded = PreviewMarkupValueCodec.DecodeBase64Url(dependencyNamesBase64Url.Trim());
        if (string.IsNullOrWhiteSpace(decoded))
        {
            return Array.Empty<string>();
        }

        return decoded
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ShouldReturnBinding(object? targetObject, object? targetProperty)
    {
        if (TryResolveAvaloniaProperty(targetObject, targetProperty, out _))
        {
            return true;
        }

        var targetType = ResolveTargetType(targetProperty);
        if (targetType is null)
        {
            return false;
        }

        return targetType == typeof(object) || typeof(IBinding).IsAssignableFrom(targetType);
    }

    private static bool TryResolveAvaloniaProperty(
        object? targetObject,
        object? targetProperty,
        out AvaloniaProperty? avaloniaProperty)
    {
        if (targetProperty is AvaloniaProperty directAvaloniaProperty)
        {
            avaloniaProperty = directAvaloniaProperty;
            return true;
        }

        avaloniaProperty = null;
        if (targetObject is null)
        {
            return false;
        }

        var propertyName = ResolveTargetPropertyName(targetProperty);
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        var targetType = targetObject.GetType();
        var avaloniaPropertyMemberName = propertyName + "Property";

        var propertyField = targetType.GetField(
            avaloniaPropertyMemberName,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (typeof(AvaloniaProperty).IsAssignableFrom(propertyField?.FieldType))
        {
            avaloniaProperty = propertyField?.GetValue(null) as AvaloniaProperty;
            if (avaloniaProperty is not null)
            {
                return true;
            }
        }

        var propertyValue = targetType.GetProperty(
            avaloniaPropertyMemberName,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (typeof(AvaloniaProperty).IsAssignableFrom(propertyValue?.PropertyType))
        {
            avaloniaProperty = propertyValue?.GetValue(null) as AvaloniaProperty;
            if (avaloniaProperty is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static Type? ResolveTargetType(object? targetProperty)
    {
        return targetProperty switch
        {
            AvaloniaProperty avaloniaProperty => avaloniaProperty.PropertyType,
            PropertyInfo propertyInfo => propertyInfo.PropertyType,
            EventInfo eventInfo => eventInfo.EventHandlerType,
            _ => TryResolveTypeByReflection(targetProperty)
        };
    }

    private static bool TryResolveDelegateType(object? targetProperty, out Type delegateType)
    {
        delegateType = ResolveTargetType(targetProperty) ?? typeof(void);
        return typeof(Delegate).IsAssignableFrom(delegateType);
    }

    private static Type? TryResolveTypeByReflection(object? targetProperty)
    {
        if (targetProperty is null)
        {
            return null;
        }

        var targetPropertyType = targetProperty.GetType();
        var eventHandlerTypeProperty = targetPropertyType.GetProperty(
            "EventHandlerType",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (eventHandlerTypeProperty?.PropertyType == typeof(Type))
        {
            return eventHandlerTypeProperty.GetValue(targetProperty) as Type;
        }

        var propertyTypeProperty = targetPropertyType.GetProperty(
            "PropertyType",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (propertyTypeProperty?.PropertyType == typeof(Type))
        {
            return propertyTypeProperty.GetValue(targetProperty) as Type;
        }

        return null;
    }

    private static string? ResolveTargetPropertyName(object? targetProperty)
    {
        return targetProperty switch
        {
            AvaloniaProperty avaloniaProperty => avaloniaProperty.Name,
            PropertyInfo propertyInfo => propertyInfo.Name,
            EventInfo eventInfo => eventInfo.Name,
            _ => TryResolveTargetPropertyNameByReflection(targetProperty)
        };
    }

    private static string? TryResolveTargetPropertyNameByReflection(object? targetProperty)
    {
        if (targetProperty is null)
        {
            return null;
        }

        var nameProperty = targetProperty.GetType().GetProperty(
            "Name",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return nameProperty?.PropertyType == typeof(string)
            ? nameProperty.GetValue(targetProperty) as string
            : null;
    }

    private static IBinding CreateBinding(
        Func<object?, object?, object?, object?> evaluator,
        IReadOnlyList<string> dependencyNames,
        object? rootObject,
        object? targetObject)
    {
        var multiBinding = new MultiBinding
        {
            Converter = new PreviewDynamicBindingConverter(evaluator, rootObject, targetObject),
            Mode = BindingMode.OneWay
        };

        multiBinding.Bindings.Add(new Binding("."));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < dependencyNames.Count; index++)
        {
            var dependencyName = dependencyNames[index];
            if (string.IsNullOrWhiteSpace(dependencyName) || !seen.Add(dependencyName))
            {
                continue;
            }

            multiBinding.Bindings.Add(new Binding(dependencyName));
        }

        return multiBinding;
    }

    private static object? EvaluateOnce(
        Func<object?, object?, object?, object?> evaluator,
        object? rootObject,
        object? targetObject)
    {
        object? sourceObject = null;
        if (targetObject is StyledElement styledElement)
        {
            sourceObject = styledElement.DataContext;
        }
        else if (rootObject is StyledElement styledRoot)
        {
            sourceObject = styledRoot.DataContext;
        }

        return evaluator(sourceObject, rootObject, targetObject);
    }

    private static object? CoerceEvaluatedValue(object? value, Type? targetType, CultureInfo culture)
    {
        if (targetType is null || value is null || targetType == typeof(object))
        {
            return value;
        }

        var effectiveTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (effectiveTargetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (effectiveTargetType == typeof(string))
        {
            return Convert.ToString(value, culture);
        }

        if (effectiveTargetType.IsEnum && value is string enumText)
        {
            try
            {
                return Enum.Parse(effectiveTargetType, enumText, ignoreCase: true);
            }
            catch
            {
                return value;
            }
        }

        if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(effectiveTargetType))
        {
            try
            {
                return Convert.ChangeType(value, effectiveTargetType, culture);
            }
            catch
            {
                return value;
            }
        }

        return value;
    }

    private static Delegate CreateNoOpDelegate(Type delegateType)
    {
        return NoOpDelegateCache.GetOrAdd(delegateType, static type =>
        {
            var invokeMethod = type.GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("Delegate type '" + type.FullName + "' does not expose Invoke().");
            var parameters = invokeMethod.GetParameters()
                .Select(static parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
                .ToArray();
            Expression body = invokeMethod.ReturnType == typeof(void)
                ? Expression.Empty()
                : Expression.Default(invokeMethod.ReturnType);
            return Expression.Lambda(type, body, parameters).Compile();
        });
    }

    internal static void ClearEvaluatorCacheForTests()
    {
        List<EvaluatorCacheEntry> entries;
        lock (EvaluatorCacheGate)
        {
            entries = [.. EvaluatorCache.Values];
            EvaluatorCache.Clear();
            EvaluatorCacheLru.Clear();
        }

        for (var index = 0; index < entries.Count; index++)
        {
            entries[index].Dispose();
        }
    }

    internal static int GetCachedEvaluatorCountForTests()
    {
        lock (EvaluatorCacheGate)
        {
            return EvaluatorCache.Count;
        }
    }

    internal static AssemblyLoadContext? GetEvaluatorLoadContextForTests(string code)
    {
        return AssemblyLoadContext.GetLoadContext(GetOrCreateEvaluator(code).Method.Module.Assembly);
    }

    private static Func<object?, object?, object?, object?> GetOrCreateEvaluator(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        lock (EvaluatorCacheGate)
        {
            if (EvaluatorCache.TryGetValue(code, out var cachedEntry))
            {
                TouchEvaluatorEntryNoLock(cachedEntry);
                return cachedEntry.Evaluator;
            }
        }

        var createdEntry = CompileEvaluatorEntry(code);
        EvaluatorCacheEntry? entryToDispose = null;
        Func<object?, object?, object?, object?> evaluator;
        lock (EvaluatorCacheGate)
        {
            if (EvaluatorCache.TryGetValue(code, out var cachedEntry))
            {
                TouchEvaluatorEntryNoLock(cachedEntry);
                evaluator = cachedEntry.Evaluator;
                entryToDispose = createdEntry;
            }
            else
            {
                var node = EvaluatorCacheLru.AddLast(code);
                createdEntry.LruNode = node;
                EvaluatorCache.Add(code, createdEntry);
                evaluator = createdEntry.Evaluator;
                entryToDispose = TrimEvaluatorCacheNoLock();
            }
        }

        entryToDispose?.Dispose();
        return evaluator;
    }

    private static void TouchEvaluatorEntryNoLock(EvaluatorCacheEntry entry)
    {
        if (entry.LruNode is null || entry.LruNode.List is null || entry.LruNode == EvaluatorCacheLru.Last)
        {
            return;
        }

        EvaluatorCacheLru.Remove(entry.LruNode);
        EvaluatorCacheLru.AddLast(entry.LruNode);
    }

    private static EvaluatorCacheEntry? TrimEvaluatorCacheNoLock()
    {
        if (EvaluatorCache.Count <= MaxCachedEvaluatorCount || EvaluatorCacheLru.First is null)
        {
            return null;
        }

        var leastRecentlyUsedNode = EvaluatorCacheLru.First;
        EvaluatorCacheLru.RemoveFirst();
        if (!EvaluatorCache.Remove(leastRecentlyUsedNode.Value, out var entry))
        {
            return null;
        }

        entry.LruNode = null;
        return entry;
    }

    private static EvaluatorCacheEntry CompileEvaluatorEntry(string code)
    {
        var className = CreateEvaluatorClassName(code);
        var source = string.Join(
            Environment.NewLine,
            "#nullable enable",
            "using System;",
            "public static class " + className,
            "{",
            "    public static object? Evaluate(object? sourceObj, object? rootObj, object? targetObj)",
            "    {",
            "        dynamic source = sourceObj!;",
            "        dynamic root = rootObj!;",
            "        dynamic target = targetObj!;",
            "        return (object?)(" + code + ");",
            "    }",
            "}");

        var compilation = CSharpCompilation.Create(
            assemblyName: className,
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: MetadataReferences.Value,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
        {
            var diagnostic = emitResult.Diagnostics
                .FirstOrDefault(static candidate => candidate.Severity == DiagnosticSeverity.Error);
            throw new InvalidOperationException(
                "Preview CSharp evaluation compilation failed: " +
                (diagnostic?.GetMessage(CultureInfo.InvariantCulture) ?? "unknown error") +
                " Expression: " +
                code);
        }

        stream.Position = 0;
        var loadContext = new AssemblyLoadContext(className, isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromStream(stream);
            var method = assembly.GetType(className, throwOnError: true)
                ?.GetMethod("Evaluate", BindingFlags.Static | BindingFlags.Public)
                ?? throw new InvalidOperationException("Preview evaluator method was not emitted.");
            var evaluator = (Func<object?, object?, object?, object?>)Delegate.CreateDelegate(
                typeof(Func<object?, object?, object?, object?>),
                method);
            return new EvaluatorCacheEntry(evaluator, loadContext);
        }
        catch
        {
            loadContext.Unload();
            throw;
        }
    }

    internal static string CreateEvaluatorClassName(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return "__AXSGPreviewExpr_" + Convert.ToHexString(hashBytes[..8]);
    }

    private static PortableExecutableReference[] CreateMetadataReferences()
    {
        var references = new List<PortableExecutableReference>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddMetadataReference(references, seenPaths, typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < assemblies.Length; index++)
        {
            AddMetadataReference(references, seenPaths, assemblies[index]);
        }

        return references.ToArray();
    }

    private static void AddMetadataReference(
        ICollection<PortableExecutableReference> references,
        ISet<string> seenPaths,
        Assembly assembly)
    {
        if (assembly.IsDynamic ||
            string.IsNullOrWhiteSpace(assembly.Location) ||
            !File.Exists(assembly.Location) ||
            !seenPaths.Add(assembly.Location))
        {
            return;
        }

        references.Add(MetadataReference.CreateFromFile(assembly.Location));
    }

    private sealed class PreviewDynamicBindingConverter(
        Func<object?, object?, object?, object?> evaluator,
        object? rootObject,
        object? targetObject) : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var sourceObject = values.Count > 0 ? values[0] : null;

            try
            {
                return CoerceEvaluatedValue(
                    evaluator(sourceObject, rootObject, targetObject),
                    targetType,
                    culture);
            }
            catch
            {
                return null;
            }
        }
    }

    private sealed class EvaluatorCacheEntry(
        Func<object?, object?, object?, object?> evaluator,
        AssemblyLoadContext loadContext) : IDisposable
    {
        public Func<object?, object?, object?, object?> Evaluator { get; } = evaluator;

        public AssemblyLoadContext LoadContext { get; } = loadContext;

        public LinkedListNode<string>? LruNode { get; set; }

        public void Dispose()
        {
            LoadContext.Unload();
        }
    }
}
