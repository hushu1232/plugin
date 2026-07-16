namespace TftCompanion.Poc.Core.Session;

public sealed record SessionState(
    string SessionId,
    long Epoch,
    DateTimeOffset CreatedAt,
    bool IsTerminated);

public sealed class SessionManager
{
    private long currentEpoch;
    private readonly Dictionary<string, SessionState> sessions = new();

    public SessionState CreateSession(long epoch)
    {
        if (epoch > currentEpoch)
        {
            currentEpoch = epoch;
        }

        string sessionId = Guid.NewGuid().ToString("N");
        SessionState session = new(sessionId, epoch, DateTimeOffset.UtcNow, IsTerminated: false);
        sessions[sessionId] = session;
        return session;
    }

    public void TerminateSession(string sessionId, long epoch)
    {
        if (sessions.TryGetValue(sessionId, out SessionState? session) && session.Epoch == epoch)
        {
            sessions[sessionId] = session with { IsTerminated = true };
        }
    }

    public bool IsSessionActive(string sessionId, long epoch)
    {
        if (!sessions.TryGetValue(sessionId, out SessionState? session))
        {
            return false;
        }

        return !session.IsTerminated && session.Epoch == epoch;
    }

    public void AdvanceEpoch()
    {
        currentEpoch++;

        // Terminate all sessions from previous epochs.
        foreach (string key in sessions.Keys.ToList())
        {
            SessionState session = sessions[key];
            if (session.Epoch < currentEpoch)
            {
                sessions[key] = session with { IsTerminated = true };
            }
        }
    }
}
