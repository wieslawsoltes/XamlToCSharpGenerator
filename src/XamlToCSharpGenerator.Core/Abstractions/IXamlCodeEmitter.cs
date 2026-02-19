using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Core.Abstractions;

public interface IXamlCodeEmitter
{
    (string HintName, string Source) Emit(ResolvedViewModel viewModel);
}
