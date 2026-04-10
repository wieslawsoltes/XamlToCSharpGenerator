using System.Text;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Abstractions;

public interface IXamlFrameworkEventBindingEmitterAdapter
{
    void EmitMethod(
        StringBuilder sourceBuilder,
        ResolvedEventBindingDefinition definition,
        string emittedMethodName);
}
