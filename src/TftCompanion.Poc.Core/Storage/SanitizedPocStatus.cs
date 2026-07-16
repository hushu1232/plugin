namespace TftCompanion.Poc.Core.Storage;

public sealed record SanitizedPocStatus(
    string RuntimeEpoch,
    bool BridgeOnline,
    bool RenderOnline,
    bool MatchObserved,
    bool RoundObserved,
    string Freshness,
    string GapState,
    string LastErrorCode)
{
    public static SanitizedPocStatus Empty(string runtimeEpoch) => new(
        runtimeEpoch,
        BridgeOnline: false,
        RenderOnline: false,
        MatchObserved: false,
        RoundObserved: false,
        Freshness: "Unknown",
        GapState: "None",
        LastErrorCode: "NONE");
}
