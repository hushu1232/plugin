using NUnit.Framework;
using TftCompanion.Poc.Core.Storage;
using TftCompanion.Poc.Tests.TestSupport;

namespace TftCompanion.Poc.Tests;

[TestFixture]
public sealed class StorageRootPolicyTests
{
    [Test]
    public void windows_storage_adapter_resolves_its_volume_guid_from_the_d_volume_mount_root()
    {
        string repositoryRoot = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "TftCompanion.Poc.Core",
            "Storage",
            "WindowsStorageFileSystem.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("GetVolumeGuid(DVolumeRoot)"));
            Assert.That(source, Does.Not.Contain("GetVolumeGuid(\n                parentDir"));
            Assert.That(source, Does.Not.Contain("File.WriteAllBytes"));
            Assert.That(source, Does.Contain("ReadFinalPath(handle, VolumeNameGuid)"));
            Assert.That(source.IndexOf("status = Inspect(handle)", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(source.IndexOf("status = Inspect(handle)", StringComparison.Ordinal),
                Is.LessThan(source.IndexOf("stream.SetLength", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void windows_storage_adapter_never_creates_runtime_paths_before_validating_a_same_handle_write_target()
    {
        string repositoryRoot = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "TftCompanion.Poc.Core",
            "Storage",
            "WindowsStorageFileSystem.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("FileReadAttributes"));
            Assert.That(source, Does.Contain("FileWriteData"));
            Assert.That(source, Does.Contain("FileFlagOpenReparsePoint"));
            Assert.That(source, Does.Contain("OpenExisting"));
            Assert.That(source, Does.Not.Contain("Directory.CreateDirectory"));
            Assert.That(source, Does.Not.Contain("FileMode.OpenOrCreate"));
            Assert.That(source, Does.Not.Contain("FileMode.Create"));
            Assert.That(source.IndexOf("ValidateStatusFileHandle", StringComparison.Ordinal),
                Is.LessThan(source.IndexOf("stream.SetLength", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void unavailable_d_root_enters_memory_only_without_any_write()
    {
        FakeStorageFileSystem fileSystem = FakeStorageFileSystem.Unavailable("D_ROOT_UNAVAILABLE");
        StorageRootPolicy policy = new(fileSystem);

        StorageHealth health = policy.Initialize();
        bool persisted = policy.TryPersist(new SanitizedPocStatus(
            RuntimeEpoch: "runtime-1",
            BridgeOnline: false,
            RenderOnline: false,
            MatchObserved: false,
            RoundObserved: false,
            Freshness: "Unknown",
            GapState: "None",
            LastErrorCode: "NONE"));

        Assert.Multiple(() =>
        {
            Assert.That(health, Is.EqualTo(StorageHealth.MemoryOnlyDegraded));
            Assert.That(persisted, Is.False);
            Assert.That(fileSystem.WriteAttempts, Is.Empty);
        });
    }

    [Test]
    public void resolved_root_outside_d_enters_integrity_degraded_without_fallback()
    {
        FakeStorageFileSystem fileSystem = FakeStorageFileSystem.IntegrityFailure(
            new FinalPath(@"\\?\Volume{c-drive}\", @"\\?\C:\escaped\TFTCompanion"));
        StorageRootPolicy policy = new(fileSystem);

        StorageHealth health = policy.Initialize();
        bool persisted = policy.TryPersist(SanitizedPocStatus.Empty("runtime-2"));

        Assert.Multiple(() =>
        {
            Assert.That(health, Is.EqualTo(StorageHealth.IntegrityDegraded));
            Assert.That(persisted, Is.False);
            Assert.That(fileSystem.WriteAttempts, Is.Empty);
        });
    }

    [Test]
    public void write_failure_after_validation_enters_persist_unavailable_and_never_retries_elsewhere()
    {
        FakeStorageFileSystem fileSystem = FakeStorageFileSystem.ValidRootThenWriteFailure("STATUS_WRITE_DENIED");
        StorageRootPolicy policy = new(fileSystem);

        Assert.That(policy.Initialize(), Is.EqualTo(StorageHealth.Available));
        Assert.That(policy.TryPersist(SanitizedPocStatus.Empty("runtime-3")), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(policy.Health, Is.EqualTo(StorageHealth.PersistUnavailable));
            Assert.That(fileSystem.WriteAttempts, Has.Count.EqualTo(1));
            Assert.That(fileSystem.WriteAttempts[0], Is.EqualTo("poc-status.json"));
        });
    }

    [Test]
    public void runtime_integrity_failure_after_initialization_preserves_integrity_health()
    {
        FakeStorageFileSystem fileSystem = FakeStorageFileSystem.ValidRootThenWriteFailure("ROOT_OUTSIDE_D");
        StorageRootPolicy policy = new(fileSystem);

        Assert.That(policy.Initialize(), Is.EqualTo(StorageHealth.Available));
        Assert.That(policy.TryPersist(SanitizedPocStatus.Empty("runtime-5")), Is.False);

        Assert.That(policy.Health, Is.EqualTo(StorageHealth.IntegrityDegraded));
    }

    [Test]
    public void successful_status_is_bounded_and_contains_no_raw_payload_or_identity_fields()
    {
        FakeStorageFileSystem fileSystem = FakeStorageFileSystem.ValidRoot();
        StorageRootPolicy policy = new(fileSystem);
        SanitizedPocStatus status = new(
            RuntimeEpoch: "runtime-4",
            BridgeOnline: true,
            RenderOnline: true,
            MatchObserved: true,
            RoundObserved: true,
            Freshness: "Fresh",
            GapState: "None",
            LastErrorCode: "NONE");

        Assert.That(policy.Initialize(), Is.EqualTo(StorageHealth.Available));
        Assert.That(policy.TryPersist(status), Is.True);

        string json = fileSystem.LastUtf8Document!;
        Assert.Multiple(() =>
        {
            Assert.That(System.Text.Encoding.UTF8.GetByteCount(json), Is.LessThanOrEqualTo(8_192));
            Assert.That(json, Does.Not.Contain("payload"));
            Assert.That(json, Does.Not.Contain("pseudo_match"));
            Assert.That(json, Does.Not.Contain("summoner"));
            Assert.That(json, Does.Not.Contain("token"));
            Assert.That(json, Does.Not.Contain("origin"));
        });
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TftCompanion.Poc.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the TFT Companion repository root.");
    }
}
