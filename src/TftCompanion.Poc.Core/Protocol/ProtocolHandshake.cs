namespace TftCompanion.Poc.Core.Protocol;

public sealed class ProtocolHandshake
{
    private static readonly HashSet<string> IngestMessageTypes = ["hello", "stateSnapshot"];
    private static readonly HashSet<string> RenderMessageTypes = ["hello", "receipt"];

    private readonly OriginPolicy originPolicy;
    private readonly string expectedPairingToken;

    public ProtocolHandshake(string allowedOrigin, string expectedPairingToken)
    {
        originPolicy = new OriginPolicy(allowedOrigin);
        if (!PairingTokenValidator.IsValid(expectedPairingToken))
        {
            throw new ArgumentException("Pairing token must be a 32-byte base64url value.", nameof(expectedPairingToken));
        }

        this.expectedPairingToken = expectedPairingToken;
    }

    public bool TryValidateHello(
        HelloMessage hello,
        ChannelKind expectedChannel,
        out string failureCode)
    {
        if (hello.GameId != ProtocolConstants.GameId)
        {
            failureCode = "GAME_ID_REJECTED";
            return false;
        }

        if (hello.ProtocolVersion != ProtocolConstants.ProtocolVersion)
        {
            failureCode = "PROTOCOL_VERSION_REJECTED";
            return false;
        }

        if (hello.SchemaVersion != ProtocolConstants.SchemaVersion)
        {
            failureCode = "SCHEMA_VERSION_REJECTED";
            return false;
        }

        if (hello.Channel != expectedChannel)
        {
            failureCode = "CHANNEL_MISMATCH";
            return false;
        }

        OriginVerification originResult = originPolicy.Verify(hello.Origin);
        if (!originResult.Accepted)
        {
            failureCode = originResult.FailureCode;
            return false;
        }

        if (!PairingTokenValidator.FixedTimeEquals(expectedPairingToken, hello.PairingProof))
        {
            failureCode = "PAIRING_REJECTED";
            return false;
        }

        if (string.IsNullOrWhiteSpace(hello.BridgeInstanceId) || hello.BridgeInstanceId.Length > 128)
        {
            failureCode = "BRIDGE_INSTANCE_REJECTED";
            return false;
        }

        failureCode = "NONE";
        return true;
    }

    public bool TryValidateTextFrame(int frameByteLength, out string failureCode)
    {
        if (frameByteLength is < 0 or > ProtocolConstants.MaximumTextFrameBytes)
        {
            failureCode = "FRAME_OVERSIZED";
            return false;
        }

        failureCode = "NONE";
        return true;
    }

    public bool TryValidateChannelMessage(
        ChannelKind channel,
        string messageType,
        out string failureCode)
    {
        bool allowed = channel switch
        {
            ChannelKind.Ingest => IngestMessageTypes.Contains(messageType),
            ChannelKind.Render => RenderMessageTypes.Contains(messageType),
            _ => false
        };

        if (!allowed)
        {
            failureCode = "CHANNEL_MISMATCH";
            return false;
        }

        failureCode = "NONE";
        return true;
    }
}
