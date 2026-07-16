using TftCompanion.Poc.Core.LocalSimulation;
using TftCompanion.SecondScreen.Recovery;

namespace TftCompanion.SecondScreen.Sessions;

public sealed class SystemSecondScreenClock : ISecondScreenClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class SystemManualRunIdGenerator : IManualRunIdGenerator
{
    public Guid Create() => Guid.NewGuid();
}

public sealed class ManualSessionController
{
    private readonly ManualSessionRecoveryStore recoveryStore;
    private readonly ISecondScreenClock clock;
    private readonly IManualRunIdGenerator runIdGenerator;
    private readonly FrozenFixturePack fixturePack;
    private readonly LocalCompanionEngine engine;
    private LocalAdviceCoordinator coordinator;
    private readonly EmbeddedFixtureExpressionSkill expressionSkill;
    private readonly ManualPanelProjectionHarness projectionHarness;

    private Guid activeRunId;
    private ManualTopic activeTopic = ManualTopic.LossStreakReview;
    private long highestRevision;
    private ManualCheckpoint checkpoint = EmptyCheckpoint(ManualTopic.LossStreakReview);
    private ManualSessionPhase sessionPhase = ManualSessionPhase.NoSession;
    private long snapshotGeneration;
    private ManualRecoveryStatus recoveryStatus = ManualRecoveryStatus.MemoryOnlyDegraded;
    private Guid? recoveryDerivedCurrentRunId;

    public ManualSessionController(
        ManualSessionRecoveryStore recoveryStore,
        ISecondScreenClock clock,
        IManualRunIdGenerator runIdGenerator)
    {
        this.recoveryStore = recoveryStore ?? throw new ArgumentNullException(nameof(recoveryStore));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.runIdGenerator = runIdGenerator ?? throw new ArgumentNullException(nameof(runIdGenerator));
        fixturePack = FrozenFixturePack.CreateV1();
        engine = new LocalCompanionEngine();
        coordinator = new LocalAdviceCoordinator();
        expressionSkill = new EmbeddedFixtureExpressionSkill();
        projectionHarness = new ManualPanelProjectionHarness(expressionSkill);
    }

    public SecondScreenSessionState Restore()
    {
        ManualRecoveryLoadResult load = recoveryStore.TryLoad();
        recoveryStatus = load.Status;

        if (load.Status != ManualRecoveryStatus.RecoveryAvailable || load.Snapshot is null)
        {
            DiscardRecoveryDerivedCurrentAfterFailedRestore();
            return State(load.Status == ManualRecoveryStatus.RecoveryRejected
                ? ManualSessionNotice.RecoveryRejected
                : ManualSessionNotice.RecoveryUnavailable);
        }

        if (coordinator.Current is not null &&
            activeRunId != Guid.Empty &&
            recoveryDerivedCurrentRunId != activeRunId)
        {
            return State(ManualSessionNotice.None);
        }

        ManualSessionRecoveryPayload snapshot = load.Snapshot;
        if (!HasSnapshotGenerationSuccessor(snapshot) || IsStaleSameRunRecovery(snapshot))
        {
            return RejectRecovery();
        }

        if (coordinator.Current is not null &&
            activeRunId != Guid.Empty &&
            recoveryDerivedCurrentRunId == activeRunId)
        {
            ResetRecoveryDerivedLineage();
        }

        switch (snapshot.SessionPhase)
        {
            case ManualSessionPhase.NoSession:
                SetNoSession();
                SeedSnapshotGeneration(snapshot);
                return State(ManualSessionNotice.None);

            case ManualSessionPhase.EditingCheckpoint:
                if (snapshot.ManualRunId == Guid.Empty || snapshot.HighestRevision != 0 || !MatchesTopic(snapshot))
                {
                    return RejectRecovery();
                }

                ApplyLineage(snapshot, ManualSessionPhase.EditingCheckpoint);
                SeedSnapshotGeneration(snapshot);
                return State(ManualSessionNotice.None);

            case ManualSessionPhase.CurrentAdvice:
                return RestoreCurrent(snapshot);

            case ManualSessionPhase.Cleared:
                return RestoreTerminal(snapshot, LocalAdvicePhase.Cleared, ManualSessionPhase.Cleared);

            case ManualSessionPhase.Expired:
                return RestoreTerminal(snapshot, LocalAdvicePhase.Expired, ManualSessionPhase.Expired);

            default:
                return RejectRecovery();
        }
    }

    public SecondScreenSessionState StartNewSession(ManualTopic topic)
    {
        _ = ScenarioFor(topic);
        Guid nextRunId = runIdGenerator.Create();
        if (nextRunId == Guid.Empty)
        {
            return State(ManualSessionNotice.NoActiveSession);
        }

        if (sessionPhase == ManualSessionPhase.CurrentAdvice && activeRunId != Guid.Empty)
        {
            if (!TryGetNextRevision(out long clearRevision))
            {
                return CounterExhaustedState();
            }

            if (!coordinator.TryClear(activeRunId, clearRevision))
            {
                return State(ManualSessionNotice.NoActiveSession);
            }
        }

        activeRunId = nextRunId;
        activeTopic = topic;
        highestRevision = 0;
        checkpoint = EmptyCheckpoint(topic);
        sessionPhase = ManualSessionPhase.EditingCheckpoint;
        recoveryDerivedCurrentRunId = null;
        return PersistOrdinary(ManualSessionNotice.Started);
    }

    public SecondScreenSessionState Submit(ManualCheckpoint submittedCheckpoint)
    {
        if (activeRunId == Guid.Empty || sessionPhase == ManualSessionPhase.NoSession)
        {
            return State(ManualSessionNotice.NoActiveSession);
        }

        if (submittedCheckpoint is null || submittedCheckpoint.Topic != activeTopic)
        {
            return State(submittedCheckpoint is null
                ? ManualSessionNotice.IncompleteCheckpoint
                : ManualSessionNotice.TopicChangeRequiresNewSession);
        }

        if (!IsComplete(submittedCheckpoint))
        {
            return State(ManualSessionNotice.IncompleteCheckpoint);
        }

        if (!TryGetNextRevision(out long revision))
        {
            return CounterExhaustedState();
        }

        DateTimeOffset createdAt = clock.UtcNow;
        DateTimeOffset expiresAt = createdAt + ManualSessionPolicy.CurrentAdviceLifetime;
        ManualScenarioDraft draft = new(
            activeRunId,
            revision,
            ScenarioFor(activeTopic),
            submittedCheckpoint.Topic,
            submittedCheckpoint.Intent,
            submittedCheckpoint.HealthBand,
            submittedCheckpoint.GoldBand,
            submittedCheckpoint.CopiesBand,
            submittedCheckpoint.UnitCostBand,
            LocalFactProvenance.UserEntered);
        SemanticAdvice advice = engine.Evaluate(
            new LocalDecisionSnapshot(draft, fixturePack.Version, createdAt, expiresAt),
            fixturePack,
            createdAt);

        if (advice.Phase != LocalAdvicePhase.Current || !coordinator.TryAccept(advice))
        {
            return State(ManualSessionNotice.IncompleteCheckpoint);
        }

        highestRevision = revision;
        checkpoint = submittedCheckpoint;
        sessionPhase = ManualSessionPhase.CurrentAdvice;
        recoveryDerivedCurrentRunId = null;
        return PersistOrdinary(ManualSessionNotice.Submitted);
    }

    public SecondScreenSessionState ClearCurrentAdvice()
    {
        if (sessionPhase != ManualSessionPhase.CurrentAdvice ||
            activeRunId == Guid.Empty ||
            coordinator.Current is null ||
            coordinator.Current.ManualRunId != activeRunId)
        {
            return State(ManualSessionNotice.NoActiveSession);
        }

        if (!TryGetNextRevision(out long clearRevision))
        {
            return CounterExhaustedState();
        }

        if (!coordinator.TryClear(activeRunId, clearRevision))
        {
            return State(ManualSessionNotice.NoActiveSession);
        }

        highestRevision = clearRevision;
        sessionPhase = ManualSessionPhase.Cleared;
        recoveryDerivedCurrentRunId = null;
        return PersistOrdinary(ManualSessionNotice.Cleared);
    }

    public SecondScreenSessionState EnableDDriveRecovery()
    {
        if (!TryAdvanceSnapshotGeneration())
        {
            return CounterExhaustedState();
        }

        ManualRecoverySaveResult save = recoveryStore.TryEnableProvisioning(BuildSnapshot(clock.UtcNow));
        recoveryStatus = save.Status;
        return State(NoticeForPersistence(save, ManualSessionNotice.RecoveryEnabled));
    }

    public SecondScreenSessionState Refresh()
    {
        ManualPanelProjection projection = projectionHarness.Project(coordinator, clock.UtcNow);
        ManualSessionNotice notice = ManualSessionNotice.None;

        if (projection.Phase == LocalAdvicePhase.Expired && sessionPhase == ManualSessionPhase.CurrentAdvice)
        {
            sessionPhase = ManualSessionPhase.Expired;
            recoveryDerivedCurrentRunId = null;
            notice = ManualSessionNotice.Expired;
        }

        return new SecondScreenSessionState(projection, sessionPhase, recoveryStatus, notice);
    }

    private SecondScreenSessionState RestoreCurrent(ManualSessionRecoveryPayload snapshot)
    {
        ManualCheckpoint restoredCheckpoint = ToCheckpoint(snapshot);
        if (!MatchesTopic(snapshot) ||
            snapshot.ManualRunId == Guid.Empty ||
            snapshot.HighestRevision == long.MaxValue ||
            snapshot.HighestRevision <= 0 ||
            !HasPolicyCurrentLifetime(snapshot) ||
            !IsComplete(restoredCheckpoint))
        {
            return RejectRecovery();
        }

        SemanticAdvice advice = engine.Evaluate(
            new LocalDecisionSnapshot(
                new ManualScenarioDraft(
                    snapshot.ManualRunId,
                    snapshot.HighestRevision,
                    snapshot.FixtureScenarioId,
                    snapshot.Topic,
                    snapshot.Intent,
                    snapshot.HealthBand,
                    snapshot.GoldBand,
                    snapshot.CopiesBand,
                    snapshot.UnitCostBand,
                    snapshot.Provenance),
                snapshot.FixturePackVersion,
                snapshot.CreatedAt,
                snapshot.ExpiresAt),
            fixturePack,
            clock.UtcNow);

        if (advice.Phase == LocalAdvicePhase.Current && coordinator.TryAccept(advice))
        {
            ApplyLineage(snapshot, ManualSessionPhase.CurrentAdvice, restoredCheckpoint);
            SeedSnapshotGeneration(snapshot);
            recoveryDerivedCurrentRunId = snapshot.ManualRunId;
            return State(ManualSessionNotice.None);
        }

        if (advice.Phase == LocalAdvicePhase.Expired)
        {
            return RestoreTerminal(snapshot, LocalAdvicePhase.Expired, ManualSessionPhase.Expired);
        }

        return RejectRecovery();
    }

    private SecondScreenSessionState RestoreTerminal(
        ManualSessionRecoveryPayload snapshot,
        LocalAdvicePhase terminalPhase,
        ManualSessionPhase restoredPhase)
    {
        ManualCheckpoint restoredCheckpoint = ToCheckpoint(snapshot);
        if (!MatchesTopic(snapshot) ||
            snapshot.ManualRunId == Guid.Empty ||
            snapshot.HighestRevision <= 0 ||
            !IsComplete(restoredCheckpoint) ||
            (restoredPhase == ManualSessionPhase.Expired && snapshot.ExpiresAt > clock.UtcNow) ||
            !coordinator.TryRestoreTerminalState(snapshot.ManualRunId, snapshot.HighestRevision, terminalPhase))
        {
            return RejectRecovery();
        }

        ApplyLineage(snapshot, restoredPhase, restoredCheckpoint);
        SeedSnapshotGeneration(snapshot);
        recoveryDerivedCurrentRunId = null;
        return State(restoredPhase == ManualSessionPhase.Expired
            ? ManualSessionNotice.Expired
            : ManualSessionNotice.Cleared);
    }

    private void ApplyLineage(
        ManualSessionRecoveryPayload snapshot,
        ManualSessionPhase restoredPhase,
        ManualCheckpoint? restoredCheckpoint = null)
    {
        activeRunId = snapshot.ManualRunId;
        activeTopic = snapshot.Topic;
        highestRevision = snapshot.HighestRevision;
        checkpoint = restoredCheckpoint ?? ToCheckpoint(snapshot);
        sessionPhase = restoredPhase;
        recoveryDerivedCurrentRunId = null;
    }

    private void SetNoSession()
    {
        activeRunId = Guid.Empty;
        activeTopic = ManualTopic.LossStreakReview;
        highestRevision = 0;
        checkpoint = EmptyCheckpoint(activeTopic);
        sessionPhase = ManualSessionPhase.NoSession;
        recoveryDerivedCurrentRunId = null;
    }

    private SecondScreenSessionState PersistOrdinary(ManualSessionNotice successfulNotice)
    {
        if (!TryAdvanceSnapshotGeneration())
        {
            return CounterExhaustedState();
        }

        ManualRecoverySaveResult save = recoveryStore.TrySave(BuildSnapshot(clock.UtcNow));
        recoveryStatus = save.Status;
        return State(NoticeForPersistence(save, successfulNotice));
    }

    private ManualSessionRecoveryPayload BuildSnapshot(DateTimeOffset now)
    {
        if (sessionPhase == ManualSessionPhase.NoSession)
        {
            return new ManualSessionRecoveryPayload(
                ManualSessionRecoveryContract.SchemaVersion,
                snapshotGeneration,
                Guid.Empty,
                0,
                ManualSessionPhase.NoSession,
                string.Empty,
                ManualTopic.LossStreakReview,
                ManualIntent.Review,
                ManualRiskBand.Unknown,
                ManualRiskBand.Unknown,
                ManualCopiesBand.Unknown,
                ManualUnitCostBand.Unknown,
                LocalFactProvenance.UserEntered,
                fixturePack.Version,
                now,
                now);
        }

        DateTimeOffset createdAt = now;
        DateTimeOffset expiresAt = now;
        if (sessionPhase == ManualSessionPhase.CurrentAdvice && coordinator.Current is SemanticAdvice current)
        {
            expiresAt = current.ExpiresAt;
            createdAt = expiresAt - ManualSessionPolicy.CurrentAdviceLifetime;
        }

        return new ManualSessionRecoveryPayload(
            ManualSessionRecoveryContract.SchemaVersion,
            snapshotGeneration,
            activeRunId,
            highestRevision,
            sessionPhase,
            ScenarioFor(activeTopic),
            activeTopic,
            checkpoint.Intent,
            checkpoint.HealthBand,
            checkpoint.GoldBand,
            checkpoint.CopiesBand,
            checkpoint.UnitCostBand,
            LocalFactProvenance.UserEntered,
            fixturePack.Version,
            createdAt,
            expiresAt);
    }

    private SecondScreenSessionState RejectRecovery()
    {
        DiscardRecoveryDerivedCurrentAfterFailedRestore();
        recoveryStatus = ManualRecoveryStatus.RecoveryRejected;
        return State(ManualSessionNotice.RecoveryRejected);
    }

    private static bool HasSnapshotGenerationSuccessor(ManualSessionRecoveryPayload snapshot) =>
        snapshot.SnapshotGeneration < long.MaxValue;

    private bool IsStaleSameRunRecovery(ManualSessionRecoveryPayload snapshot) =>
        snapshot.ManualRunId != Guid.Empty &&
        snapshot.ManualRunId == activeRunId &&
        snapshot.HighestRevision < highestRevision;

    private static bool HasPolicyCurrentLifetime(ManualSessionRecoveryPayload snapshot) =>
        snapshot.ExpiresAt.UtcDateTime.Ticks - snapshot.CreatedAt.UtcDateTime.Ticks ==
        ManualSessionPolicy.CurrentAdviceLifetime.Ticks;

    private void SeedSnapshotGeneration(ManualSessionRecoveryPayload snapshot) =>
        snapshotGeneration = Math.Max(snapshotGeneration, snapshot.SnapshotGeneration);

    private bool TryGetNextRevision(out long revision)
    {
        revision = 0;
        if (highestRevision == long.MaxValue)
        {
            return false;
        }

        revision = highestRevision + 1;
        return true;
    }

    private bool TryAdvanceSnapshotGeneration()
    {
        if (snapshotGeneration == long.MaxValue)
        {
            return false;
        }

        snapshotGeneration++;
        return true;
    }

    private SecondScreenSessionState CounterExhaustedState()
    {
        recoveryStatus = ManualRecoveryStatus.MemoryOnlyDegraded;
        return State(ManualSessionNotice.RecoveryUnavailable);
    }

    private void DiscardRecoveryDerivedCurrentAfterFailedRestore()
    {
        if (activeRunId == Guid.Empty ||
            recoveryDerivedCurrentRunId != activeRunId ||
            coordinator.Current is null)
        {
            return;
        }

        ResetRecoveryDerivedLineage();
    }

    private void ResetRecoveryDerivedLineage()
    {
        coordinator = new LocalAdviceCoordinator();
        SetNoSession();
    }

    private SecondScreenSessionState State(ManualSessionNotice notice)
    {
        ManualPanelProjection projection = projectionHarness.Project(coordinator, clock.UtcNow);
        if (projection.Phase == LocalAdvicePhase.Expired && sessionPhase == ManualSessionPhase.CurrentAdvice)
        {
            sessionPhase = ManualSessionPhase.Expired;
            if (notice == ManualSessionNotice.None)
            {
                notice = ManualSessionNotice.Expired;
            }
        }

        return new SecondScreenSessionState(projection, sessionPhase, recoveryStatus, notice);
    }

    private static ManualSessionNotice NoticeForPersistence(
        ManualRecoverySaveResult result,
        ManualSessionNotice successfulNotice) => result.Status switch
    {
        ManualRecoveryStatus.RecoveryAvailable => successfulNotice,
        ManualRecoveryStatus.MemoryOnlyDegraded => ManualSessionNotice.RecoveryUnavailable,
        ManualRecoveryStatus.RecoveryRejected => ManualSessionNotice.RecoveryRejected,
        _ => ManualSessionNotice.RecoveryRejected
    };

    private static ManualCheckpoint EmptyCheckpoint(ManualTopic topic) => new(
        topic,
        ManualIntent.Review,
        ManualRiskBand.Unknown,
        ManualRiskBand.Unknown,
        ManualCopiesBand.Unknown,
        ManualUnitCostBand.Unknown);

    private static ManualCheckpoint ToCheckpoint(ManualSessionRecoveryPayload snapshot) => new(
        snapshot.Topic,
        snapshot.Intent,
        snapshot.HealthBand,
        snapshot.GoldBand,
        snapshot.CopiesBand,
        snapshot.UnitCostBand);

    private static bool MatchesTopic(ManualSessionRecoveryPayload snapshot) =>
        string.Equals(snapshot.FixtureScenarioId, ScenarioFor(snapshot.Topic), StringComparison.Ordinal);

    private static bool IsComplete(ManualCheckpoint value)
    {
        if (!Enum.IsDefined(value.Topic) ||
            !Enum.IsDefined(value.Intent) ||
            !Enum.IsDefined(value.HealthBand) ||
            !Enum.IsDefined(value.GoldBand) ||
            !Enum.IsDefined(value.CopiesBand) ||
            !Enum.IsDefined(value.UnitCostBand))
        {
            return false;
        }

        return value.Topic switch
        {
            ManualTopic.LossStreakReview =>
                value.HealthBand != ManualRiskBand.Unknown &&
                value.GoldBand != ManualRiskBand.Unknown &&
                value.Intent is ManualIntent.Review or ManualIntent.PreserveLossStreak or ManualIntent.PrepareToStabilize,
            ManualTopic.RerollReview =>
                value.GoldBand != ManualRiskBand.Unknown &&
                value.CopiesBand != ManualCopiesBand.Unknown &&
                value.UnitCostBand != ManualUnitCostBand.Unknown &&
                value.Intent is ManualIntent.Review or ManualIntent.ConsiderReroll,
            _ => false
        };
    }

    private static string ScenarioFor(ManualTopic topic) => topic switch
    {
        ManualTopic.LossStreakReview => "loss-streak-review-v1",
        ManualTopic.RerollReview => "reroll-review-v1",
        _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, null)
    };
}
