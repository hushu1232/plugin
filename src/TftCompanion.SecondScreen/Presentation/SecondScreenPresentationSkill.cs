using TftCompanion.Poc.Core.LocalSimulation;
using TftCompanion.SecondScreen.Recovery;
using TftCompanion.SecondScreen.Sessions;

namespace TftCompanion.SecondScreen.Presentation;

public sealed record SecondScreenPresentation(
    bool IsCurrentAdviceVisible,
    string? AdviceText,
    string StatusText,
    LocalPrecisionState Precision,
    DateTimeOffset? ExpiresAt,
    string SourceLabel,
    string FixturePackVersion);

public sealed class SecondScreenPresentationSkill
{
    private const string StartSession = "Start a manual fixture session to begin.";
    private const string CompleteCheckpoint = "Complete the bounded checkpoint fields before submitting.";
    private const string Cleared = "The current advice was cleared.";
    private const string Expired = "The current advice expired and is no longer shown.";
    private const string RecoveryAvailable = "D: recovery is available.";
    private const string MemoryOnly = "This session is in memory only and will not be restored after closing.";
    private const string RecoveryRejected = "The saved recovery snapshot was rejected; no prior advice was restored.";

    public SecondScreenPresentation Present(SecondScreenSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        ManualPanelProjection projection = state.Projection;
        bool visible = projection.IsCurrent;

        return new SecondScreenPresentation(
            visible,
            visible ? projection.RenderedText : null,
            StatusFor(state),
            projection.Precision,
            projection.ExpiresAt,
            projection.SourceLabel,
            projection.FixturePackVersion);
    }

    private static string StatusFor(SecondScreenSessionState state)
    {
        if (state.Notice == ManualSessionNotice.IncompleteCheckpoint)
        {
            return CompleteCheckpoint;
        }

        if (state.SessionPhase == ManualSessionPhase.Cleared || state.Notice == ManualSessionNotice.Cleared)
        {
            return Cleared;
        }

        if (state.SessionPhase == ManualSessionPhase.Expired || state.Notice == ManualSessionNotice.Expired)
        {
            return Expired;
        }

        if (state.RecoveryStatus == ManualRecoveryStatus.RecoveryRejected ||
            state.Notice == ManualSessionNotice.RecoveryRejected)
        {
            return RecoveryRejected;
        }

        if (state.RecoveryStatus == ManualRecoveryStatus.MemoryOnlyDegraded ||
            state.Notice == ManualSessionNotice.RecoveryUnavailable)
        {
            return MemoryOnly;
        }

        if (state.Notice == ManualSessionNotice.RecoveryEnabled ||
            (state.RecoveryStatus == ManualRecoveryStatus.RecoveryAvailable &&
                state.SessionPhase != ManualSessionPhase.NoSession))
        {
            return RecoveryAvailable;
        }

        return StartSession;
    }
}
