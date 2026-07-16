namespace TftCompanion.SecondScreen.Recovery;

/// <summary>
/// Coordinates canonical recovery snapshots with a fixed, pre-provisioned file.
/// The store never creates a location or retries to an alternate location.
/// </summary>
public sealed class ManualSessionRecoveryStore
{
    private const string NoneFailureCode = "NONE";
    private const string InvalidFileSystemResultFailureCode = "FILE_SYSTEM_RESULT_INVALID";
    private const string MissingDocumentFailureCode = "RECOVERY_DOCUMENT_MISSING";
    private const string ReadUnavailableFailureCode = "RECOVERY_READ_UNAVAILABLE";
    private const string WriteUnavailableFailureCode = "RECOVERY_WRITE_UNAVAILABLE";
    private const string ProvisionUnavailableFailureCode = "RECOVERY_PROVISION_UNAVAILABLE";
    private const string DocumentTooLargeFailureCode = "DOCUMENT_TOO_LARGE";
    private const string SnapshotEncodeFailureCode = "SNAPSHOT_ENCODE_FAILED";

    private readonly IManualSessionRecoveryFileSystem _fileSystem;
    private readonly ManualSessionRecoveryCodec _codec;

    public ManualSessionRecoveryStore(
        IManualSessionRecoveryFileSystem fileSystem,
        ManualSessionRecoveryCodec codec)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
    }

    public ManualRecoveryLoadResult TryLoad()
    {
        ManualRecoveryFileResult fileResult;

        try
        {
            fileResult = _fileSystem.TryReadExisting(ManualSessionRecoveryContract.MaximumSnapshotBytes);
        }
        catch (Exception)
        {
            return new ManualRecoveryLoadResult(
                ManualRecoveryStatus.MemoryOnlyDegraded,
                Snapshot: null,
                ReadUnavailableFailureCode);
        }

        if (IsSuccessHeader(fileResult))
        {
            if (fileResult.Utf8Document is null)
            {
                return RejectedLoad(MissingDocumentFailureCode);
            }

            if (fileResult.Utf8Document.Length > ManualSessionRecoveryContract.MaximumSnapshotBytes)
            {
                return RejectedLoad(DocumentTooLargeFailureCode);
            }

            if (_codec.TryDecode(fileResult.Utf8Document, out ManualSessionRecoveryPayload? snapshot, out string failureCode) &&
                snapshot is not null)
            {
                return new ManualRecoveryLoadResult(
                    ManualRecoveryStatus.RecoveryAvailable,
                    snapshot,
                    NoneFailureCode);
            }

            return RejectedLoad(failureCode);
        }

        if (!IsFailureResult(fileResult))
        {
            return RejectedLoad(InvalidFileSystemResultFailureCode);
        }

        return new ManualRecoveryLoadResult(fileResult.Status, Snapshot: null, fileResult.FailureCode);
    }

    public ManualRecoverySaveResult TrySave(ManualSessionRecoveryPayload snapshot)
    {
        if (!TryPrepare(snapshot, out byte[] canonicalDocument, out string preflightFailureCode))
        {
            return RejectedSave(preflightFailureCode);
        }

        ManualRecoveryFileResult fileResult;

        try
        {
            fileResult = _fileSystem.TryWriteExisting(canonicalDocument);
        }
        catch (Exception)
        {
            return new ManualRecoverySaveResult(
                ManualRecoveryStatus.MemoryOnlyDegraded,
                WriteUnavailableFailureCode);
        }

        return ToSaveResult(fileResult);
    }

    public ManualRecoverySaveResult TryEnableProvisioning(ManualSessionRecoveryPayload snapshot)
    {
        if (!TryPrepare(snapshot, out byte[] canonicalDocument, out string preflightFailureCode))
        {
            return RejectedSave(preflightFailureCode);
        }

        ManualRecoveryFileResult provisionResult;

        try
        {
            provisionResult = _fileSystem.TryProvisionFixedFile();
        }
        catch (Exception)
        {
            return new ManualRecoverySaveResult(
                ManualRecoveryStatus.MemoryOnlyDegraded,
                ProvisionUnavailableFailureCode);
        }

        if (!IsSuccessfulOperationResult(provisionResult))
        {
            return ToSaveResult(provisionResult);
        }

        ManualRecoveryFileResult writeResult;

        try
        {
            writeResult = _fileSystem.TryWriteExisting(canonicalDocument);
        }
        catch (Exception)
        {
            return new ManualRecoverySaveResult(
                ManualRecoveryStatus.MemoryOnlyDegraded,
                WriteUnavailableFailureCode);
        }

        return ToSaveResult(writeResult);
    }

    private bool TryPrepare(
        ManualSessionRecoveryPayload snapshot,
        out byte[] canonicalDocument,
        out string failureCode)
    {
        canonicalDocument = [];
        failureCode = SnapshotEncodeFailureCode;

        try
        {
            canonicalDocument = _codec.Encode(snapshot);
        }
        catch (ArgumentNullException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (canonicalDocument.Length > ManualSessionRecoveryContract.MaximumSnapshotBytes)
        {
            failureCode = DocumentTooLargeFailureCode;
            return false;
        }

        if (!_codec.TryDecode(canonicalDocument, out _, out failureCode))
        {
            return false;
        }

        return true;
    }

    private static ManualRecoveryLoadResult RejectedLoad(string failureCode) => new(
        ManualRecoveryStatus.RecoveryRejected,
        Snapshot: null,
        failureCode);

    private static ManualRecoverySaveResult RejectedSave(string failureCode) => new(
        ManualRecoveryStatus.RecoveryRejected,
        failureCode);

    private static ManualRecoverySaveResult ToSaveResult(ManualRecoveryFileResult fileResult)
    {
        if (IsSuccessfulOperationResult(fileResult))
        {
            return new ManualRecoverySaveResult(
                ManualRecoveryStatus.RecoveryAvailable,
                NoneFailureCode);
        }

        if (IsFailureResult(fileResult))
        {
            return new ManualRecoverySaveResult(fileResult.Status, fileResult.FailureCode);
        }

        return RejectedSave(InvalidFileSystemResultFailureCode);
    }

    private static bool IsSuccessHeader(ManualRecoveryFileResult result) =>
        result.Success &&
        result.Status == ManualRecoveryStatus.RecoveryAvailable &&
        string.Equals(result.FailureCode, NoneFailureCode, StringComparison.Ordinal);

    private static bool IsSuccessfulOperationResult(ManualRecoveryFileResult result) =>
        IsSuccessHeader(result) &&
        result.Utf8Document is null;

    private static bool IsFailureResult(ManualRecoveryFileResult result) =>
        !result.Success &&
        result.Status is ManualRecoveryStatus.MemoryOnlyDegraded or ManualRecoveryStatus.RecoveryRejected &&
        IsNamedFailureCode(result.FailureCode) &&
        !string.Equals(result.FailureCode, NoneFailureCode, StringComparison.Ordinal) &&
        result.Utf8Document is null;

    private static bool IsNamedFailureCode(string failureCode)
    {
        if (string.IsNullOrWhiteSpace(failureCode))
        {
            return false;
        }

        foreach (char character in failureCode)
        {
            bool isUppercaseLetter = character is >= 'A' and <= 'Z';
            bool isDigit = character is >= '0' and <= '9';

            if (!isUppercaseLetter && !isDigit && character != '_')
            {
                return false;
            }
        }

        return true;
    }
}
