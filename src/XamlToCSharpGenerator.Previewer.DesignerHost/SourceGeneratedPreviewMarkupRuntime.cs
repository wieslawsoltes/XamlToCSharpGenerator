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
    private static readonly ConcurrentDictionary<string, Func<object?, object?, object?, object?>> EvaluatorCache = new(StringComparer.Ordinal);
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

        var evaluator = EvaluatorCache.GetOrAdd(resolvedCode, CompileEvaluator);
        var dependencyNames = ResolveDependencyNames(dependencyNamesBase64Url);
        if (ShouldReturnBinding(targetProperty))
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

    private static bool ShouldReturnBinding(object? targetProperty)
    {
        if (targetProperty is AvaloniaProperty)
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

    private static Func<object?, object?, object?, object?> CompileEvaluator(string code)
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
        var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
        var method = assembly.GetType(className, throwOnError: true)
            ?.GetMethod("Evaluate", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("Preview evaluator method was not emitted.");
        return (Func<object?, object?, object?, object?>)Delegate.CreateDelegate(
            typeof(Func<object?, object?, object?, object?>),
            method);
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
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < assemblies.Length; index++)
        {
            var assembly = assemblies[index];
            if (assembly.IsDynamic ||
                string.IsNullOrWhiteSpace(assembly.Location) ||
                !File.Exists(assembly.Location) ||
                !seenPaths.Add(assembly.Location))
            {
                continue;
            }

            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        return references.ToArray();
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
}
