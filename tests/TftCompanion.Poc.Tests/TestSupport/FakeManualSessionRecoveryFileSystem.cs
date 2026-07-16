using System;
using System.Collections.Generic;
using TftCompanion.SecondScreen.Recovery;

namespace TftCompanion.Poc.Tests.TestSupport;

/// <summary>
/// Pure in-memory recovery fake that records fixed logical-name operations.
/// </summary>
public sealed class FakeManualSessionRecoveryFileSystem : IManualSessionRecoveryFileSystem
{
    private ManualRecoveryFileResult _readResult = Failure(
        ManualRecoveryStatus.MemoryOnlyDegraded,
        "RECOVERY_NOT_PROVISIONED");

    private ManualRecoveryFileResult _writeResult = Success();

    private ManualRecoveryFileResult _provisionResult = Success();

    public ManualRecoveryFileResult ReadResult
    {
        get => CopyResult(_readResult);
        set => _readResult = CopyResult(value);
    }

    public ManualRecoveryFileResult WriteResult
    {
        get => CopyResult(_writeResult);
        set => _writeResult = CopyResult(value);
    }

    public ManualRecoveryFileResult ProvisionResult
    {
        get => CopyResult(_provisionResult);
        set => _provisionResult = CopyResult(value);
    }

    public List<string> Operations { get; } = [];

    public List<int> RequestedMaximumBytes { get; } = [];

    public List<byte[]> WrittenDocuments { get; } = [];

    public List<string> CreatedPaths { get; } = [];

    public List<string> ObservedLogicalNames { get; } = [];

    public ManualRecoveryFileResult TryReadExisting(int maximumBytes)
    {
        Operations.Add("ReadExisting");
        RequestedMaximumBytes.Add(maximumBytes);
        ObservedLogicalNames.Add(ManualSessionRecoveryContract.FixedFileName);
        return CopyResult(_readResult);
    }

    public ManualRecoveryFileResult TryWriteExisting(ReadOnlyMemory<byte> utf8)
    {
        Operations.Add("WriteExisting");
        ObservedLogicalNames.Add(ManualSessionRecoveryContract.FixedFileName);
        WrittenDocuments.Add(utf8.ToArray());
        return CopyResult(_writeResult);
    }

    public ManualRecoveryFileResult TryProvisionFixedFile()
    {
        Operations.Add("ProvisionFixedFile");
        ObservedLogicalNames.Add(ManualSessionRecoveryContract.FixedFileName);
        return CopyResult(_provisionResult);
    }

    public static ManualRecoveryFileResult Success(byte[]? utf8Document = null) => new(
        Success: true,
        Status: ManualRecoveryStatus.RecoveryAvailable,
        FailureCode: "NONE",
        Utf8Document: CopyBytes(utf8Document));

    public static ManualRecoveryFileResult Failure(ManualRecoveryStatus status, string failureCode) => new(
        Success: false,
        Status: status,
        FailureCode: failureCode,
        Utf8Document: null);

    private static ManualRecoveryFileResult CopyResult(ManualRecoveryFileResult result) => new(
        result.Success,
        result.Status,
        result.FailureCode,
        CopyBytes(result.Utf8Document));

    private static byte[]? CopyBytes(byte[]? bytes) =>
        bytes is null ? null : (byte[])bytes.Clone();
}
