namespace XamlToCSharpGenerator.NoUi.Emission;

public sealed class MauiPilotCodeEmitter : PilotCodeEmitterBase
{
    protected override string HintPrefix => "MAUIPilot";

    protected override string PublicBuildMethodName => "BuildMauiPilotVisualTree";

    protected override string PrivateBuildMethodName => "__BuildMauiPilotVisualTree";
}
