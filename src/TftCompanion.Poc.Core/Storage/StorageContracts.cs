namespace TftCompanion.Poc.Core.Storage;

public enum StorageHealth
{
    Available,
    MemoryOnlyDegraded,
    PersistUnavailable,
    IntegrityDegraded
}

public sealed record FinalPath(string VolumeGuid, string CanonicalPath);

public sealed record StorageRootProbe(
    bool Success,
    FinalPath? ResolvedRoot,
    StorageHealth FailureHealth,
    string FailureCode);

public sealed record StorageWriteResult(
    bool Success,
    StorageHealth Health,
    string FailureCode);

public interface IStorageFileSystem
{
    StorageRootProbe ProbeCanonicalRoot();

    StorageWriteResult TryWritePocStatus(ReadOnlyMemory<byte> utf8);
}
