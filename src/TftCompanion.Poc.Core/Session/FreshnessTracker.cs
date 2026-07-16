using TftCompanion.Poc.Core.Protocol;

namespace TftCompanion.Poc.Core.Session;

public enum FreshnessKind
{
    Fresh,
    Stale,
    Unknown
}

public sealed class FreshnessTracker
{
    private readonly Func<DateTimeOffset> clock;
    private DateTimeOffset? lastObservation;

    public FreshnessTracker(Func<DateTimeOffset> clock)
    {
        this.clock = clock;
    }

    public FreshnessKind CurrentFreshness
    {
        get
        {
            if (lastObservation is null)
            {
                return FreshnessKind.Unknown;
            }

            DateTimeOffset now = clock();
            TimeSpan elapsed = now - lastObservation.Value;

            return elapsed.TotalSeconds <= ProtocolConstants.FreshnessTtlSeconds
                ? FreshnessKind.Fresh
                : FreshnessKind.Stale;
        }
    }

    public void RecordObservation()
    {
        lastObservation = clock();
    }

    public void Reset()
    {
        lastObservation = null;
    }
}
