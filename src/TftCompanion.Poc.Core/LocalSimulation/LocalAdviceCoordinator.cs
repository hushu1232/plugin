namespace TftCompanion.Poc.Core.LocalSimulation;

public sealed class LocalAdviceCoordinator
{
    private readonly Dictionary<Guid, long> _highWaterByRun = new();
    private readonly HashSet<Guid> _retiredRuns = new();
    private Guid? _activeRunId;

    public SemanticAdvice? Current { get; private set; }

    public LocalAdvicePhase LastPhase { get; private set; } = LocalAdvicePhase.Unknown;

    public bool TryAccept(SemanticAdvice advice)
    {
        ArgumentNullException.ThrowIfNull(advice);

        if (advice.Phase != LocalAdvicePhase.Current ||
            advice.ManualRunId == Guid.Empty ||
            advice.ManualRevision <= 0 ||
            advice.SupportingActions is null)
        {
            return false;
        }

        Guid runId = advice.ManualRunId;
        long revision = advice.ManualRevision;

        if (_retiredRuns.Contains(runId))
        {
            return false;
        }

        if (_highWaterByRun.TryGetValue(runId, out long highWater) && revision <= highWater)
        {
            return false;
        }

        if (_activeRunId is Guid activeRunId && activeRunId != runId)
        {
            _retiredRuns.Add(activeRunId);
        }

        SemanticAdvice accepted = advice with
        {
            SupportingActions = Array.AsReadOnly(advice.SupportingActions.ToArray())
        };

        Current = accepted;
        _activeRunId = runId;
        _highWaterByRun[runId] = revision;
        LastPhase = LocalAdvicePhase.Current;
        return true;
    }

    public bool TryClear(Guid manualRunId, long revision)
    {
        if (Current is null || Current.ManualRunId != manualRunId)
        {
            return false;
        }

        long highWater = _highWaterByRun.TryGetValue(manualRunId, out long stored) ? stored : Current.ManualRevision;
        if (revision <= highWater)
        {
            return false;
        }

        _highWaterByRun[manualRunId] = revision;
        Current = null;
        LastPhase = LocalAdvicePhase.Cleared;
        return true;
    }

    public SemanticAdvice? ExpireIfNeeded(DateTimeOffset now)
    {
        if (Current is null || now < Current.ExpiresAt)
        {
            return null;
        }

        SemanticAdvice expired = Current with
        {
            Phase = LocalAdvicePhase.Expired,
            Precision = LocalPrecisionState.Degraded,
            PrimaryAction = null,
            SupportingActions = Array.Empty<LocalActionCandidate>(),
            Observation = null,
            ReasonCode = "fixture.state-invalidated"
        };
        Current = null;
        LastPhase = LocalAdvicePhase.Expired;
        return expired;
    }

    public bool TryRestoreTerminalState(
        Guid manualRunId,
        long highWaterRevision,
        LocalAdvicePhase phase)
    {
        if (Current is not null ||
            manualRunId == Guid.Empty ||
            highWaterRevision <= 0 ||
            phase is not (LocalAdvicePhase.Cleared or LocalAdvicePhase.Expired) ||
            _retiredRuns.Contains(manualRunId))
        {
            return false;
        }

        if (_highWaterByRun.TryGetValue(manualRunId, out long knownHighWater) &&
            highWaterRevision < knownHighWater)
        {
            return false;
        }

        if (_activeRunId is Guid activeRunId && activeRunId != manualRunId)
        {
            _retiredRuns.Add(activeRunId);
        }

        _highWaterByRun[manualRunId] = highWaterRevision;
        _activeRunId = manualRunId;
        Current = null;
        LastPhase = phase;
        return true;
    }
}
