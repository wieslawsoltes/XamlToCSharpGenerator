namespace XamlToCSharpGenerator.Framework.Abstractions;

public interface IXamlFrameworkObjectNodeLifecycleEmitterAdapter
{
    string BuildAttachNameScopeStatement(string nodeReference, string nameScopeReference, int scopedIndex);

    string BuildAssignObjectNameStatement(string nodeReference, string objectName);

    string BuildRegisterNameScopeEntryStatement(string nameScopeReference, string objectName, string nodeReference);

    string BuildBeginInitStatement(string nodeReference);

    string BuildEndInitStatement(string nodeReference);
}
