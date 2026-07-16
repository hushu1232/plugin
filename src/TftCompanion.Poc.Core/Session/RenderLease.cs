namespace TftCompanion.Poc.Core.Session;

public enum RenderLeasePhase
{
    HideAllIssued,
    HiddenConfirmed,
    MarkerShown,
    Shown,
    Terminated
}

public sealed record RenderLease(
    string LeaseId,
    long Epoch,
    RenderLeasePhase Phase);

public sealed class RenderLeaseManager
{
    private readonly Dictionary<string, RenderLease> leases = new();
    private string? activeLeaseId;

    public bool IsCurrentlyVisible =>
        activeLeaseId is not null &&
        leases.TryGetValue(activeLeaseId, out RenderLease? lease) &&
        lease.Phase == RenderLeasePhase.Shown;

    public RenderLease CreateLease(long epoch)
    {
        // Terminate any previous active lease.
        if (activeLeaseId is not null && leases.TryGetValue(activeLeaseId, out RenderLease? old))
        {
            leases[activeLeaseId] = old with { Phase = RenderLeasePhase.Terminated };
        }

        string leaseId = Guid.NewGuid().ToString("N");
        RenderLease lease = new(leaseId, epoch, RenderLeasePhase.HideAllIssued);
        leases[leaseId] = lease;
        activeLeaseId = leaseId;
        return lease;
    }

    public bool TryIssueHideAll(string leaseId, long epoch, out string failureCode)
    {
        if (!IsLeaseActive(leaseId, epoch, out failureCode))
        {
            return false;
        }

        // HideAll is the fail-closed transition from every non-terminated
        // phase. A stale fact, disconnect or newer lease must be able to hide
        // an already shown marker before any later renderer action occurs.
        RenderLease lease = leases[leaseId];
        if (lease.Phase is
            RenderLeasePhase.HideAllIssued or
            RenderLeasePhase.HiddenConfirmed or
            RenderLeasePhase.MarkerShown or
            RenderLeasePhase.Shown)
        {
            leases[leaseId] = lease with { Phase = RenderLeasePhase.HideAllIssued };
            failureCode = "NONE";
            return true;
        }

        failureCode = "INVALID_PHASE_FOR_HIDEALL";
        return false;
    }

    public void ConfirmHidden(string leaseId, long epoch)
    {
        if (!IsLeaseActive(leaseId, epoch, out _))
        {
            return;
        }

        RenderLease lease = leases[leaseId];
        if (lease.Phase == RenderLeasePhase.HideAllIssued)
        {
            leases[leaseId] = lease with { Phase = RenderLeasePhase.HiddenConfirmed };
        }
    }

    public bool TryShowMarker(string leaseId, long epoch, out string failureCode)
    {
        if (!IsLeaseActive(leaseId, epoch, out failureCode))
        {
            return false;
        }

        RenderLease lease = leases[leaseId];
        if (lease.Phase != RenderLeasePhase.HiddenConfirmed && lease.Phase != RenderLeasePhase.MarkerShown)
        {
            failureCode = "HIDEALL_NOT_CONFIRMED";
            return false;
        }

        leases[leaseId] = lease with { Phase = RenderLeasePhase.MarkerShown };
        failureCode = "NONE";
        return true;
    }

    public void ConfirmShown(string leaseId, long epoch)
    {
        if (!IsLeaseActive(leaseId, epoch, out _))
        {
            return;
        }

        RenderLease lease = leases[leaseId];
        if (lease.Phase == RenderLeasePhase.MarkerShown)
        {
            leases[leaseId] = lease with { Phase = RenderLeasePhase.Shown };
        }
    }

    public void TerminateLease(string leaseId, long epoch)
    {
        if (leases.TryGetValue(leaseId, out RenderLease? lease) && lease.Epoch == epoch)
        {
            leases[leaseId] = lease with { Phase = RenderLeasePhase.Terminated };
            if (activeLeaseId == leaseId)
            {
                activeLeaseId = null;
            }
        }
    }

    private bool IsLeaseActive(string leaseId, long epoch, out string failureCode)
    {
        if (!leases.TryGetValue(leaseId, out RenderLease? lease))
        {
            failureCode = "LEASE_NOT_FOUND";
            return false;
        }

        if (lease.Phase == RenderLeasePhase.Terminated)
        {
            failureCode = "LEASE_TERMINATED";
            return false;
        }

        if (lease.Epoch != epoch)
        {
            failureCode = "LEASE_EPOCH_MISMATCH";
            return false;
        }

        // Only the active lease can be operated on.
        if (leaseId != activeLeaseId)
        {
            failureCode = "LEASE_NOT_ACTIVE";
            return false;
        }

        failureCode = "NONE";
        return true;
    }
}
