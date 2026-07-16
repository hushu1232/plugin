using System;
using System.IO;
using NUnit.Framework;
using TftCompanion.Poc.Core.LocalSimulation;
using TftCompanion.Poc.Tests.TestSupport;
using TftCompanion.SecondScreen.Recovery;
using TftCompanion.SecondScreen.Sessions;

namespace TftCompanion.Poc.Tests;

[TestFixture]
public sealed class ManualSessionRecoveryStoreTests
{
    [Test]
    public void ordinary_save_writes_only_the_preprovisioned_fixed_file_with_a_valid_canonical_document()
    {
        ManualSessionRecoveryCodec codec = new();
        FakeManualSessionRecoveryFileSystem fileSystem = new();
        ManualSessionRecoveryStore store = new(fileSystem, codec);

        ManualRecoverySaveResult result = store.TrySave(ValidCurrent);
        byte[] writtenDocument = fileSystem.WrittenDocuments[0];
        bool decodeSuccess = codec.TryDecode(
            writtenDocument,
            out ManualSessionRecoveryPayload? decoded,
            out string decodeFailureCode);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ManualRecoveryStatus.RecoveryAvailable));
            Assert.That(result.FailureCode, Is.EqualTo("NONE"));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "WriteExisting" }));
            Assert.That(fileSystem.Operations, Does.Not.Contain("ProvisionFixedFile"));
            Assert.That(fileSystem.CreatedPaths, Is.Empty);
            Assert.That(fileSystem.WrittenDocuments, Has.Count.EqualTo(1));
            Assert.That(writtenDocument, Is.EqualTo(codec.Encode(ValidCurrent)));
            Assert.That(decodeSuccess, Is.True);
            Assert.That(decoded, Is.EqualTo(ValidCurrent));
            Assert.That(decodeFailureCode, Is.EqualTo("NONE"));
        });
    }

    [Test]
    public void explicit_enable_validates_the_preprovisioned_file_then_writes_without_creating_it()
    {
        ManualSessionRecoveryCodec codec = new();
        FakeManualSessionRecoveryFileSystem fileSystem = new();
        ManualSessionRecoveryStore store = new(fileSystem, codec);

        ManualRecoverySaveResult result = store.TryEnableProvisioning(ValidCurrent);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ManualRecoveryStatus.RecoveryAvailable));
            Assert.That(result.FailureCode, Is.EqualTo("NONE"));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ProvisionFixedFile", "WriteExisting" }));
            Assert.That(fileSystem.CreatedPaths, Is.Empty);
            Assert.That(fileSystem.WrittenDocuments, Has.Count.EqualTo(1));
            Assert.That(fileSystem.WrittenDocuments[0], Is.EqualTo(codec.Encode(ValidCurrent)));
        });
    }

    [Test]
    public void enable_with_a_missing_preprovisioned_file_is_memory_only_and_does_not_write()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ProvisionResult = FakeManualSessionRecoveryFileSystem.Failure(
                ManualRecoveryStatus.MemoryOnlyDegraded,
                "RECOVERY_NOT_PROVISIONED")
        };
        ManualSessionRecoveryStore store = new(fileSystem, new ManualSessionRecoveryCodec());

        ManualRecoverySaveResult result = store.TryEnableProvisioning(ValidCurrent);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ManualRecoveryStatus.MemoryOnlyDegraded));
            Assert.That(result.FailureCode, Is.EqualTo("RECOVERY_NOT_PROVISIONED"));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ProvisionFixedFile" }));
            Assert.That(fileSystem.WrittenDocuments, Is.Empty);
            Assert.That(fileSystem.CreatedPaths, Is.Empty);
        });
    }

    [Test]
    public void invalid_snapshot_preflight_rejects_before_save_or_enable_file_operations()
    {
        ManualSessionRecoveryPayload invalidSnapshot = ValidCurrent with { SnapshotGeneration = 0 };
        FakeManualSessionRecoveryFileSystem saveFileSystem = new();
        FakeManualSessionRecoveryFileSystem enableFileSystem = new();

        ManualRecoverySaveResult saveResult = new ManualSessionRecoveryStore(
            saveFileSystem,
            new ManualSessionRecoveryCodec()).TrySave(invalidSnapshot);
        ManualRecoverySaveResult enableResult = new ManualSessionRecoveryStore(
            enableFileSystem,
            new ManualSessionRecoveryCodec()).TryEnableProvisioning(invalidSnapshot);

        Assert.Multiple(() =>
        {
            Assert.That(saveResult.Status, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
            Assert.That(saveResult.FailureCode, Is.EqualTo("SNAPSHOT_GENERATION_INVALID"));
            Assert.That(enableResult.Status, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
            Assert.That(enableResult.FailureCode, Is.EqualTo("SNAPSHOT_GENERATION_INVALID"));
            Assert.That(saveFileSystem.Operations, Is.Empty);
            Assert.That(enableFileSystem.Operations, Is.Empty);
        });
    }

    [Test]
    public void oversized_snapshot_preflight_rejects_before_any_file_operation()
    {
        ManualSessionRecoveryPayload oversizedSnapshot = ValidCurrent with
        {
            FixtureScenarioId = new string('x', ManualSessionRecoveryContract.MaximumSnapshotBytes)
        };
        FakeManualSessionRecoveryFileSystem fileSystem = new();
        ManualSessionRecoveryStore store = new(fileSystem, new ManualSessionRecoveryCodec());

        ManualRecoverySaveResult result = store.TrySave(oversizedSnapshot);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
            Assert.That(result.FailureCode, Is.EqualTo("DOCUMENT_TOO_LARGE"));
            Assert.That(fileSystem.Operations, Is.Empty);
        });
    }

    [Test]
    public void missing_recovery_root_or_file_loads_memory_only_without_a_snapshot()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Failure(
                ManualRecoveryStatus.MemoryOnlyDegraded,
                "RECOVERY_NOT_PROVISIONED")
        };
        ManualSessionRecoveryStore store = new(fileSystem, new ManualSessionRecoveryCodec());

        ManualRecoveryLoadResult result = store.TryLoad();

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ManualRecoveryStatus.MemoryOnlyDegraded));
            Assert.That(result.Snapshot, Is.Null);
            Assert.That(result.FailureCode, Is.EqualTo("RECOVERY_NOT_PROVISIONED"));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ReadExisting" }));
            Assert.That(fileSystem.RequestedMaximumBytes,
                Is.EqualTo(new[] { ManualSessionRecoveryContract.MaximumSnapshotBytes }));
        });
    }

    [Test]
    public void corrupt_document_load_is_rejected_without_a_partial_snapshot()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(new byte[] { 0x7B, 0xFF, 0x7D })
        };
        ManualSessionRecoveryStore store = new(fileSystem, new ManualSessionRecoveryCodec());

        ManualRecoveryLoadResult result = store.TryLoad();

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
            Assert.That(result.Snapshot, Is.Null);
            Assert.That(result.FailureCode, Is.EqualTo("MALFORMED_JSON"));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ReadExisting" }));
        });
    }

    [Test]
    public void oversized_document_load_is_rejected_without_a_snapshot()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(
                new byte[ManualSessionRecoveryContract.MaximumSnapshotBytes + 1])
        };
        ManualSessionRecoveryStore store = new(fileSystem, new ManualSessionRecoveryCodec());

        ManualRecoveryLoadResult result = store.TryLoad();

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
            Assert.That(result.Snapshot, Is.Null);
            Assert.That(result.FailureCode, Is.EqualTo("DOCUMENT_TOO_LARGE"));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ReadExisting" }));
        });
    }

    [Test]
    public void successful_read_without_document_bytes_is_rejected_as_an_untrusted_result()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success()
        };
        ManualSessionRecoveryStore store = new(fileSystem, new ManualSessionRecoveryCodec());

        ManualRecoveryLoadResult result = store.TryLoad();

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
            Assert.That(result.Snapshot, Is.Null);
            Assert.That(result.FailureCode, Is.EqualTo("RECOVERY_DOCUMENT_MISSING"));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ReadExisting" }));
        });
    }

    [Test]
    public void inconsistent_read_result_contract_is_rejected_without_decoding_it()
    {
        ManualSessionRecoveryCodec codec = new();
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = new ManualRecoveryFileResult(
                Success: true,
                Status: ManualRecoveryStatus.RecoveryAvailable,
                FailureCode: "RECOVERY_READ_STALE",
                Utf8Document: codec.Encode(ValidCurrent))
        };
        ManualSessionRecoveryStore store = new(fileSystem, codec);

        ManualRecoveryLoadResult result = store.TryLoad();

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
            Assert.That(result.Snapshot, Is.Null);
            Assert.That(result.FailureCode, Is.EqualTo("FILE_SYSTEM_RESULT_INVALID"));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ReadExisting" }));
        });
    }

    [Test]
    public void unknown_file_system_failure_status_is_rejected_without_a_snapshot()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = new ManualRecoveryFileResult(
                Success: false,
                Status: (ManualRecoveryStatus)999,
                FailureCode: "RECOVERY_UNKNOWN_STATUS",
                Utf8Document: null)
        };
        ManualSessionRecoveryStore store = new(fileSystem, new ManualSessionRecoveryCodec());

        ManualRecoveryLoadResult result = store.TryLoad();

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
            Assert.That(result.Snapshot, Is.Null);
            Assert.That(result.FailureCode, Is.EqualTo("FILE_SYSTEM_RESULT_INVALID"));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ReadExisting" }));
        });
    }

    [Test]
    public void non_named_file_system_failure_code_is_rejected_without_exposing_it()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Failure(
                ManualRecoveryStatus.RecoveryRejected,
                @"X:\untrusted\failure")
        };
        ManualSessionRecoveryStore store = new(fileSystem, new ManualSessionRecoveryCodec());

        ManualRecoveryLoadResult result = store.TryLoad();

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
            Assert.That(result.Snapshot, Is.Null);
            Assert.That(result.FailureCode, Is.EqualTo("FILE_SYSTEM_RESULT_INVALID"));
            Assert.That(result.FailureCode, Does.Not.Contain("X:"));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ReadExisting" }));
        });
    }

    [Test]
    public void valid_codec_document_round_trips_through_load()
    {
        ManualSessionRecoveryCodec codec = new();
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(codec.Encode(ValidCurrent))
        };
        ManualSessionRecoveryStore store = new(fileSystem, codec);

        ManualRecoveryLoadResult result = store.TryLoad();

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ManualRecoveryStatus.RecoveryAvailable));
            Assert.That(result.Snapshot, Is.EqualTo(ValidCurrent));
            Assert.That(result.FailureCode, Is.EqualTo("NONE"));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "ReadExisting" }));
        });
    }

    [Test]
    public void write_availability_failure_is_memory_only_and_never_retries_or_falls_back()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            WriteResult = FakeManualSessionRecoveryFileSystem.Failure(
                ManualRecoveryStatus.MemoryOnlyDegraded,
                "RECOVERY_WRITE_DENIED")
        };
        ManualSessionRecoveryStore store = new(fileSystem, new ManualSessionRecoveryCodec());

        ManualRecoverySaveResult result = store.TrySave(ValidCurrent);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ManualRecoveryStatus.MemoryOnlyDegraded));
            Assert.That(result.FailureCode, Is.EqualTo("RECOVERY_WRITE_DENIED"));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "WriteExisting" }));
            Assert.That(fileSystem.WrittenDocuments, Has.Count.EqualTo(1));
            Assert.That(fileSystem.CreatedPaths, Is.Empty);
        });
    }

    [Test]
    public void write_integrity_failure_is_propagated_without_a_retry_or_fallback()
    {
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            WriteResult = FakeManualSessionRecoveryFileSystem.Failure(
                ManualRecoveryStatus.RecoveryRejected,
                "RECOVERY_FILE_LINK_COUNT_INVALID")
        };
        ManualSessionRecoveryStore store = new(fileSystem, new ManualSessionRecoveryCodec());

        ManualRecoverySaveResult result = store.TrySave(ValidCurrent);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(ManualRecoveryStatus.RecoveryRejected));
            Assert.That(result.FailureCode, Is.EqualTo("RECOVERY_FILE_LINK_COUNT_INVALID"));
            Assert.That(fileSystem.Operations, Is.EqualTo(new[] { "WriteExisting" }));
            Assert.That(fileSystem.WrittenDocuments, Has.Count.EqualTo(1));
            Assert.That(fileSystem.CreatedPaths, Is.Empty);
        });
    }

    [Test]
    public void fake_read_results_are_defensive_copies_of_configured_and_returned_bytes()
    {
        byte[] configuredDocument = [1, 2, 3];
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(configuredDocument)
        };

        configuredDocument[0] = 9;
        ManualRecoveryFileResult firstResult = fileSystem.TryReadExisting(
            ManualSessionRecoveryContract.MaximumSnapshotBytes);
        firstResult.Utf8Document![1] = 8;
        ManualRecoveryFileResult secondResult = fileSystem.TryReadExisting(
            ManualSessionRecoveryContract.MaximumSnapshotBytes);

        Assert.Multiple(() =>
        {
            Assert.That(firstResult.Utf8Document, Is.EqualTo(new byte[] { 1, 8, 3 }));
            Assert.That(secondResult.Utf8Document, Is.EqualTo(new byte[] { 1, 2, 3 }));
        });
    }

    [Test]
    public void fake_observations_never_include_c_appdata_or_temp_paths()
    {
        ManualSessionRecoveryCodec codec = new();
        FakeManualSessionRecoveryFileSystem fileSystem = new()
        {
            ReadResult = FakeManualSessionRecoveryFileSystem.Success(codec.Encode(ValidCurrent))
        };
        ManualSessionRecoveryStore store = new(fileSystem, codec);

        _ = store.TrySave(ValidCurrent);
        _ = store.TryEnableProvisioning(ValidCurrent);
        _ = store.TryLoad();

        Assert.Multiple(() =>
        {
            Assert.That(
                fileSystem.ObservedLogicalNames.Exists(static value =>
                    value.Contains("C:", StringComparison.OrdinalIgnoreCase) ||
                    value.Contains("AppData", StringComparison.OrdinalIgnoreCase) ||
                    value.Contains("Temp", StringComparison.OrdinalIgnoreCase)),
                Is.False);
            Assert.That(fileSystem.CreatedPaths, Is.Empty);
            Assert.That(fileSystem.ObservedLogicalNames,
                Is.EqualTo(new[]
                {
                    "manual-session-v1.json",
                    "manual-session-v1.json",
                    "manual-session-v1.json",
                    "manual-session-v1.json"
                }));
        });
    }

    [Test]
    public void windows_adapter_source_is_existing_only_fail_closed_and_has_no_fallback_paths()
    {
        string repositoryRoot = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "TftCompanion.SecondScreen",
            "Recovery",
            "WindowsManualSessionRecoveryFileSystem.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("manual-session-v1.json"));
            Assert.That(source, Does.Contain("FileFlagOpenReparsePoint"));
            Assert.That(source, Does.Contain("NumberOfLinks != 1"));
            Assert.That(source, Does.Contain("GetFinalPathNameByHandleW"));
            Assert.That(source, Does.Contain("OpenExisting"));
            Assert.That(source, Does.Contain("GetFileSizeEx"));
            Assert.That(source, Does.Not.Contain("CreateNew"));
            Assert.That(source, Does.Not.Contain("CREATE_NEW"));
            Assert.That(source, Does.Not.Contain("poc-status.json"));
            Assert.That(source, Does.Not.Contain("Directory.CreateDirectory"));
            Assert.That(source, Does.Not.Contain("AppData"));
            Assert.That(source, Does.Not.Contain("Temp"));
            Assert.That(source, Does.Not.Contain("@\"C:\\\""));
            Assert.That(source, Does.Not.Contain("FileMode.Create"));
            Assert.That(source, Does.Not.Contain("OpenOrCreate"));
            Assert.That(source, Does.Not.Contain("File.Move"));
            Assert.That(source, Does.Not.Contain("File.Replace"));
            Assert.That(source, Does.Not.Contain("MoveFile"));
            Assert.That(source, Does.Not.Contain("ReplaceFile"));
        });
    }

    private static ManualSessionRecoveryPayload ValidCurrent { get; } = new(
        SchemaVersion: "manual-session-v1",
        SnapshotGeneration: 11,
        ManualRunId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        HighestRevision: 4,
        SessionPhase: ManualSessionPhase.CurrentAdvice,
        FixtureScenarioId: "loss-streak-review-v1",
        Topic: ManualTopic.LossStreakReview,
        Intent: ManualIntent.PreserveLossStreak,
        HealthBand: ManualRiskBand.Medium,
        GoldBand: ManualRiskBand.High,
        CopiesBand: ManualCopiesBand.Unknown,
        UnitCostBand: ManualUnitCostBand.Unknown,
        Provenance: LocalFactProvenance.UserEntered,
        FixturePackVersion: "fixture-v1",
        CreatedAt: new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero),
        ExpiresAt: new DateTimeOffset(2026, 7, 16, 10, 2, 0, TimeSpan.Zero));

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
