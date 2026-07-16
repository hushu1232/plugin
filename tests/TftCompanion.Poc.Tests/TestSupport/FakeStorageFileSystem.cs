using System.Text;
using TftCompanion.Poc.Core.Storage;

namespace TftCompanion.Poc.Tests.TestSupport;

/// <summary>
/// In-memory fake that never touches the real file system.
/// Records every write attempt path and the last UTF-8 document.
/// </summary>
public sealed class FakeStorageFileSystem : IStorageFileSystem
{
    private readonly StorageRootProbe probeResult;
    private readonly Func<StorageWriteResult>? writeOutcome;

    private FakeStorageFileSystem(
        StorageRootProbe probeResult,
        Func<StorageWriteResult>? writeOutcome)
    {
        this.probeResult = probeResult;
        this.writeOutcome = writeOutcome;
    }

    public List<string> WriteAttempts { get; } = [];
    public string? LastUtf8Document { get; private set; }

    public static FakeStorageFileSystem Unavailable(string failureCode) =>
        new(
            new StorageRootProbe(
                Success: false,
                ResolvedRoot: null,
                FailureHealth: StorageHealth.MemoryOnlyDegraded,
                FailureCode: failureCode),
            writeOutcome: null);

    public static FakeStorageFileSystem IntegrityFailure(FinalPath escapedPath) =>
        new(
            new StorageRootProbe(
                Success: false,
                ResolvedRoot: escapedPath,
                FailureHealth: StorageHealth.IntegrityDegraded,
                FailureCode: "ROOT_OUTSIDE_D"),
            writeOutcome: null);

    public static FakeStorageFileSystem ValidRootThenWriteFailure(string failureCode)
    {
        FinalPath root = new(
            VolumeGuid: @"\\?\Volume{d-drive}\",
            CanonicalPath: @"\\?\D:\AlifeData\TFTCompanion");
        return new(
            new StorageRootProbe(
                Success: true,
                ResolvedRoot: root,
                FailureHealth: StorageHealth.Available,
                FailureCode: "NONE"),
            writeOutcome: () => new StorageWriteResult(
                Success: false,
                Health: FailureHealthFor(failureCode),
                FailureCode: failureCode));
    }

    public static FakeStorageFileSystem ValidRoot()
    {
        FinalPath root = new(
            VolumeGuid: @"\\?\Volume{d-drive}\",
            CanonicalPath: @"\\?\D:\AlifeData\TFTCompanion");
        return new(
            new StorageRootProbe(
                Success: true,
                ResolvedRoot: root,
                FailureHealth: StorageHealth.Available,
                FailureCode: "NONE"),
            writeOutcome: () => new StorageWriteResult(
                Success: true,
                Health: StorageHealth.Available,
                FailureCode: "NONE"));
    }

    public StorageRootProbe ProbeCanonicalRoot() => probeResult;

    public StorageWriteResult TryWritePocStatus(ReadOnlyMemory<byte> utf8)
    {
        WriteAttempts.Add("poc-status.json");

        if (writeOutcome is null)
        {
            return new StorageWriteResult(
                Success: false,
                Health: probeResult.FailureHealth,
                FailureCode: probeResult.FailureCode);
        }

        StorageWriteResult result = writeOutcome();
        if (!result.Success)
            return result;

        LastUtf8Document = Encoding.UTF8.GetString(utf8.Span);
        return result;
    }

    private static StorageHealth FailureHealthFor(string failureCode) => failureCode switch
    {
        "D_ROOT_UNAVAILABLE" or "ROOT_NOT_PROVISIONED" or "STATUS_NOT_PROVISIONED" =>
            StorageHealth.MemoryOnlyDegraded,
        "ROOT_OUTSIDE_D" or "VOLUME_MISMATCH" or "SEGMENT_IS_REPARSE_POINT" or
            "FILE_PATH_ESCAPE" or "FILE_IS_REPARSE_POINT" => StorageHealth.IntegrityDegraded,
        _ => StorageHealth.PersistUnavailable
    };
}
