using NUnit.Framework;
using TftCompanion.Poc.Core.LocalSimulation;
using TftCompanion.Poc.Tests.TestSupport;
using TftCompanion.SecondScreen.Recovery;
using TftCompanion.SecondScreen.Sessions;

namespace TftCompanion.Poc.Tests;

[TestFixture]
public sealed class ManualSessionControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid RunA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid RunB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Test]
    public void start_submit_clear_and_higher_resubmit_keep_one_run_at_revisions_one_two_and_three()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new();
        FakeSecondScreenClock clock = new(Now);
        ManualSessionController controller = CreateController(fileSystem, clock, RunA);

        SecondScreenSessionState started = controller.StartNewSession(ManualTopic.LossStreakReview);
        SecondScreenSessionState first = controller.Submit(LossCheckpoint());
        SecondScreenSessionState cleared = controller.ClearCurrentAdvice();
        SecondScreenSessionState second = controller.Submit(LossCheckpoint(ManualIntent.Review));

        Assert.Multiple(() =>
        {
            Assert.That(started.SessionPhase, Is.EqualTo(ManualSessionPhase.EditingCheckpoint));
            Assert.That(started.Projection.IsCurrent, Is.False);
            Assert.That(first.Projection.IsCurrent, Is.True);
            Assert.That(first.Projection.ManualRunId, Is.EqualTo(RunA));
            Assert.That(first.Projection.ManualRevision, Is.EqualTo(1));
            Assert.That(cleared.SessionPhase, Is.EqualTo(ManualSessionPhase.Cleared));
            Assert.That(cleared.Projection.IsCurrent, Is.False);
            Assert.That(second.Projection.IsCurrent, Is.True);
            Assert.That(second.Projection.ManualRunId, Is.EqualTo(RunA));
            Assert.That(second.Projection.ManualRevision, Is.EqualTo(3));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[]
            {
                "WriteExisting", "WriteExisting", "WriteExisting", "WriteExisting"
            }));
        });
    }

    [Test]
    public void topic_change_requires_a_new_run_and_the_old_run_cannot_reappear()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new();
        FakeSecondScreenClock clock = new(Now);
        ManualSessionController controller = CreateController(fileSystem, clock, RunA, RunB);

        _ = controller.StartNewSession(ManualTopic.LossStreakReview);
        SecondScreenSessionState oldCurrent = controller.Submit(LossCheckpoint());
        SecondScreenSessionState newEditing = controller.StartNewSession(ManualTopic.RerollReview);
        SecondScreenSessionState mismatched = controller.Submit(LossCheckpoint());
        SecondScreenSessionState newCurrent = controller.Submit(RerollCheckpoint());

        Assert.Multiple(() =>
        {
            Assert.That(oldCurrent.Projection.ManualRunId, Is.EqualTo(RunA));
            Assert.That(newEditing.Projection.IsCurrent, Is.False);
            Assert.That(mismatched.Notice, Is.EqualTo(ManualSessionNotice.TopicChangeRequiresNewSession));
            Assert.That(mismatched.Projection.IsCurrent, Is.False);
            Assert.That(newCurrent.Projection.IsCurrent, Is.True);
            Assert.That(newCurrent.Projection.ManualRunId, Is.EqualTo(RunB));
            Assert.That(newCurrent.Projection.ManualRevision, Is.EqualTo(1));
        });
    }

    [Test]
    public void incomplete_checkpoint_leaves_editing_without_a_current_card()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new();
        ManualSessionController controller = CreateController(fileSystem, new FakeSecondScreenClock(Now), RunA);

        _ = controller.StartNewSession(ManualTopic.LossStreakReview);
        SecondScreenSessionState result = controller.Submit(LossCheckpoint(goldBand: ManualRiskBand.Unknown));

        Assert.Multiple(() =>
        {
            Assert.That(result.SessionPhase, Is.EqualTo(ManualSessionPhase.EditingCheckpoint));
            Assert.That(result.Notice, Is.EqualTo(ManualSessionNotice.IncompleteCheckpoint));
            Assert.That(result.Projection.IsCurrent, Is.False);
            Assert.That(result.Projection.RenderedText, Is.Null);
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "WriteExisting" }));
        });
    }

    [Test]
    public void refresh_at_exact_expiry_removes_the_card_without_an_automatic_write()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new();
        FakeSecondScreenClock clock = new(Now);
        ManualSessionController controller = CreateController(fileSystem, clock, RunA);

        _ = controller.StartNewSession(ManualTopic.LossStreakReview);
        SecondScreenSessionState current = controller.Submit(LossCheckpoint());
        clock.UtcNow = current.Projection.ExpiresAt!.Value;
        SecondScreenSessionState expired = controller.Refresh();

        Assert.Multiple(() =>
        {
            Assert.That(expired.SessionPhase, Is.EqualTo(ManualSessionPhase.Expired));
            Assert.That(expired.Notice, Is.EqualTo(ManualSessionNotice.Expired));
            Assert.That(expired.Projection.IsCurrent, Is.False);
            Assert.That(expired.Projection.RenderedText, Is.Null);
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "WriteExisting", "WriteExisting" }));
        });
    }

    [Test]
    public void submit_without_a_session_and_clear_without_a_card_do_not_mutate_state()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new();
        ManualSessionController controller = CreateController(fileSystem, new FakeSecondScreenClock(Now), RunA);

        SecondScreenSessionState submit = controller.Submit(LossCheckpoint());
        SecondScreenSessionState clear = controller.ClearCurrentAdvice();

        Assert.Multiple(() =>
        {
            Assert.That(submit.SessionPhase, Is.EqualTo(ManualSessionPhase.NoSession));
            Assert.That(submit.Notice, Is.EqualTo(ManualSessionNotice.NoActiveSession));
            Assert.That(clear.SessionPhase, Is.EqualTo(ManualSessionPhase.NoSession));
            Assert.That(clear.Notice, Is.EqualTo(ManualSessionNotice.NoActiveSession));
            Assert.That(clear.Projection.IsCurrent, Is.False);
            Assert.That(fileSystem.Operations, Is.Empty);
        });
    }

    [Test]
    public void write_failure_preserves_the_current_card_and_reports_memory_only()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new();
        ManualSessionController controller = CreateController(fileSystem, new FakeSecondScreenClock(Now), RunA);

        _ = controller.StartNewSession(ManualTopic.LossStreakReview);
        fileSystem.WriteResult = FakeManualSessionRecoveryFileSystem.Failure(
            ManualRecoveryStatus.MemoryOnlyDegraded,
            "RECOVERY_WRITE_DENIED");
        SecondScreenSessionState submitted = controller.Submit(LossCheckpoint());

        Assert.Multiple(() =>
        {
            Assert.That(submitted.SessionPhase, Is.EqualTo(ManualSessionPhase.CurrentAdvice));
            Assert.That(submitted.Projection.IsCurrent, Is.True);
            Assert.That(submitted.RecoveryStatus, Is.EqualTo(ManualRecoveryStatus.MemoryOnlyDegraded));
            Assert.That(submitted.Notice, Is.EqualTo(ManualSessionNotice.RecoveryUnavailable));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "WriteExisting", "WriteExisting" }));
        });
    }

    [Test]
    public void restore_re_evaluates_valid_current_advice_through_the_harness()
    {
        FakeManualSessionRecoveryFileSystem sourceFileSystem = new();
        ManualSessionController source = CreateController(sourceFileSystem, new FakeSecondScreenClock(Now), RunA);
        _ = source.StartNewSession(ManualTopic.LossStreakReview);
        _ = source.Submit(LossCheckpoint());

        FakeManualSessionRecoveryFileSystem restoredFileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(sourceFileSystem.WrittenDocuments[^1])
        };
        ManualSessionController restored = CreateController(restoredFileSystem, new FakeSecondScreenClock(Now), RunB);

        SecondScreenSessionState state = restored.Restore();

        Assert.Multiple(() =>
        {
            Assert.That(state.SessionPhase, Is.EqualTo(ManualSessionPhase.CurrentAdvice));
            Assert.That(state.Projection.IsCurrent, Is.True);
            Assert.That(state.Projection.ManualRunId, Is.EqualTo(RunA));
            Assert.That(state.Projection.ManualRevision, Is.EqualTo(1));
            Assert.That(state.Projection.RenderedText, Is.Not.Null.And.Not.Empty);
            Assert.That(restoredFileSystem.Operations, Is.EqualTo(new[] { "ReadExisting" }));
        });
    }

    [Test]
    public void restore_handles_cleared_expired_and_invalid_saved_data_without_old_text()
    {
        FakeManualSessionRecoveryFileSystem clearedSourceFileSystem = new();
        ManualSessionController clearedSource = CreateController(clearedSourceFileSystem, new FakeSecondScreenClock(Now), RunA);
        _ = clearedSource.StartNewSession(ManualTopic.LossStreakReview);
        _ = clearedSource.Submit(LossCheckpoint());
        _ = clearedSource.ClearCurrentAdvice();

        FakeManualSessionRecoveryFileSystem clearedFileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(clearedSourceFileSystem.WrittenDocuments[^1])
        };
        SecondScreenSessionState cleared = CreateController(clearedFileSystem, new FakeSecondScreenClock(Now), RunB).Restore();

        FakeManualSessionRecoveryFileSystem currentSourceFileSystem = new();
        ManualSessionController currentSource = CreateController(currentSourceFileSystem, new FakeSecondScreenClock(Now), RunA);
        _ = currentSource.StartNewSession(ManualTopic.LossStreakReview);
        _ = currentSource.Submit(LossCheckpoint());
        FakeManualSessionRecoveryFileSystem expiredFileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(currentSourceFileSystem.WrittenDocuments[^1])
        };
        SecondScreenSessionState expired = CreateController(
            expiredFileSystem,
            new FakeSecondScreenClock(Now + ManualSessionPolicy.CurrentAdviceLifetime),
            RunB).Restore();

        FakeManualSessionRecoveryFileSystem invalidFileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(new byte[] { 0x7B, 0xFF, 0x7D })
        };
        SecondScreenSessionState invalid = CreateController(invalidFileSystem, new FakeSecondScreenClock(Now), RunB).Restore();

        Assert.Multiple(() =>
        {
            Assert.That(cleared.SessionPhase, Is.EqualTo(ManualSessionPhase.Cleared));
            Assert.That(cleared.Projection.IsCurrent, Is.False);
            Assert.That(cleared.Projection.RenderedText, Is.Null);
            Assert.That(expired.SessionPhase, Is.EqualTo(ManualSessionPhase.Expired));
            Assert.That(expired.Projection.IsCurrent, Is.False);
            Assert.That(expired.Projection.RenderedText, Is.Null);
            Assert.That(invalid.Projection.IsCurrent, Is.False);
            Assert.That(invalid.Projection.RenderedText, Is.Null);
            Assert.That(invalid.RecoveryStatus, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void restore_rejects_a_current_snapshot_that_cannot_advance_its_counters(bool exhaustsSnapshotGeneration)
    {
        ManualSessionRecoveryCodec codec = new();
        ManualSessionRecoveryPayload current = CreateValidCurrentPayload();
        ManualSessionRecoveryPayload exhausted = exhaustsSnapshotGeneration
            ? current with { SnapshotGeneration = long.MaxValue }
            : current with { HighestRevision = long.MaxValue };
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(codec.Encode(exhausted))
        };
        ManualSessionController controller = CreateController(fileSystem, new FakeSecondScreenClock(Now), RunB);

        SecondScreenSessionState restored = controller.Restore();
        SecondScreenSessionState started = controller.StartNewSession(ManualTopic.RerollReview);

        Assert.Multiple(() =>
        {
            Assert.That(restored.RecoveryStatus, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
            Assert.That(restored.Notice, Is.EqualTo(ManualSessionNotice.RecoveryRejected));
            Assert.That(restored.SessionPhase, Is.EqualTo(ManualSessionPhase.NoSession));
            Assert.That(restored.Projection.IsCurrent, Is.False);
            Assert.That(restored.Projection.RenderedText, Is.Null);
            Assert.That(started.SessionPhase, Is.EqualTo(ManualSessionPhase.EditingCheckpoint));
            Assert.That(started.RecoveryStatus, Is.EqualTo(ManualRecoveryStatus.RecoveryAvailable));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ReadExisting", "WriteExisting" }));
        });
    }

    [Test]
    public void restore_rejects_current_advice_with_a_lifetime_other_than_the_session_policy()
    {
        ManualSessionRecoveryCodec codec = new();
        ManualSessionRecoveryPayload current = CreateValidCurrentPayload();
        ManualSessionRecoveryPayload nonStandardLifetime = current with
        {
            ExpiresAt = current.CreatedAt + TimeSpan.FromHours(1)
        };
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(codec.Encode(nonStandardLifetime))
        };
        ManualSessionController controller = CreateController(fileSystem, new FakeSecondScreenClock(Now), RunB);

        SecondScreenSessionState restored = controller.Restore();

        Assert.Multiple(() =>
        {
            Assert.That(restored.RecoveryStatus, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
            Assert.That(restored.Projection.IsCurrent, Is.False);
            Assert.That(restored.Projection.RenderedText, Is.Null);
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ReadExisting" }));
        });
    }

    [Test]
    public void restore_rejects_a_short_current_lifetime_at_the_date_floor_before_provisioning_can_underflow()
    {
        ManualSessionRecoveryCodec codec = new();
        DateTimeOffset createdAt = DateTimeOffset.MinValue;
        ManualSessionRecoveryPayload current = CreateValidCurrentPayload() with
        {
            CreatedAt = createdAt,
            ExpiresAt = createdAt + TimeSpan.FromMinutes(1)
        };
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(codec.Encode(current))
        };
        ManualSessionController controller = CreateController(fileSystem, new FakeSecondScreenClock(createdAt), RunB);

        SecondScreenSessionState restored = controller.Restore();

        Assert.Multiple(() =>
        {
            Assert.That(restored.RecoveryStatus, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
            Assert.That(restored.Projection.IsCurrent, Is.False);
            Assert.That(restored.Projection.RenderedText, Is.Null);
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ReadExisting" }));
        });
    }

    [TestCase(ManualSessionPhase.Cleared)]
    [TestCase(ManualSessionPhase.Expired)]
    public void restore_rejects_a_terminal_snapshot_that_does_not_preserve_a_submittable_checkpoint(
        ManualSessionPhase terminalPhase)
    {
        ManualSessionRecoveryCodec codec = new();
        ManualSessionRecoveryPayload current = CreateValidCurrentPayload();
        ManualSessionRecoveryPayload invalidTerminal = current with
        {
            SessionPhase = terminalPhase,
            Intent = ManualIntent.ConsiderReroll,
            ExpiresAt = terminalPhase == ManualSessionPhase.Expired ? Now : current.ExpiresAt
        };
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(codec.Encode(invalidTerminal))
        };
        ManualSessionController controller = CreateController(fileSystem, new FakeSecondScreenClock(Now), RunB);

        SecondScreenSessionState restored = controller.Restore();

        Assert.Multiple(() =>
        {
            Assert.That(restored.RecoveryStatus, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
            Assert.That(restored.SessionPhase, Is.EqualTo(ManualSessionPhase.NoSession));
            Assert.That(restored.Projection.IsCurrent, Is.False);
            Assert.That(restored.Projection.RenderedText, Is.Null);
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ReadExisting" }));
        });
    }

    [Test]
    public void restore_rejects_an_expired_snapshot_that_has_not_reached_its_expiry_time()
    {
        ManualSessionRecoveryCodec codec = new();
        ManualSessionRecoveryPayload current = CreateValidCurrentPayload();
        ManualSessionRecoveryPayload prematureExpired = current with
        {
            SessionPhase = ManualSessionPhase.Expired
        };
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(codec.Encode(prematureExpired))
        };
        ManualSessionController controller = CreateController(fileSystem, new FakeSecondScreenClock(Now), RunB);

        SecondScreenSessionState restored = controller.Restore();

        Assert.Multiple(() =>
        {
            Assert.That(restored.RecoveryStatus, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
            Assert.That(restored.SessionPhase, Is.EqualTo(ManualSessionPhase.NoSession));
            Assert.That(restored.Projection.IsCurrent, Is.False);
            Assert.That(restored.Projection.RenderedText, Is.Null);
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ReadExisting" }));
        });
    }

    [Test]
    public void restore_rejects_a_stale_editing_snapshot_after_a_terminal_state_of_the_same_run()
    {
        FakeManualSessionRecoveryFileSystem sourceFileSystem = new();
        ManualSessionController source = CreateController(sourceFileSystem, new FakeSecondScreenClock(Now), RunA);
        _ = source.StartNewSession(ManualTopic.LossStreakReview);
        byte[] editingDocument = sourceFileSystem.WrittenDocuments[^1];
        _ = source.Submit(LossCheckpoint());
        _ = source.ClearCurrentAdvice();
        byte[] clearedDocument = sourceFileSystem.WrittenDocuments[^1];

        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(clearedDocument)
        };
        ManualSessionController controller = CreateController(fileSystem, new FakeSecondScreenClock(Now), RunB);
        SecondScreenSessionState cleared = controller.Restore();
        fileSystem.ReadResult = FakeManualSessionRecoveryFileSystem.Success(editingDocument);

        SecondScreenSessionState staleEditing = controller.Restore();

        Assert.Multiple(() =>
        {
            Assert.That(cleared.SessionPhase, Is.EqualTo(ManualSessionPhase.Cleared));
            Assert.That(staleEditing.RecoveryStatus, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
            Assert.That(staleEditing.Notice, Is.EqualTo(ManualSessionNotice.RecoveryRejected));
            Assert.That(staleEditing.SessionPhase, Is.EqualTo(ManualSessionPhase.Cleared));
            Assert.That(staleEditing.Projection.IsCurrent, Is.False);
            Assert.That(staleEditing.Projection.RenderedText, Is.Null);
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ReadExisting", "ReadExisting" }));
        });
    }

    [TestCase(ManualRecoveryStatus.RecoveryRejected, "RECOVERY_DOCUMENT_INVALID", ManualSessionNotice.RecoveryRejected)]
    [TestCase(ManualRecoveryStatus.MemoryOnlyDegraded, "RECOVERY_NOT_PROVISIONED", ManualSessionNotice.RecoveryUnavailable)]
    public void failed_restore_invalidates_a_recovered_current_card_without_writing(
        ManualRecoveryStatus failedStatus,
        string failureCode,
        ManualSessionNotice expectedNotice)
    {
        FakeManualSessionRecoveryFileSystem sourceFileSystem = new();
        ManualSessionController source = CreateController(sourceFileSystem, new FakeSecondScreenClock(Now), RunA);
        _ = source.StartNewSession(ManualTopic.LossStreakReview);
        _ = source.Submit(LossCheckpoint());

        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(sourceFileSystem.WrittenDocuments[^1])
        };
        ManualSessionController controller = CreateController(fileSystem, new FakeSecondScreenClock(Now), RunB);
        SecondScreenSessionState recovered = controller.Restore();
        fileSystem.ReadResult = FakeManualSessionRecoveryFileSystem.Failure(failedStatus, failureCode);

        SecondScreenSessionState failedRestore = controller.Restore();

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Projection.IsCurrent, Is.True);
            Assert.That(failedRestore.SessionPhase, Is.EqualTo(ManualSessionPhase.NoSession));
            Assert.That(failedRestore.RecoveryStatus, Is.EqualTo(failedStatus));
            Assert.That(failedRestore.Notice, Is.EqualTo(expectedNotice));
            Assert.That(failedRestore.Projection.IsCurrent, Is.False);
            Assert.That(failedRestore.Projection.RenderedText, Is.Null);
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ReadExisting", "ReadExisting" }));
        });
    }

    [Test]
    public void failed_restore_does_not_clear_a_later_local_current_run()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Failure(
                ManualRecoveryStatus.RecoveryRejected,
                "RECOVERY_DOCUMENT_INVALID")
        };
        ManualSessionController controller = CreateController(fileSystem, new FakeSecondScreenClock(Now), RunA);

        _ = controller.StartNewSession(ManualTopic.LossStreakReview);
        SecondScreenSessionState localCurrent = controller.Submit(LossCheckpoint());
        SecondScreenSessionState failedRestore = controller.Restore();

        Assert.Multiple(() =>
        {
            Assert.That(localCurrent.Projection.IsCurrent, Is.True);
            Assert.That(failedRestore.SessionPhase, Is.EqualTo(ManualSessionPhase.CurrentAdvice));
            Assert.That(failedRestore.RecoveryStatus, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
            Assert.That(failedRestore.Notice, Is.EqualTo(ManualSessionNotice.RecoveryRejected));
            Assert.That(failedRestore.Projection.IsCurrent, Is.True);
            Assert.That(failedRestore.Projection.ManualRunId, Is.EqualTo(RunA));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[]
            {
                "WriteExisting", "WriteExisting", "ReadExisting"
            }));
        });
    }

    [Test]
    public void valid_cleared_restore_replaces_a_prior_recovery_derived_current_card()
    {
        FakeManualSessionRecoveryFileSystem sourceFileSystem = new();
        ManualSessionController source = CreateController(sourceFileSystem, new FakeSecondScreenClock(Now), RunA);
        _ = source.StartNewSession(ManualTopic.LossStreakReview);
        _ = source.Submit(LossCheckpoint());
        byte[] currentDocument = sourceFileSystem.WrittenDocuments[^1];
        _ = source.ClearCurrentAdvice();
        byte[] clearedDocument = sourceFileSystem.WrittenDocuments[^1];

        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(currentDocument)
        };
        ManualSessionController controller = CreateController(fileSystem, new FakeSecondScreenClock(Now), RunB);
        SecondScreenSessionState recoveredCurrent = controller.Restore();
        fileSystem.ReadResult = FakeManualSessionRecoveryFileSystem.Success(clearedDocument);

        SecondScreenSessionState restoredCleared = controller.Restore();

        Assert.Multiple(() =>
        {
            Assert.That(recoveredCurrent.Projection.IsCurrent, Is.True);
            Assert.That(restoredCleared.SessionPhase, Is.EqualTo(ManualSessionPhase.Cleared));
            Assert.That(restoredCleared.Notice, Is.EqualTo(ManualSessionNotice.Cleared));
            Assert.That(restoredCleared.Projection.IsCurrent, Is.False);
            Assert.That(restoredCleared.Projection.RenderedText, Is.Null);
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ReadExisting", "ReadExisting" }));
        });
    }

    [Test]
    public void a_recovered_old_run_is_rejected_after_a_new_run_becomes_current()
    {
        FakeManualSessionRecoveryFileSystem sourceFileSystem = new();
        ManualSessionController source = CreateController(sourceFileSystem, new FakeSecondScreenClock(Now), RunA);
        _ = source.StartNewSession(ManualTopic.LossStreakReview);
        _ = source.Submit(LossCheckpoint());
        byte[] recoveredOldDocument = sourceFileSystem.WrittenDocuments[^1];

        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(recoveredOldDocument)
        };
        ManualSessionController controller = CreateController(fileSystem, new FakeSecondScreenClock(Now), RunB);
        _ = controller.Restore();
        _ = controller.StartNewSession(ManualTopic.RerollReview);
        SecondScreenSessionState newer = controller.Submit(RerollCheckpoint());
        SecondScreenSessionState retryOldRecovery = controller.Restore();

        Assert.Multiple(() =>
        {
            Assert.That(newer.Projection.IsCurrent, Is.True);
            Assert.That(newer.Projection.ManualRunId, Is.EqualTo(RunB));
            Assert.That(retryOldRecovery.Projection.IsCurrent, Is.True);
            Assert.That(retryOldRecovery.Projection.ManualRunId, Is.EqualTo(RunB));
            Assert.That(retryOldRecovery.Projection.ManualRevision, Is.EqualTo(1));
        });
    }

    [Test]
    public void enabling_existing_only_recovery_does_not_create_and_stops_before_write_when_unavailable()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ProvisionResult = FakeManualSessionRecoveryFileSystem.Failure(
                ManualRecoveryStatus.MemoryOnlyDegraded,
                "RECOVERY_NOT_PROVISIONED")
        };
        ManualSessionController controller = CreateController(fileSystem, new FakeSecondScreenClock(Now), RunA);

        SecondScreenSessionState enabled = controller.EnableDDriveRecovery();

        Assert.Multiple(() =>
        {
            Assert.That(enabled.RecoveryStatus, Is.EqualTo(ManualRecoveryStatus.MemoryOnlyDegraded));
            Assert.That(enabled.Notice, Is.EqualTo(ManualSessionNotice.RecoveryUnavailable));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ProvisionFixedFile" }));
            Assert.That(fileSystem.CreatedPaths, Is.Empty);
        });
    }

    private static ManualSessionController CreateController(
        FakeManualSessionRecoveryFileSystem fileSystem,
        FakeSecondScreenClock clock,
        params Guid[] runIds) => new(
            new ManualSessionRecoveryStore(fileSystem, new ManualSessionRecoveryCodec()),
            clock,
            new SequenceManualRunIdGenerator(runIds));

    private static ManualSessionRecoveryPayload CreateValidCurrentPayload()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new();
        ManualSessionController source = CreateController(fileSystem, new FakeSecondScreenClock(Now), RunA);
        _ = source.StartNewSession(ManualTopic.LossStreakReview);
        _ = source.Submit(LossCheckpoint());

        ManualSessionRecoveryCodec codec = new();
        bool decoded = codec.TryDecode(
            fileSystem.WrittenDocuments[^1],
            out ManualSessionRecoveryPayload? payload,
            out string failureCode);

        Assert.Multiple(() =>
        {
            Assert.That(decoded, Is.True);
            Assert.That(payload, Is.Not.Null);
            Assert.That(failureCode, Is.EqualTo("NONE"));
        });

        return payload!;
    }

    private static ManualCheckpoint LossCheckpoint(
        ManualIntent intent = ManualIntent.PreserveLossStreak,
        ManualRiskBand healthBand = ManualRiskBand.Medium,
        ManualRiskBand goldBand = ManualRiskBand.High) => new(
            ManualTopic.LossStreakReview,
            intent,
            healthBand,
            goldBand,
            ManualCopiesBand.Unknown,
            ManualUnitCostBand.Unknown);

    private static ManualCheckpoint RerollCheckpoint() => new(
        ManualTopic.RerollReview,
        ManualIntent.ConsiderReroll,
        ManualRiskBand.Unknown,
        ManualRiskBand.High,
        ManualCopiesBand.NearThreshold,
        ManualUnitCostBand.Four);
}
