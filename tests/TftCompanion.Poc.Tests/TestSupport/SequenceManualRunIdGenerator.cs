using TftCompanion.SecondScreen.Sessions;

namespace TftCompanion.Poc.Tests.TestSupport;

public sealed class SequenceManualRunIdGenerator : IManualRunIdGenerator
{
    private readonly Queue<Guid> runIds;

    public SequenceManualRunIdGenerator(IEnumerable<Guid> runIds)
    {
        this.runIds = new Queue<Guid>(runIds ?? throw new ArgumentNullException(nameof(runIds)));
    }

    public Guid Create() => runIds.Count == 0 ? Guid.Empty : runIds.Dequeue();
}
