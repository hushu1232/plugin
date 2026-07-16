namespace TftCompanion.Poc.Core.Protocol;

public static class ProtocolConstants
{
    public const int GameId = 21570;
    public const int ProtocolVersion = 1;
    public const int SchemaVersion = 1;
    public const int DefaultPort = 32173;
    public const int MaximumTextFrameBytes = 65_536;
    public const int MaximumMessagesPerSecond = 30;
    public const int IngressMailboxCapacity = 128;
    public const int RenderMailboxCapacity = 16;
    public const int FreshnessTtlSeconds = 3;
    public const int MaximumStatusDocumentBytes = 8_192;
    public const int StatusWriteIntervalSeconds = 1;
    public const string LoopbackAddress = "127.0.0.1";
    public const string CanonicalStorageRoot = @"D:\AlifeData\TFTCompanion";
}
