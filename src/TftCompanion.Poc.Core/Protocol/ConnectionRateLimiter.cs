namespace TftCompanion.Poc.Core.Protocol;

/// <summary>
/// Fixed-window admission limiter used per physical WebSocket connection.
/// It is intentionally small and deterministic: overload is rejected at the
/// transport boundary instead of accumulating a retry queue or event spool.
/// </summary>
public sealed class ConnectionRateLimiter
{
    private readonly int maximumMessagesPerWindow;
    private readonly TimeSpan window;
    private readonly Func<DateTimeOffset> clock;
    private DateTimeOffset? windowStartedAt;
    private int acceptedInWindow;

    public ConnectionRateLimiter(
        int maximumMessagesPerWindow,
        TimeSpan window,
        Func<DateTimeOffset> clock)
    {
        if (maximumMessagesPerWindow <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumMessagesPerWindow));
        }

        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        this.maximumMessagesPerWindow = maximumMessagesPerWindow;
        this.window = window;
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool TryAccept()
    {
        DateTimeOffset now = clock();
        if (windowStartedAt is null || now - windowStartedAt.Value >= window)
        {
            windowStartedAt = now;
            acceptedInWindow = 0;
        }

        if (acceptedInWindow >= maximumMessagesPerWindow)
        {
            return false;
        }

        acceptedInWindow++;
        return true;
    }
}
