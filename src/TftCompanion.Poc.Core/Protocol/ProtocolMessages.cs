namespace TftCompanion.Poc.Core.Protocol;

public enum ChannelKind
{
    Ingest,
    Render
}

public sealed record HelloMessage(
    int GameId,
    int ProtocolVersion,
    int SchemaVersion,
    ChannelKind Channel,
    string? Origin,
    string? PairingProof,
    string? BridgeInstanceId);

public sealed record WelcomeMessage(
    string SessionId,
    string RuntimeInstanceId,
    long ConnectionEpoch,
    int ProtocolVersion,
    int SchemaVersion,
    ChannelKind Channel,
    string? RenderLeaseId,
    bool ResyncRequired);

/// <summary>
/// The only ingress object that may leave the WebSocket parsing boundary in
/// v0.0.1. It intentionally contains no raw GEP object, player data or IDs.
/// </summary>
public sealed record IngressMessage(
    string Type,
    string SessionId,
    long Sequence,
    bool MatchObserved,
    bool RoundObserved,
    bool IsAuthoritativeSnapshot);

public sealed record RenderCommand(
    string Type,
    string RuntimeInstanceId,
    long ConnectionEpoch,
    long CommandSequence,
    string SessionId,
    string RenderLeaseId,
    string CommandId);

public sealed record RendererReceipt(
    string Type,
    string RuntimeInstanceId,
    long ConnectionEpoch,
    long CommandSequence,
    string SessionId,
    string RenderLeaseId,
    string CommandId,
    string ReceiptType);
