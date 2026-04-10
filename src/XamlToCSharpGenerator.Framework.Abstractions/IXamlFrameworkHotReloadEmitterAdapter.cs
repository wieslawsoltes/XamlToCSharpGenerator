using System.Text;

namespace XamlToCSharpGenerator.Framework.Abstractions;

public interface IXamlFrameworkHotReloadEmitterAdapter
{
    void EmitApplyInvalidationStatements(
        StringBuilder sourceBuilder,
        string indent,
        string instanceReference);

    void EmitRegistrationStateTransfer(
        StringBuilder sourceBuilder,
        string indent,
        string instanceReference,
        string stateReference);
}
