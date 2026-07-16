using System.Collections.Frozen;

namespace TftCompanion.Poc.Core.LocalSimulation;

public static class FixtureExpressionSkillContract
{
    public const string Version = "render-tft-companion-advice-fixture-v1";
}

public sealed record LocalDecisionSnapshot(
    ManualScenarioDraft Draft,
    string FixturePackVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record LocalActionCandidate
{
    public LocalActionCandidate(
        string CandidateId,
        string MessageKey,
        string ReasonCode,
        int Priority,
        IReadOnlyDictionary<string, string> LockedSlots)
    {
        ArgumentNullException.ThrowIfNull(CandidateId);
        ArgumentNullException.ThrowIfNull(MessageKey);
        ArgumentNullException.ThrowIfNull(ReasonCode);
        ArgumentNullException.ThrowIfNull(LockedSlots);

        this.CandidateId = CandidateId;
        this.MessageKey = MessageKey;
        this.ReasonCode = ReasonCode;
        this.Priority = Priority;
        this.LockedSlots = LockedSlots.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public string CandidateId { get; }
    public string MessageKey { get; }
    public string ReasonCode { get; }
    public int Priority { get; }
    public IReadOnlyDictionary<string, string> LockedSlots { get; }
}

public sealed record SemanticAdvice(
    Guid AdviceId,
    Guid ManualRunId,
    long ManualRevision,
    LocalAdvicePhase Phase,
    LocalPrecisionState Precision,
    string FixturePackVersion,
    LocalActionCandidate? PrimaryAction,
    IReadOnlyList<LocalActionCandidate> SupportingActions,
    LocalActionCandidate? Observation,
    string ReasonCode,
    string SemanticDigest,
    string SkillVersion,
    DateTimeOffset ExpiresAt);

public enum LocalCapabilityStatus { Eligible, Unknown, Degraded }

public sealed record LocalCapabilityResult(
    LocalCapabilityStatus Status,
    string ReasonCode,
    LocalPrecisionState Precision);
