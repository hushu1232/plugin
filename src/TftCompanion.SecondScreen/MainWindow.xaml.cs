using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using TftCompanion.SecondScreen.ViewModels;

namespace TftCompanion.SecondScreen;

public partial class MainWindow : Window
{
    private readonly SecondScreenViewModel viewModel;
    private readonly DispatcherTimer expiryTimer = new();

    public MainWindow(SecondScreenViewModel viewModel)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = this.viewModel;
        this.viewModel.PropertyChanged += OnViewModelPropertyChanged;
        expiryTimer.Tick += OnExpiryTimerTick;
        Activated += OnActivated;
        Closed += OnClosed;
    }

    private void OnActivated(object? sender, EventArgs eventArgs) => RefreshAndSchedule();

    private void OnExpiryTimerTick(object? sender, EventArgs eventArgs)
    {
        expiryTimer.Stop();
        RefreshAndSchedule();
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        expiryTimer.Stop();
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        expiryTimer.Tick -= OnExpiryTimerTick;
        Activated -= OnActivated;
        Closed -= OnClosed;
    }

    private void RefreshAndSchedule()
    {
        viewModel.RefreshProjection();
        ScheduleExpiry();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SecondScreenViewModel.IsCurrentAdviceVisible) or
            nameof(SecondScreenViewModel.CurrentAdviceExpiresAt))
        {
            ScheduleExpiry();
        }
    }

    private void ScheduleExpiry()
    {
        expiryTimer.Stop();
        if (viewModel.IsCurrentAdviceVisible && viewModel.CurrentAdviceExpiresAt is DateTimeOffset expiresAt)
        {
            TimeSpan due = expiresAt - DateTimeOffset.UtcNow;
            expiryTimer.Interval = due > TimeSpan.Zero ? due : TimeSpan.FromMilliseconds(1);
            expiryTimer.Start();
        }
    }
}
