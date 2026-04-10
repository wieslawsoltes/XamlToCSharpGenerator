using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class FrameworkEventBindingMethodEmissionService
{
    private readonly IXamlFrameworkEventBindingEmitterAdapter _adapter;
    private readonly EventBindingEmissionService _eventBindingEmissionService;

    public FrameworkEventBindingMethodEmissionService(
        IXamlFrameworkEventBindingEmitterAdapter adapter,
        EventBindingEmissionService eventBindingEmissionService)
    {
        _adapter = adapter;
        _eventBindingEmissionService = eventBindingEmissionService;
    }

    public void EmitMethods(
        StringBuilder sourceBuilder,
        ImmutableArray<ResolvedEventBindingDefinition> eventBindingDefinitions,
        IReadOnlyDictionary<string, string> emittedEventBindingMethodNames)
    {
        for (var index = 0; index < eventBindingDefinitions.Length; index++)
        {
            var definition = eventBindingDefinitions[index];
            var emittedMethodName = emittedEventBindingMethodNames.TryGetValue(definition.GeneratedMethodName, out var mappedName)
                ? mappedName
                : definition.GeneratedMethodName;
            _adapter.EmitMethod(sourceBuilder, definition, emittedMethodName);
        }
    }
}
