using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TftCompanion.Poc.Core.LocalSimulation;
using TftCompanion.SecondScreen.Presentation;
using TftCompanion.SecondScreen.Sessions;

namespace TftCompanion.SecondScreen.ViewModels;

public sealed class SecondScreenViewModel : INotifyPropertyChanged
{
    private readonly ManualSessionController controller;
    private readonly SecondScreenPresentationSkill presentationSkill;
    private ManualTopic selectedTopic = ManualTopic.LossStreakReview;
    private ManualIntent selectedIntent = ManualIntent.Review;
    private ManualRiskBand selectedHealthBand = ManualRiskBand.Unknown;
    private ManualRiskBand selectedGoldBand = ManualRiskBand.Unknown;
    private ManualCopiesBand selectedCopiesBand = ManualCopiesBand.Unknown;
    private ManualUnitCostBand selectedUnitCostBand = ManualUnitCostBand.Unknown;
    private bool isCurrentAdviceVisible;
    private string? adviceText;
    private string statusText = string.Empty;
    private DateTimeOffset? currentAdviceExpiresAt;

    public SecondScreenViewModel(
        ManualSessionController controller,
        SecondScreenPresentationSkill presentationSkill)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        this.presentationSkill = presentationSkill ?? throw new ArgumentNullException(nameof(presentationSkill));
        Topics = Enum.GetValues<ManualTopic>();
        Intents = Enum.GetValues<ManualIntent>();
        RiskBands = Enum.GetValues<ManualRiskBand>();
        CopiesBands = Enum.GetValues<ManualCopiesBand>();
        UnitCostBands = Enum.GetValues<ManualUnitCostBand>();
        StartNewSessionCommand = new RelayCommand(StartNewSession);
        SubmitCheckpointCommand = new RelayCommand(SubmitCheckpoint);
        ClearCurrentAdviceCommand = new RelayCommand(ClearCurrentAdvice);
        EnableDDriveRecoveryCommand = new RelayCommand(EnableDDriveRecovery);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ManualTopic> Topics { get; }

    public IReadOnlyList<ManualIntent> Intents { get; }

    public IReadOnlyList<ManualRiskBand> RiskBands { get; }

    public IReadOnlyList<ManualCopiesBand> CopiesBands { get; }

    public IReadOnlyList<ManualUnitCostBand> UnitCostBands { get; }

    public ManualTopic SelectedTopic
    {
        get => selectedTopic;
        set => SetField(ref selectedTopic, value);
    }

    public ManualIntent SelectedIntent
    {
        get => selectedIntent;
        set => SetField(ref selectedIntent, value);
    }

    public ManualRiskBand SelectedHealthBand
    {
        get => selectedHealthBand;
        set => SetField(ref selectedHealthBand, value);
    }

    public ManualRiskBand SelectedGoldBand
    {
        get => selectedGoldBand;
        set => SetField(ref selectedGoldBand, value);
    }

    public ManualCopiesBand SelectedCopiesBand
    {
        get => selectedCopiesBand;
        set => SetField(ref selectedCopiesBand, value);
    }

    public ManualUnitCostBand SelectedUnitCostBand
    {
        get => selectedUnitCostBand;
        set => SetField(ref selectedUnitCostBand, value);
    }

    public bool IsCurrentAdviceVisible
    {
        get => isCurrentAdviceVisible;
        private set => SetField(ref isCurrentAdviceVisible, value);
    }

    public string? AdviceText
    {
        get => adviceText;
        private set => SetField(ref adviceText, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetField(ref statusText, value);
    }

    public DateTimeOffset? CurrentAdviceExpiresAt
    {
        get => currentAdviceExpiresAt;
        private set => SetField(ref currentAdviceExpiresAt, value);
    }

    public ICommand StartNewSessionCommand { get; }

    public ICommand SubmitCheckpointCommand { get; }

    public ICommand ClearCurrentAdviceCommand { get; }

    public ICommand EnableDDriveRecoveryCommand { get; }

    public void RestoreAndRefresh() => Apply(controller.Restore());

    public void RefreshProjection() => Apply(controller.Refresh());

    private void StartNewSession() => Apply(controller.StartNewSession(SelectedTopic));

    private void SubmitCheckpoint() => Apply(controller.Submit(new ManualCheckpoint(
        SelectedTopic,
        SelectedIntent,
        SelectedHealthBand,
        SelectedGoldBand,
        SelectedCopiesBand,
        SelectedUnitCostBand)));

    private void ClearCurrentAdvice() => Apply(controller.ClearCurrentAdvice());

    private void EnableDDriveRecovery() => Apply(controller.EnableDDriveRecovery());

    private void Apply(SecondScreenSessionState state)
    {
        SecondScreenPresentation presentation = presentationSkill.Present(state);
        IsCurrentAdviceVisible = presentation.IsCurrentAdviceVisible;
        AdviceText = presentation.AdviceText;
        StatusText = presentation.StatusText;
        CurrentAdviceExpiresAt = presentation.ExpiresAt;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
