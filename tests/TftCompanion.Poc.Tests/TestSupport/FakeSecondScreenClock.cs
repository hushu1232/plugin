using TftCompanion.SecondScreen.Sessions;

namespace TftCompanion.Poc.Tests.TestSupport;

public sealed class FakeSecondScreenClock : ISecondScreenClock
{
    public FakeSecondScreenClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; set; }
}
