using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Abstractions;

public interface IXamlFrameworkEventSubscriptionEmitterAdapter
{
    ImmutableArray<string> BuildSubscriptionStatements(
        string nodeReference,
        string rootReference,
        string emittedMethodName,
        ResolvedEventSubscription eventSubscription);
}
