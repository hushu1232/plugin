using NUnit.Framework;
using TftCompanion.Poc.Core.Protocol;
using TftCompanion.Poc.Core.Session;
using TftCompanion.Poc.Core.Storage;
using TftCompanion.Poc.Host.Channels;
using TftCompanion.Poc.Tests.TestSupport;

namespace TftCompanion.Poc.Tests;

/// <summary>
/// Cross-boundary security tests that verify safety invariants spanning
/// storage, protocol, session, and render lease boundaries.
/// </summary>
[TestFixture]
public sealed class CrossBoundarySecurityTests
{
    [Test]
    public void storage_root_policy_never_falls_back_to_c_drive_or_appdata()
    {
        // D-drive unavailable → memory-only, no writes anywhere.
        FakeStorageFileSystem fileSystem = FakeStorageFileSystem.Unavailable("D_ROOT_UNAVAILABLE");
        StorageRootPolicy policy = new(fileSystem);

        StorageHealth health = policy.Initialize();
        bool persisted = policy.TryPersist(SanitizedPocStatus.Empty("epoch-1"));

        Assert.Multiple(() =>
        {
            Assert.That(health, Is.EqualTo(StorageHealth.MemoryOnlyDegraded));
            Assert.That(persisted, Is.False);
            Assert.That(fileSystem.WriteAttempts, Is.Empty);
        });
    }

    [Test]
    public void poc_status_json_does_not_contain_raw_gep_payload_or_identity_fields()
    {
        FakeStorageFileSystem fileSystem = FakeStorageFileSystem.ValidRoot();
        StorageRootPolicy policy = new(fileSystem);
        policy.Initialize();

        SanitizedPocStatus status = new(
            RuntimeEpoch: "epoch-2",
            BridgeOnline: true,
            RenderOnline: true,
            MatchObserved: true,
            RoundObserved: true,
            Freshness: "Fresh",
            GapState: "None",
            LastErrorCode: "NONE");

        policy.TryPersist(status);
        string json = fileSystem.LastUtf8Document!;

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("payload"));
            Assert.That(json, Does.Not.Contain("pseudo_match"));
            Assert.That(json, Does.Not.Contain("summoner"));
            Assert.That(json, Does.Not.Contain("token"));
            Assert.That(json, Does.Not.Contain("origin"));
            Assert.That(json, Does.Not.Contain("url"));
            Assert.That(json, Does.Not.Contain("console"));
        });
    }

    [Test]
    public void ingress_burst_does_not_block_hideall_on_render_mailbox()
    {
        IngressMailbox ingressMailbox = new(ProtocolConstants.IngressMailboxCapacity, "session-security");
        RenderMailbox renderMailbox = new(ProtocolConstants.RenderMailboxCapacity);
        // Fill ingress mailbox to capacity.
        for (int i = 0; i < ProtocolConstants.IngressMailboxCapacity; i++)
        {
            ingressMailbox.TryWrite(CreateIngressEnvelope(i));
        }

        // HideAll on render mailbox must succeed immediately.
        Assert.That(
            renderMailbox.TryWriteHideAll(
                "runtime-security",
                1,
                1,
                "session-security",
                "lease-security",
                "command-security"),
            Is.True);
    }

    [Test]
    public void old_render_lease_does_not_revive_visibility_after_epoch_advance()
    {
        RenderLeaseManager manager = new();

        // Epoch 1: full cycle to Shown.
        RenderLease lease1 = manager.CreateLease(epoch: 1);
        manager.TryIssueHideAll(lease1.LeaseId, epoch: 1, out _);
        manager.ConfirmHidden(lease1.LeaseId, epoch: 1);
        manager.TryShowMarker(lease1.LeaseId, epoch: 1, out _);
        manager.ConfirmShown(lease1.LeaseId, epoch: 1);
        Assert.That(manager.IsCurrentlyVisible, Is.True);

        // Epoch 2: new connection, new lease, HideAll issued.
        manager.TerminateLease(lease1.LeaseId, epoch: 1);
        RenderLease lease2 = manager.CreateLease(epoch: 2);
        manager.TryIssueHideAll(lease2.LeaseId, epoch: 2, out _);

        // Stale Shown receipt from epoch 1 must not revive visibility.
        manager.ConfirmShown(lease1.LeaseId, epoch: 1);
        Assert.That(manager.IsCurrentlyVisible, Is.False);

        // Stale ShowMarker command from epoch 1 must be rejected.
        bool revived = manager.TryShowMarker(lease1.LeaseId, epoch: 1, out _);
        Assert.That(revived, Is.False);
    }

    [Test]
    public void terminated_session_cannot_be_revived_by_any_operation()
    {
        SessionManager manager = new();
        SessionState session = manager.CreateSession(epoch: 5);
        manager.TerminateSession(session.SessionId, epoch: 5);

        Assert.Multiple(() =>
        {
            Assert.That(manager.IsSessionActive(session.SessionId, epoch: 5), Is.False);
            Assert.That(manager.IsSessionActive(session.SessionId, epoch: 99), Is.False);
        });
    }

    [Test]
    public void protocol_handshake_rejects_all_unauthorized_channels_and_origins()
    {
        const string allowedOrigin = "overwolf-tool://tft-companion-poc";
        const string pairingToken = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        ProtocolHandshake handshake = new(allowedOrigin, pairingToken);

        // Wrong game.
        HelloMessage wrongGame = new(99999, 1, 1, ChannelKind.Ingest, allowedOrigin, pairingToken, "bridge-security");
        Assert.That(handshake.TryValidateHello(wrongGame, ChannelKind.Ingest, out _), Is.False);

        // Foreign origin.
        HelloMessage foreignOrigin = new(ProtocolConstants.GameId, 1, 1, ChannelKind.Ingest, "https://evil.example.com", pairingToken, "bridge-security");
        Assert.That(handshake.TryValidateHello(foreignOrigin, ChannelKind.Ingest, out _), Is.False);

        // Missing origin.
        HelloMessage missingOrigin = new(ProtocolConstants.GameId, 1, 1, ChannelKind.Ingest, null, pairingToken, "bridge-security");
        Assert.That(handshake.TryValidateHello(missingOrigin, ChannelKind.Ingest, out _), Is.False);

        // Wrong protocol version.
        HelloMessage wrongVersion = new(ProtocolConstants.GameId, 99, 1, ChannelKind.Ingest, allowedOrigin, pairingToken, "bridge-security");
        Assert.That(handshake.TryValidateHello(wrongVersion, ChannelKind.Ingest, out _), Is.False);
    }

    [Test]
    public void text_frame_above_maximum_is_rejected_at_boundary()
    {
        ProtocolHandshake handshake = new(
            "overwolf-tool://tft-companion-poc",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        bool accepted = handshake.TryValidateTextFrame(
            ProtocolConstants.MaximumTextFrameBytes + 1,
            out string failureCode);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.False);
            Assert.That(failureCode, Is.EqualTo("FRAME_OVERSIZED"));
        });
    }

    private static IngressEnvelope CreateIngressEnvelope(int index) => new(
        "session-security",
        new IngressMessage(
            "stateSnapshot",
            "session-security",
            index + 1L,
            MatchObserved: true,
            RoundObserved: true,
            IsAuthoritativeSnapshot: true));
}
