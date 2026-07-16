using System.Text.Json;
using TftCompanion.Poc.Core.Protocol;

namespace TftCompanion.Poc.Core.Storage;

public sealed class StorageRootPolicy
{
    private readonly IStorageFileSystem fileSystem;

    public StorageRootPolicy(IStorageFileSystem fileSystem)
    {
        this.fileSystem = fileSystem;
    }

    public StorageHealth Health { get; private set; } = StorageHealth.MemoryOnlyDegraded;

    public StorageHealth Initialize()
    {
        StorageRootProbe probe = fileSystem.ProbeCanonicalRoot();
        Health = probe.Success ? StorageHealth.Available : probe.FailureHealth;
        return Health;
    }

    public bool TryPersist(SanitizedPocStatus status)
    {
        if (Health != StorageHealth.Available)
            return false;

        byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(status);
        if (utf8.Length > ProtocolConstants.MaximumStatusDocumentBytes)
        {
            Health = StorageHealth.PersistUnavailable;
            return false;
        }

        StorageWriteResult writeResult = fileSystem.TryWritePocStatus(utf8);
        if (writeResult.Success)
            return true;

        Health = writeResult.Health;
        return false;
    }
}
