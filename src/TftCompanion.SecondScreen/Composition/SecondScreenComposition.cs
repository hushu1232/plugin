using TftCompanion.Poc.Core.LocalSimulation;
using TftCompanion.SecondScreen.Presentation;
using TftCompanion.SecondScreen.Recovery;
using TftCompanion.SecondScreen.Sessions;
using TftCompanion.SecondScreen.ViewModels;

namespace TftCompanion.SecondScreen.Composition;

public static class SecondScreenComposition
{
    public static string AssemblyBoundaryMarker => "TftCompanion.SecondScreen";

    public static Type CoreAssemblyBoundaryMarker => typeof(ManualScenarioDraft);

    public static SecondScreenViewModel CreateViewModel()
    {
        WindowsManualSessionRecoveryFileSystem fileSystem = new();
        ManualSessionRecoveryCodec codec = new();
        ManualSessionRecoveryStore recoveryStore = new(fileSystem, codec);
        ManualSessionController controller = new(
            recoveryStore,
            new SystemSecondScreenClock(),
            new SystemManualRunIdGenerator());
        SecondScreenPresentationSkill presentationSkill = new();

        return new SecondScreenViewModel(controller, presentationSkill);
    }
}
