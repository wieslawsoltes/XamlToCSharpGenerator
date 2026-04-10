namespace XamlToCSharpGenerator.NoUi.Emission;

public sealed class WpfPilotCodeEmitter : PilotCodeEmitterBase
{
    protected override string HintPrefix => "WPFPilot";

    protected override string PublicBuildMethodName => "BuildWpfPilotObjectGraph";

    protected override string PrivateBuildMethodName => "__BuildWpfPilotObjectGraph";
}
