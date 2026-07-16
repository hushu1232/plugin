using NUnit.Framework;
using TftCompanion.Poc.Core.Protocol;
using TftCompanion.Poc.Core.Session;

namespace TftCompanion.Poc.Tests;

[TestFixture]
public sealed class StateMachineTests
{
    [Test]
    public void valid_session_creation_does_not_revive_terminated_session()
    {
        SessionManager manager = new();
        SessionState sessionA = manager.CreateSession(epoch: 1);

        manager.TerminateSession(sessionA.SessionId, epoch: 1);
        SessionState sessionB = manager.CreateSession(epoch: 2);

        Assert.Multiple(() =>
        {
            Assert.That(sessionB.SessionId, Is.Not.EqualTo(sessionA.SessionId));
            Assert.That(manager.IsSessionActive(sessionA.SessionId, epoch: 1), Is.False);
            Assert.That(manager.IsSessionActive(sessionB.SessionId, epoch: 2), Is.True);
        });
    }

    [Test]
    public void freshness_ttl_expires()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        FreshnessTracker tracker = new(() => now);

        tracker.RecordObservation();
        Assert.That(tracker.CurrentFreshness, Is.EqualTo(FreshnessKind.Fresh));

        now = now.AddSeconds(ProtocolConstants.FreshnessTtlSeconds + 1);
        Assert.That(tracker.CurrentFreshness, Is.EqualTo(FreshnessKind.Stale));
    }

    [Test]
    public void gap_detection_enters_resync_requested()
    {
        GapDetector detector = new();

        detector.RecordSequenceNumber(1);
        Assert.That(detector.CurrentGapState, Is.EqualTo(GapState.None));

        detector.RecordSequenceNumber(3);
        Assert.That(detector.CurrentGapState, Is.EqualTo(GapState.ResyncRequested));
    }

    [Test]
    public void render_lease_creation_enforces_hideall_first()
    {
        RenderLeaseManager manager = new();
        RenderLease lease = manager.CreateLease(epoch: 1);

        // ShowMarker before HideAll confirmed must be rejected.
        bool showBeforeHidden = manager.TryShowMarker(lease.LeaseId, epoch: 1, out _);
        Assert.That(showBeforeHidden, Is.False);

        // Issue HideAll.
        bool hideAll = manager.TryIssueHideAll(lease.LeaseId, epoch: 1, out _);
        Assert.That(hideAll, Is.True);

        // ShowMarker still rejected before Hidden receipt.
        bool showBeforeReceipt = manager.TryShowMarker(lease.LeaseId, epoch: 1, out _);
        Assert.That(showBeforeReceipt, Is.False);

        // Confirm Hidden.
        manager.ConfirmHidden(lease.LeaseId, epoch: 1);

        // Now ShowMarker is allowed.
        bool showAfterHidden = manager.TryShowMarker(lease.LeaseId, epoch: 1, out _);
        Assert.That(showAfterHidden, Is.True);
    }

    [Test]
    public void render_lease_creation_does_not_revive_terminated_lease()
    {
        RenderLeaseManager manager = new();
        RenderLease leaseA = manager.CreateLease(epoch: 1);

        manager.TerminateLease(leaseA.LeaseId, epoch: 1);
        RenderLease leaseB = manager.CreateLease(epoch: 2);

        Assert.Multiple(() =>
        {
            Assert.That(leaseB.LeaseId, Is.Not.EqualTo(leaseA.LeaseId));
            bool revived = manager.TryShowMarker(leaseA.LeaseId, epoch: 1, out _);
            Assert.That(revived, Is.False);
        });
    }

    [Test]
    public void old_epoch_receipt_does_not_revive_current_visibility()
    {
        RenderLeaseManager manager = new();

        // Epoch 1: full cycle to Shown.
        RenderLease lease1 = manager.CreateLease(epoch: 1);
        manager.TryIssueHideAll(lease1.LeaseId, epoch: 1, out _);
        manager.ConfirmHidden(lease1.LeaseId, epoch: 1);
        manager.TryShowMarker(lease1.LeaseId, epoch: 1, out _);
        manager.ConfirmShown(lease1.LeaseId, epoch: 1);

        // Epoch 2: new connection, new lease, HideAll issued but not yet confirmed.
        manager.TerminateLease(lease1.LeaseId, epoch: 1);
        RenderLease lease2 = manager.CreateLease(epoch: 2);
        manager.TryIssueHideAll(lease2.LeaseId, epoch: 2, out _);

        // Stale receipt from epoch 1 arrives.
        manager.ConfirmShown(lease1.LeaseId, epoch: 1);

        // Current visibility must remain hidden (waiting for epoch 2 Hidden).
        Assert.That(manager.IsCurrentlyVisible, Is.False);
    }

    [Test]
    public void session_terminated_when_epoch_advances()
    {
        SessionManager manager = new();
        SessionState session = manager.CreateSession(epoch: 1);

        manager.AdvanceEpoch();

        Assert.That(manager.IsSessionActive(session.SessionId, epoch: 1), Is.False);
    }
}
