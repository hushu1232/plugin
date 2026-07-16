using TftCompanion.Poc.Core.LocalSimulation;
using TftCompanion.SecondScreen.Sessions;

namespace TftCompanion.SecondScreen.Recovery;

public static class ManualSessionRecoveryContract
{
    public const string SchemaVersion = "manual-session-v1";
    public const string FixedFileName = "manual-session-v1.json";
    public const int MaximumSnapshotBytes = 8_192;
}

public enum ManualRecoveryStatus
{
    MemoryOnlyDegraded,
    RecoveryAvailable,
    RecoveryRejected
}

public sealed record ManualSessionRecoveryPayload(
    string SchemaVersion,
    long SnapshotGeneration,
    Guid ManualRunId,
    long HighestRevision,
    ManualSessionPhase SessionPhase,
    string FixtureScenarioId,
    ManualTopic Topic,
    ManualIntent Intent,
    ManualRiskBand HealthBand,
    ManualRiskBand GoldBand,
    ManualCopiesBand CopiesBand,
    ManualUnitCostBand UnitCostBand,
    LocalFactProvenance Provenance,
    string FixturePackVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record ManualSessionRecoveryDocument(
    ManualSessionRecoveryPayload Payload,
    string CanonicalPayloadDigest);

public sealed record ManualRecoveryFileResult(
    bool Success,
    ManualRecoveryStatus Status,
    string FailureCode,
    byte[]? Utf8Document);

public sealed record ManualRecoveryLoadResult(
    ManualRecoveryStatus Status,
    ManualSessionRecoveryPayload? Snapshot,
    string FailureCode);

public sealed record ManualRecoverySaveResult(
    ManualRecoveryStatus Status,
    string FailureCode);

public interface IManualSessionRecoveryFileSystem
{
    ManualRecoveryFileResult TryReadExisting(int maximumBytes);

    ManualRecoveryFileResult TryWriteExisting(ReadOnlyMemory<byte> utf8);

    ManualRecoveryFileResult TryProvisionFixedFile();
}
