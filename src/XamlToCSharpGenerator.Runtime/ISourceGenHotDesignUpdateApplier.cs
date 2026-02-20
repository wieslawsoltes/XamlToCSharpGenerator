using System.Threading;
using System.Threading.Tasks;

namespace XamlToCSharpGenerator.Runtime;

public interface ISourceGenHotDesignUpdateApplier
{
    int Priority { get; }

    bool CanApply(SourceGenHotDesignUpdateContext context);

    ValueTask<SourceGenHotDesignApplyResult> ApplyAsync(
        SourceGenHotDesignUpdateContext context,
        CancellationToken cancellationToken = default);
}
