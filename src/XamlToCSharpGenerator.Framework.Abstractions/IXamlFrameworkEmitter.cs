using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Abstractions;

public interface IXamlFrameworkEmitter
{
    (string HintName, string Source) Emit(ResolvedViewModel viewModel);
}
