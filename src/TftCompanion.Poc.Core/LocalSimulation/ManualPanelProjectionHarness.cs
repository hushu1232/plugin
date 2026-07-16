namespace TftCompanion.Poc.Core.LocalSimulation;

public sealed record ManualPanelProjection(
    Guid? ManualRunId,
    long? ManualRevision,
    LocalAdvicePhase Phase,
    LocalPrecisionState Precision,
    string SourceLabel,
    string FixturePackVersion,
    string? MessageKey,
    string? RenderedText,
    string ReasonCode,
    DateTimeOffset? ExpiresAt,
    bool IsCurrent);

public sealed class ManualPanelProjectionHarness
{
    private const string SourceLabel = "Manual / FixtureOnly";

    private readonly EmbeddedFixtureExpressionSkill _expressionSkill;

    public ManualPanelProjectionHarness(EmbeddedFixtureExpressionSkill expressionSkill)
    {
        _expressionSkill = expressionSkill ?? throw new ArgumentNullException(nameof(expressionSkill));
    }

    public ManualPanelProjection Project(LocalAdviceCoordinator coordinator, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(coordinator);

        coordinator.ExpireIfNeeded(now);

        SemanticAdvice? current = coordinator.Current;

        if (current is null)
        {
            LocalAdvicePhase phase = coordinator.LastPhase;
            LocalPrecisionState precision = phase is LocalAdvicePhase.Expired or LocalAdvicePhase.Cleared
                ? LocalPrecisionState.Degraded
                : LocalPrecisionState.Unknown;
            string reasonCode = phase switch
            {
                LocalAdvicePhase.Expired => "fixture.state-invalidated",
                LocalAdvicePhase.Cleared => "fixture.cleared",
                _ => "fixture.no-current-advice"
            };

            return new ManualPanelProjection(
                ManualRunId: null,
                ManualRevision: null,
                Phase: phase,
                Precision: precision,
                SourceLabel: SourceLabel,
                FixturePackVersion: string.Empty,
                MessageKey: null,
                RenderedText: null,
                ReasonCode: reasonCode,
                ExpiresAt: null,
                IsCurrent: false);
        }

        if (!_expressionSkill.TryRender(current, out RenderedFixtureAdvice? rendered) || rendered is null)
        {
            return new ManualPanelProjection(
                ManualRunId: current.ManualRunId,
                ManualRevision: current.ManualRevision,
                Phase: LocalAdvicePhase.Degraded,
                Precision: LocalPrecisionState.Degraded,
                SourceLabel: SourceLabel,
                FixturePackVersion: current.FixturePackVersion,
                MessageKey: null,
                RenderedText: null,
                ReasonCode: "fixture.expression-unavailable",
                ExpiresAt: current.ExpiresAt,
                IsCurrent: false);
        }

        return new ManualPanelProjection(
            ManualRunId: current.ManualRunId,
            ManualRevision: current.ManualRevision,
            Phase: LocalAdvicePhase.Current,
            Precision: current.Precision,
            SourceLabel: SourceLabel,
            FixturePackVersion: current.FixturePackVersion,
            MessageKey: rendered.MessageKey,
            RenderedText: rendered.Text,
            ReasonCode: current.ReasonCode,
            ExpiresAt: current.ExpiresAt,
            IsCurrent: true);
    }
}
