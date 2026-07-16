using System.Threading.Channels;

namespace TftCompanion.Poc.Host.Channels;

public sealed record RenderCommandEnvelope(
    string CommandType,
    string RuntimeInstanceId,
    long ConnectionEpoch,
    long CommandSequence,
    string SessionId,
    string RenderLeaseId,
    string CommandId);

public sealed class RenderMailbox
{
    private readonly Channel<RenderCommandEnvelope> channel;
    private readonly int capacity;

    public RenderMailbox(int capacity)
    {
        this.capacity = capacity;
        channel = Channel.CreateBounded<RenderCommandEnvelope>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
    }

    public bool TryWriteHideAll(
        string runtimeInstanceId,
        long connectionEpoch,
        long commandSequence,
        string sessionId,
        string renderLeaseId,
        string commandId) =>
        TryWrite(new RenderCommandEnvelope(
            "hideAll",
            runtimeInstanceId,
            connectionEpoch,
            commandSequence,
            sessionId,
            renderLeaseId,
            commandId));

    public bool TryWriteShowMarker(
        string runtimeInstanceId,
        long connectionEpoch,
        long commandSequence,
        string sessionId,
        string renderLeaseId,
        string commandId) =>
        TryWrite(new RenderCommandEnvelope(
            "showMarker",
            runtimeInstanceId,
            connectionEpoch,
            commandSequence,
            sessionId,
            renderLeaseId,
            commandId));

    private bool TryWrite(RenderCommandEnvelope envelope)
    {
        if (channel.Reader.Count >= capacity)
        {
            return false;
        }

        return channel.Writer.TryWrite(envelope);
    }

    public bool TryRead(out RenderCommandEnvelope? envelope) =>
        channel.Reader.TryRead(out envelope);
}
