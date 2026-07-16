namespace TftCompanion.Poc.Core.Session;

public enum GapState
{
    None,
    ResyncRequested
}

public sealed class GapDetector
{
    private long? lastSequenceNumber;

    public GapState CurrentGapState { get; private set; } = GapState.None;

    public void RecordSequenceNumber(long sequenceNumber)
    {
        if (lastSequenceNumber is null)
        {
            lastSequenceNumber = sequenceNumber;
            CurrentGapState = GapState.None;
            return;
        }

        if (sequenceNumber > lastSequenceNumber.Value + 1)
        {
            CurrentGapState = GapState.ResyncRequested;
        }
        else if (sequenceNumber == lastSequenceNumber.Value + 1 || sequenceNumber <= lastSequenceNumber.Value)
        {
            CurrentGapState = GapState.None;
        }

        if (sequenceNumber > lastSequenceNumber.Value)
        {
            lastSequenceNumber = sequenceNumber;
        }
    }

    public void ClearResync()
    {
        CurrentGapState = GapState.None;
    }

    public void Reset()
    {
        lastSequenceNumber = null;
        CurrentGapState = GapState.None;
    }
}
