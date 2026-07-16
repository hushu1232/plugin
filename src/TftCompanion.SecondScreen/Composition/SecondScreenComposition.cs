using TftCompanion.Poc.Core.LocalSimulation;

namespace TftCompanion.SecondScreen.Composition;

public static class SecondScreenComposition
{
    public static string AssemblyBoundaryMarker => "TftCompanion.SecondScreen";

    public static Type CoreAssemblyBoundaryMarker => typeof(ManualScenarioDraft);
}
