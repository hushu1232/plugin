using System.Threading.Channels;
using TftCompanion.Poc.Core.Protocol;

namespace TftCompanion.Poc.Host.Channels;

/// <summary>
/// A bounded semantic ingress queue scoped to a single session. Raw
/// WebSocket/GEP JSON is deliberately excluded from this type so it cannot
/// leak into the runtime domain. Envelopes from a foreign session are
/// rejected at the mailbox boundary so they can neither occupy capacity
/// nor be drained into the wrong runtime.
/// </summary>
public sealed record IngressEnvelope(
    string SessionId,
    IngressMessage Snapshot);

public sealed class IngressMailbox
{
    private readonly Channel<IngressEnvelope> channel;
    private readonly int capacity;
    private readonly string sessionId;

    public IngressMailbox(int capacity, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        this.capacity = capacity;
        this.sessionId = sessionId;
        channel = Channel.CreateBounded<IngressEnvelope>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
    }

    public bool TryWrite(IngressEnvelope envelope)
    {
        if (!string.Equals(envelope.SessionId, sessionId, StringComparison.Ordinal))
        {
            return false;
        }

        if (channel.Reader.Count >= capacity)
        {
            return false;
        }

        return channel.Writer.TryWrite(envelope);
    }

    public bool TryRead(out IngressEnvelope? envelope) =>
        channel.Reader.TryRead(out envelope);
}
