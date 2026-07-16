using System.Windows;
using TftCompanion.SecondScreen.Composition;
using TftCompanion.SecondScreen.ViewModels;

namespace TftCompanion.SecondScreen;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        SecondScreenViewModel viewModel = SecondScreenComposition.CreateViewModel();
        viewModel.RestoreAndRefresh();
        MainWindow window = new(viewModel);
        MainWindow = window;
        window.Show();
    }
}
