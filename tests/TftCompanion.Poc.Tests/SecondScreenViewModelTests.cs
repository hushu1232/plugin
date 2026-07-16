using NUnit.Framework;
using TftCompanion.Poc.Core.LocalSimulation;
using TftCompanion.Poc.Tests.TestSupport;
using TftCompanion.SecondScreen.Presentation;
using TftCompanion.SecondScreen.Recovery;
using TftCompanion.SecondScreen.Sessions;
using TftCompanion.SecondScreen.ViewModels;

namespace TftCompanion.Poc.Tests;

[TestFixture]
public sealed class SecondScreenViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public void view_model_displays_projection_presentation_without_direct_advice_rendering()
    {
        SecondScreenViewModel viewModel = CreateViewModel(out _, out _);
        viewModel.SelectedTopic = ManualTopic.LossStreakReview;
        viewModel.StartNewSessionCommand.Execute(null);
        viewModel.SelectedIntent = ManualIntent.PreserveLossStreak;
        viewModel.SelectedHealthBand = ManualRiskBand.Medium;
        viewModel.SelectedGoldBand = ManualRiskBand.High;

        viewModel.SubmitCheckpointCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsCurrentAdviceVisible, Is.True);
            Assert.That(viewModel.AdviceText, Is.Not.Null.And.Not.Empty);
            Assert.That(viewModel.StatusText, Is.Not.Empty);
            Assert.That(viewModel.CurrentAdviceExpiresAt, Is.EqualTo(Now + ManualSessionPolicy.CurrentAdviceLifetime));
        });
    }

    [Test]
    public void clear_command_and_expiry_refresh_only_project_controller_state()
    {
        SecondScreenViewModel viewModel = CreateViewModel(out _, out FakeSecondScreenClock clock);
        viewModel.SelectedTopic = ManualTopic.LossStreakReview;
        viewModel.StartNewSessionCommand.Execute(null);
        viewModel.SelectedIntent = ManualIntent.PreserveLossStreak;
        viewModel.SelectedHealthBand = ManualRiskBand.Medium;
        viewModel.SelectedGoldBand = ManualRiskBand.High;
        viewModel.SubmitCheckpointCommand.Execute(null);

        viewModel.ClearCurrentAdviceCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsCurrentAdviceVisible, Is.False);
            Assert.That(viewModel.AdviceText, Is.Null);
            Assert.That(viewModel.StatusText, Is.EqualTo("The current advice was cleared."));
        });

        viewModel.StartNewSessionCommand.Execute(null);
        viewModel.SelectedIntent = ManualIntent.PrepareToStabilize;
        viewModel.SelectedHealthBand = ManualRiskBand.Medium;
        viewModel.SelectedGoldBand = ManualRiskBand.High;
        viewModel.SubmitCheckpointCommand.Execute(null);
        clock.UtcNow = viewModel.CurrentAdviceExpiresAt!.Value;

        viewModel.RefreshProjection();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsCurrentAdviceVisible, Is.False);
            Assert.That(viewModel.AdviceText, Is.Null);
            Assert.That(viewModel.StatusText, Is.EqualTo("The current advice expired and is no longer shown."));
        });
    }

    [Test]
    public void restore_and_refresh_reads_once_without_constructing_a_real_recovery_adapter()
    {
        SecondScreenViewModel viewModel = CreateViewModel(out FakeManualSessionRecoveryFileSystem fileSystem, out _);

        viewModel.RestoreAndRefresh();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsCurrentAdviceVisible, Is.False);
            Assert.That(viewModel.AdviceText, Is.Null);
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ReadExisting" }));
        });
    }

    [Test]
    public void relay_command_exposes_can_execute_and_notifies_its_binding()
    {
        bool executed = false;
        int notificationCount = 0;
        RelayCommand command = new(() => executed = true, () => true);
        command.CanExecuteChanged += (_, _) => notificationCount++;

        command.Execute(null);
        command.RaiseCanExecuteChanged();

        Assert.Multiple(() =>
        {
            Assert.That(command.CanExecute(null), Is.True);
            Assert.That(executed, Is.True);
            Assert.That(notificationCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void submit_notifies_the_expiry_fields_used_by_the_window_timer()
    {
        SecondScreenViewModel viewModel = CreateViewModel(out _, out _);
        List<string?> notifications = [];
        viewModel.PropertyChanged += (_, eventArgs) => notifications.Add(eventArgs.PropertyName);
        viewModel.SelectedTopic = ManualTopic.LossStreakReview;
        viewModel.StartNewSessionCommand.Execute(null);
        viewModel.SelectedIntent = ManualIntent.PreserveLossStreak;
        viewModel.SelectedHealthBand = ManualRiskBand.Medium;
        viewModel.SelectedGoldBand = ManualRiskBand.High;
        notifications.Clear();

        viewModel.SubmitCheckpointCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(notifications, Does.Contain(nameof(SecondScreenViewModel.IsCurrentAdviceVisible)));
            Assert.That(notifications, Does.Contain(nameof(SecondScreenViewModel.CurrentAdviceExpiresAt)));
        });
    }

    private static SecondScreenViewModel CreateViewModel(
        out FakeManualSessionRecoveryFileSystem fileSystem,
        out FakeSecondScreenClock clock)
    {
        fileSystem = new FakeManualSessionRecoveryFileSystem();
        clock = new FakeSecondScreenClock(Now);
        ManualSessionController controller = new(
            new ManualSessionRecoveryStore(fileSystem, new ManualSessionRecoveryCodec()),
            clock,
            new SequenceManualRunIdGenerator(new[]
            {
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
            }));

        return new SecondScreenViewModel(controller, new SecondScreenPresentationSkill());
    }
}
