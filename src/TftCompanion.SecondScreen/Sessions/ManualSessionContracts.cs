using TftCompanion.Poc.Core.LocalSimulation;
using TftCompanion.SecondScreen.Recovery;

namespace TftCompanion.SecondScreen.Sessions;

public static class ManualSessionPolicy
{
    public static readonly TimeSpan CurrentAdviceLifetime = TimeSpan.FromMinutes(2);
}

public enum ManualSessionPhase
{
    NoSession,
    EditingCheckpoint,
    CurrentAdvice,
    Cleared,
    Expired
}

public enum ManualSessionNotice
{
    None,
    Started,
    Submitted,
    IncompleteCheckpoint,
    TopicChangeRequiresNewSession,
    NoActiveSession,
    Cleared,
    Expired,
    RecoveryEnabled,
    RecoveryUnavailable,
    RecoveryRejected
}

public sealed record ManualCheckpoint(
    ManualTopic Topic,
    ManualIntent Intent,
    ManualRiskBand HealthBand,
    ManualRiskBand GoldBand,
    ManualCopiesBand CopiesBand,
    ManualUnitCostBand UnitCostBand);

public interface ISecondScreenClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IManualRunIdGenerator
{
    Guid Create();
}

public sealed record SecondScreenSessionState(
    ManualPanelProjection Projection,
    ManualSessionPhase SessionPhase,
    ManualRecoveryStatus RecoveryStatus,
    ManualSessionNotice Notice);
